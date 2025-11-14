using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClopWindows.Core.Optimizers;
using ClopWindows.Core.Settings;
using ClopWindows.Core.Shared;
using Microsoft.Extensions.Logging;

namespace ClopWindows.BackgroundService.Automation;

[SupportedOSPlatform("windows")]
public sealed class ShortcutsBridge : IAsyncDisposable
{
    private const string PipeName = "clop-automation";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly TimeSpan ShortcutsCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ClientReadTimeout = TimeSpan.FromSeconds(30);

    private readonly OptimisationCoordinator _coordinator;
    private readonly DirectoryOptimisationService _directoryOptimisations;
    private readonly ILogger<ShortcutsBridge> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _cacheGate = new();

    private IReadOnlyList<ShortcutDescriptor> _cachedShortcuts = Array.Empty<ShortcutDescriptor>();
    private DateTimeOffset _shortcutsCachedAt = DateTimeOffset.MinValue;

    public ShortcutsBridge(OptimisationCoordinator coordinator, DirectoryOptimisationService directoryOptimisations, ILogger<ShortcutsBridge> logger)
    {
        _coordinator = coordinator;
        _directoryOptimisations = directoryOptimisations;
        _logger = logger;
        SettingsHost.EnsureInitialized();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = linkedSource.Token;

        var listenerTask = Task.Run(() => ListenLoopAsync(token), CancellationToken.None);

        try
        {
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }

        linkedSource.Cancel();

        try
        {
            await listenerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Shortcuts bridge listener exited due to IO error during shutdown.");
        }
    }

    public ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await using var server = CreatePipeServer();
            try
            {
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Automation pipe listener WaitForConnection failed.");
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(server, token), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        connectionCts.CancelAfter(ClientReadTimeout);
        var connectionToken = connectionCts.Token;

        try
        {
            while (!connectionToken.IsCancellationRequested && server.IsConnected)
            {
                var requestJson = await ReadMessageAsync(server, connectionToken).ConfigureAwait(false);
                if (requestJson is null)
                {
                    break;
                }

                AutomationEnvelope? envelope = null;
                try
                {
                    envelope = JsonSerializer.Deserialize<AutomationEnvelope>(requestJson, SerializerOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid automation payload received: {Payload}", requestJson);
                    var errorResponse = AutomationResponse.Error("invalid_payload", "Request body was not recognised.");
                    await WriteResponseAsync(server, errorResponse, token).ConfigureAwait(false);
                    continue;
                }

                if (envelope is null || string.IsNullOrWhiteSpace(envelope.Intent))
                {
                    var errorResponse = AutomationResponse.Error("invalid_request", "Missing intent.");
                    await WriteResponseAsync(server, errorResponse, token).ConfigureAwait(false);
                    continue;
                }

                var response = await HandleEnvelopeAsync(envelope, token).ConfigureAwait(false);
                await WriteResponseAsync(server, response, token).ConfigureAwait(false);

                if (!envelope.KeepAlive)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // timeout/cancellation per-connection
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Automation pipe client disconnected unexpectedly.");
        }
        finally
        {
            try
            {
                if (server.IsConnected)
                {
                    server.Disconnect();
                }
            }
            catch
            {
                // ignore shutdown errors
            }
            server.Dispose();
        }
    }

    private async Task<AutomationResponse> HandleEnvelopeAsync(AutomationEnvelope envelope, CancellationToken token)
    {
        try
        {
            return envelope.Intent switch
            {
                AutomationIntents.Ping => AutomationResponse.Success(data: new { message = "pong", time = DateTimeOffset.UtcNow }),
                AutomationIntents.ListShortcuts => AutomationResponse.Success(data: new { shortcuts = GetCachedShortcuts() }),
                AutomationIntents.GetStatus => AutomationResponse.Success(data: BuildStatus()),
                AutomationIntents.PauseAutomation => await HandlePauseResumeAsync(paused: true).ConfigureAwait(false),
                AutomationIntents.ResumeAutomation => await HandlePauseResumeAsync(paused: false).ConfigureAwait(false),
                AutomationIntents.Optimise => await HandleOptimiseAsync(envelope, token).ConfigureAwait(false),
                _ => AutomationResponse.Error("unknown_intent", $"Intent '{envelope.Intent}' is not supported.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation intent {Intent} crashed.", envelope.Intent);
            return AutomationResponse.Error("internal_error", ex.Message);
        }
    }

    private async Task<AutomationResponse> HandleOptimiseAsync(AutomationEnvelope envelope, CancellationToken token)
    {
        if (envelope.Payload is null)
        {
            return AutomationResponse.Error("invalid_payload", "Payload is required for optimise intent.");
        }

        AutomationOptimisePayload? payload;
        try
        {
            payload = envelope.Payload.Value.Deserialize<AutomationOptimisePayload>(SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse optimise payload: {Payload}", envelope.Payload);
            return AutomationResponse.Error("invalid_payload", "Optimise payload was not recognised.");
        }

        if (payload is null || payload.Paths.Count == 0)
        {
            return AutomationResponse.Error("invalid_payload", "At least one path is required.");
        }

        var targets = ResolveTargets(payload);
        if (targets.Count == 0)
        {
            return AutomationResponse.Error("not_found", "No optimisable files were located for the supplied paths.");
        }

        var tickets = new List<(OptimisationTicket Ticket, AutomationTarget Target)>();
        var metadataBase = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source"] = "automation",
            ["automation.intent"] = envelope.Intent
        };

        if (!string.IsNullOrWhiteSpace(envelope.RequestId))
        {
            metadataBase["automation.requestId"] = envelope.RequestId;
        }

        if (payload.Aggressive)
        {
            metadataBase["automation.aggressive"] = true;
        }

        if (payload.RemoveAudio)
        {
            metadataBase["automation.removeAudio"] = true;
        }

        if (payload.PlaybackSpeedFactor.HasValue)
        {
            metadataBase["automation.playbackSpeed"] = payload.PlaybackSpeedFactor.Value;
        }

        foreach (var target in targets)
        {
            var metadata = new Dictionary<string, object?>(metadataBase, StringComparer.Ordinal)
            {
                ["automation.targetType"] = target.Type.ToString()
            };

            var request = new OptimisationRequest(target.Type, target.Path, metadata: metadata);
            var ticket = _coordinator.Enqueue(request);
            tickets.Add((ticket, target));
        }

        if (tickets.Count == 0)
        {
            return AutomationResponse.Error("unsupported", "No supported media types were found.");
        }

        var completions = await Task.WhenAll(tickets.Select(t => t.Ticket.Completion)).ConfigureAwait(false);
        var results = new List<AutomationOptimiseResult>(completions.Length);
        var failures = 0;

        for (var i = 0; i < completions.Length; i++)
        {
            var result = completions[i];
            var target = tickets[i].Target;

            if (result.Status == OptimisationStatus.Succeeded)
            {
                _directoryOptimisations.RegisterExternalOptimisation(target.Path);
            }

            results.Add(new AutomationOptimiseResult(
                RequestId: result.RequestId,
                SourcePath: target.Path.Value,
                OutputPath: result.OutputPath?.Value,
                Status: result.Status.ToString(),
                Message: result.Message));

            if (result.Status != OptimisationStatus.Succeeded)
            {
                failures++;
            }
        }

        var outcome = failures == 0 ? "ok" : failures == tickets.Count ? "failed" : "partial";
        return AutomationResponse.Success(status: outcome, data: new { results });
    }

    private IReadOnlyList<AutomationTarget> ResolveTargets(AutomationOptimisePayload payload)
    {
        var results = new List<AutomationTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filter = new TypeFilter(payload.IncludeTypes, payload.ExcludeTypes);

        foreach (var path in payload.Paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var expanded = ExpandPath(path);
            if (string.IsNullOrWhiteSpace(expanded))
            {
                continue;
            }

            if (Directory.Exists(expanded))
            {
                var enumerationOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = payload.Recursive,
                    IgnoreInaccessible = true,
                    MatchCasing = MatchCasing.CaseInsensitive
                };

                foreach (var file in Directory.EnumerateFiles(expanded, "*", enumerationOptions))
                {
                    AddCandidate(file);
                }
            }
            else if (File.Exists(expanded))
            {
                AddCandidate(expanded);
            }
            else
            {
                _logger.LogDebug("Automation request skipped missing item '{Item}'.", path);
            }
        }

        return results;

        void AddCandidate(string candidate)
        {
            if (!seen.Add(candidate))
            {
                return;
            }

            AutomationTarget? target = null;
            try
            {
                var filePath = FilePath.From(candidate);
                if (IsWithinWorkRoot(filePath))
                {
                    return;
                }

                var extension = filePath.Extension;
                if (!filter.Allows(extension))
                {
                    return;
                }

                ItemType? type = DetermineItemType(filePath);
                if (type is null)
                {
                    return;
                }

                target = new AutomationTarget(filePath, type.Value);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to process automation candidate {Candidate}.", candidate);
            }

            if (target is not null)
            {
                results.Add(target.Value);
            }
        }
    }

    private static ItemType? DetermineItemType(FilePath path)
    {
        if (MediaFormats.IsImage(path))
        {
            return ItemType.Image;
        }

        if (MediaFormats.IsVideo(path))
        {
            return ItemType.Video;
        }

        if (MediaFormats.IsPdf(path))
        {
            return ItemType.Pdf;
        }

        return null;
    }

    private static string ExpandPath(string input)
    {
        var value = input.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = Environment.ExpandEnvironmentVariables(value);
        if (value.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            value = Path.Combine(home, value[2..]);
        }

        return Path.GetFullPath(value);
    }

    private static bool IsWithinWorkRoot(FilePath path)
    {
        var workRoot = ClopPaths.WorkRoot.Value;
        if (path.Value.Equals(workRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Path.EndsInDirectorySeparator(workRoot))
        {
            workRoot += Path.DirectorySeparatorChar;
        }

        return path.Value.StartsWith(workRoot, StringComparison.OrdinalIgnoreCase);
    }

    private Task<AutomationResponse> HandlePauseResumeAsync(bool paused)
    {
        SettingsHost.Set(SettingsRegistry.PauseAutomaticOptimisations, paused);
        return Task.FromResult(AutomationResponse.Success(data: new { paused }));
    }

    private object BuildStatus()
    {
        return new
        {
            paused = SettingsHost.Get(SettingsRegistry.PauseAutomaticOptimisations),
            clipboard = new
            {
                enabled = SettingsHost.Get(SettingsRegistry.EnableClipboardOptimiser)
            },
            watchers = new
            {
                images = new
                {
                    enabled = SettingsHost.Get(SettingsRegistry.EnableAutomaticImageOptimisations),
                    directories = SettingsHost.Get(SettingsRegistry.ImageDirs) ?? Array.Empty<string>()
                },
                videos = new
                {
                    enabled = SettingsHost.Get(SettingsRegistry.EnableAutomaticVideoOptimisations),
                    directories = SettingsHost.Get(SettingsRegistry.VideoDirs) ?? Array.Empty<string>()
                },
                pdfs = new
                {
                    enabled = SettingsHost.Get(SettingsRegistry.EnableAutomaticPdfOptimisations),
                    directories = SettingsHost.Get(SettingsRegistry.PdfDirs) ?? Array.Empty<string>()
                }
            }
        };
    }

    private IReadOnlyList<ShortcutDescriptor> GetCachedShortcuts()
    {
        lock (_cacheGate)
        {
            if ((DateTimeOffset.UtcNow - _shortcutsCachedAt) > ShortcutsCacheTtl)
            {
                _cachedShortcuts = BuildShortcutCatalogue();
                _shortcutsCachedAt = DateTimeOffset.UtcNow;
            }

            return _cachedShortcuts;
        }
    }

    private static IReadOnlyList<ShortcutDescriptor> BuildShortcutCatalogue() => new List<ShortcutDescriptor>
    {
        new("clop.optimise-files", "Optimise Files", "Optimise images, videos, and PDFs with Clop's presets.", "Optimisation"),
        new("clop.optimise-images", "Optimise Images", "Optimise image files while preserving metadata.", "Optimisation"),
        new("clop.optimise-videos", "Optimise Videos", "Optimise video files with Clop's encoding presets.", "Optimisation"),
        new("clop.pause-automation", "Pause Automation", "Temporarily pause clipboard and directory automation.", "Automation"),
        new("clop.resume-automation", "Resume Automation", "Resume clipboard and directory automation.", "Automation")
    };

    private static async Task<string?> ReadMessageAsync(NamedPipeServerStream server, CancellationToken token)
    {
        var buffer = new byte[4096];
        var builder = new StringBuilder();

        do
        {
            var read = await server.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
            if (read == 0)
            {
                return builder.Length == 0 ? null : builder.ToString();
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
        while (!server.IsMessageComplete);

        return builder.ToString();
    }

    private static Task WriteResponseAsync(NamedPipeServerStream server, AutomationResponse response, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(response, SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return server.WriteAsync(bytes.AsMemory(0, bytes.Length), token).AsTask();
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
    }

    private readonly record struct AutomationTarget(FilePath Path, ItemType Type);

    private readonly record struct AutomationOptimiseResult(string RequestId, string SourcePath, string? OutputPath, string Status, string? Message);

    private sealed record AutomationEnvelope
    {
        public string Intent { get; init; } = string.Empty;
        public string? RequestId { get; init; }
        public bool KeepAlive { get; init; }
        public JsonElement? Payload { get; init; }
    }

    private sealed record AutomationOptimisePayload
    {
        public List<string> Paths { get; init; } = new();
        public bool Recursive { get; init; }
        public bool Aggressive { get; init; }
        public bool RemoveAudio { get; init; }
        public double? PlaybackSpeedFactor { get; init; }
        public List<string> IncludeTypes { get; init; } = new();
        public List<string> ExcludeTypes { get; init; } = new();
    }

    private sealed record AutomationResponse
    {
        public string Status { get; init; } = "ok";
        public string? Message { get; init; }
        public object? Data { get; init; }

        public static AutomationResponse Success(string status = "ok", object? data = null, string? message = null) =>
            new() { Status = status, Data = data, Message = message };

        public static AutomationResponse Error(string status, string? message = null, object? data = null) =>
            new() { Status = status, Message = message, Data = data };
    }

    private sealed record ShortcutDescriptor(string Identifier, string Title, string Description, string Category);

    private sealed class TypeFilter
    {
        private readonly HashSet<string>? _allowed;
        private readonly HashSet<string> _excluded;

        public TypeFilter(IEnumerable<string> include, IEnumerable<string> exclude)
        {
            _allowed = BuildSet(include);
            _excluded = BuildSet(exclude) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool Allows(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            extension = extension.TrimStart('.');
            if (_excluded.Contains(extension))
            {
                return false;
            }

            if (_allowed is null)
            {
                return true;
            }

            return _allowed.Contains(extension);
        }

        private static HashSet<string>? BuildSet(IEnumerable<string> values)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var token = value.Trim();
                if (token.Contains(',', StringComparison.Ordinal))
                {
                    foreach (var part in token.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var expanded = ResolveToken(part);
                        foreach (var item in expanded)
                        {
                            set.Add(item);
                        }
                    }
                    continue;
                }

                var entries = ResolveToken(token);
                foreach (var entry in entries)
                {
                    set.Add(entry);
                }
            }

            return set.Count == 0 ? null : set;
        }

        private static IEnumerable<string> ResolveToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                yield break;
            }

            token = token.Trim().TrimStart('.');
            switch (token.ToLowerInvariant())
            {
                case "image":
                case "images":
                    foreach (var ext in MediaFormats.ImageExtensionNames)
                    {
                        yield return ext;
                    }
                    yield break;
                case "video":
                case "videos":
                    foreach (var ext in MediaFormats.VideoExtensionNames)
                    {
                        yield return ext;
                    }
                    yield break;
                case "pdf":
                case "pdfs":
                    foreach (var ext in MediaFormats.PdfExtensionNames)
                    {
                        yield return ext;
                    }
                    yield break;
            }

            yield return token;
        }
    }

    private static class AutomationIntents
    {
        public const string Ping = "automation.ping";
        public const string ListShortcuts = "automation.shortcuts.list";
        public const string GetStatus = "automation.status";
        public const string PauseAutomation = "automation.pause";
        public const string ResumeAutomation = "automation.resume";
        public const string Optimise = "automation.optimise";
    }
}
