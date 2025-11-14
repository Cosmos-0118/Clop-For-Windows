using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClopWindows.Core.Shared;

namespace ClopWindows.Core.Optimizers;

/// <summary>
/// Windows port of <c>Video.swift</c>: orchestrates ffmpeg re-encodes, fps caps, optional GIF export,
/// and progress reporting so higher layers can mirror the macOS UX.
/// </summary>
public sealed class VideoOptimiser : IOptimiser
{
    private readonly VideoOptimiserOptions _options;
    private readonly IVideoToolchain _toolchain;

    public VideoOptimiser(VideoOptimiserOptions? options = null, IVideoToolchain? toolchain = null)
    {
        _options = options ?? VideoOptimiserOptions.Default;
        _toolchain = toolchain ?? new ExternalVideoToolchain(_options);
    }

    public ItemType ItemType => ItemType.Video;

    public async Task<OptimisationResult> OptimiseAsync(OptimisationRequest request, OptimiserExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var source = request.SourcePath;
        if (!File.Exists(source.Value))
        {
            return OptimisationResult.Failure(request.RequestId, $"Source file not found: {source.Value}");
        }

        if (!MediaFormats.IsVideo(source))
        {
            return OptimisationResult.Unsupported(request.RequestId);
        }

        var plan = BuildPlan(request, _options);
        return plan.Mode switch
        {
            VideoOptimiserMode.Gif when !_options.EnableGifExport
                => OptimisationResult.Failure(request.RequestId, "GIF export disabled in configuration."),
            VideoOptimiserMode.Gif => await RunGifOptimisationAsync(request, plan, context, cancellationToken).ConfigureAwait(false),
            _ => await RunVideoOptimisationAsync(request, plan, context, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<OptimisationResult> RunVideoOptimisationAsync(OptimisationRequest request, VideoOptimiserPlan plan, OptimiserExecutionContext context, CancellationToken cancellationToken)
    {
        context.ReportProgress(5, "Preparing encoder");
        var tempOutput = FilePath.TempFile("clop-video", plan.OutputExtension, addUniqueSuffix: true);
        tempOutput.EnsureParentDirectoryExists();

        try
        {
            var toolchainResult = await _toolchain.TranscodeAsync(plan, tempOutput, context, cancellationToken).ConfigureAwait(false);
            if (!toolchainResult.Success)
            {
                return OptimisationResult.Failure(request.RequestId, toolchainResult.ErrorMessage ?? "Video optimisation failed");
            }

            if (!File.Exists(tempOutput.Value))
            {
                return OptimisationResult.Failure(request.RequestId, "Video optimiser produced no output file");
            }

            var finalOutput = BuildOutputPath(plan);
            finalOutput.EnsureParentDirectoryExists();
            var originalSize = SafeFileSize(plan.SourcePath);
            var optimisedSize = SafeFileSize(tempOutput);

            if (plan.RequireSizeReduction && !plan.ForceMp4 && optimisedSize >= originalSize)
            {
                context.ReportProgress(100, "Original already optimal");
                return new OptimisationResult(request.RequestId, OptimisationStatus.Succeeded, plan.SourcePath, "Original already optimal");
            }

            File.Copy(tempOutput.Value, finalOutput.Value, overwrite: true);
            CopyTimestamps(plan.SourcePath, finalOutput, plan.PreserveTimestamps);

            var message = DescribeImprovement(originalSize, optimisedSize, plan);
            context.ReportProgress(100, message);
            return new OptimisationResult(request.RequestId, OptimisationStatus.Succeeded, finalOutput, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Video optimisation failed: {ex.Message}");
            return OptimisationResult.Failure(request.RequestId, ex.Message);
        }
        finally
        {
            TryDelete(tempOutput);
        }
    }

    private async Task<OptimisationResult> RunGifOptimisationAsync(OptimisationRequest request, VideoOptimiserPlan plan, OptimiserExecutionContext context, CancellationToken cancellationToken)
    {
        context.ReportProgress(5, "Preparing GIF export");
        if (string.IsNullOrWhiteSpace(_options.GifskiPath))
        {
            return OptimisationResult.Failure(request.RequestId, "GIF export requested but gifski path is not configured.");
        }

        var tempOutput = FilePath.TempFile("clop-gif", ".gif", addUniqueSuffix: true);
        tempOutput.EnsureParentDirectoryExists();

        try
        {
            var toolchainResult = await _toolchain.ConvertToGifAsync(plan, tempOutput, context, cancellationToken).ConfigureAwait(false);
            if (!toolchainResult.Success)
            {
                return OptimisationResult.Failure(request.RequestId, toolchainResult.ErrorMessage ?? "GIF export failed");
            }

            if (!File.Exists(tempOutput.Value))
            {
                return OptimisationResult.Failure(request.RequestId, "GIF export produced no output file");
            }

            var finalOutput = BuildOutputPath(plan);
            finalOutput.EnsureParentDirectoryExists();
            var originalSize = SafeFileSize(plan.SourcePath);
            var optimisedSize = SafeFileSize(tempOutput);

            File.Copy(tempOutput.Value, finalOutput.Value, overwrite: true);
            CopyTimestamps(plan.SourcePath, finalOutput, plan.PreserveTimestamps);

            var message = DescribeImprovement(originalSize, optimisedSize, plan, isGif: true);
            context.ReportProgress(100, message);
            return new OptimisationResult(request.RequestId, OptimisationStatus.Succeeded, finalOutput, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"GIF export failed: {ex.Message}");
            return OptimisationResult.Failure(request.RequestId, ex.Message);
        }
        finally
        {
            TryDelete(tempOutput);
        }
    }

    private static FilePath BuildOutputPath(VideoOptimiserPlan plan)
    {
        var stem = plan.SourcePath.Stem;
        if (!stem.EndsWith(".clop", StringComparison.OrdinalIgnoreCase))
        {
            stem += ".clop";
        }
        var fileName = $"{stem}.{plan.OutputExtension}";
        return plan.SourcePath.Parent.Append(fileName);
    }

    private static string DescribeImprovement(long originalSize, long optimisedSize, VideoOptimiserPlan plan, bool isGif = false)
    {
        if (optimisedSize <= 0)
        {
            return isGif ? "Created GIF" : "Optimised";
        }

        if (originalSize <= 0)
        {
            return isGif ? "Created GIF" : "Optimised";
        }

        var diff = originalSize - optimisedSize;
        if (diff <= 0 && plan.RequireSizeReduction)
        {
            return "Re-encoded";
        }

        var descriptor = diff > 0
            ? $"Saved {diff.HumanSize()} ({originalSize.HumanSize()} → {optimisedSize.HumanSize()})"
            : "Re-encoded";
        return isGif ? $"GIF ready – {descriptor}" : descriptor;
    }

    private static void CopyTimestamps(FilePath source, FilePath destination, bool preserve)
    {
        if (!preserve)
        {
            return;
        }

        try
        {
            var sourceInfo = new FileInfo(source.Value);
            if (!sourceInfo.Exists)
            {
                return;
            }

            File.SetCreationTimeUtc(destination.Value, sourceInfo.CreationTimeUtc);
            File.SetLastWriteTimeUtc(destination.Value, sourceInfo.LastWriteTimeUtc);
        }
        catch
        {
            // timestamp preservation is best-effort only.
        }
    }

    private static long SafeFileSize(FilePath path)
    {
        try
        {
            return File.Exists(path.Value) ? new FileInfo(path.Value).Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    private static void TryDelete(FilePath path)
    {
        try
        {
            if (File.Exists(path.Value))
            {
                File.Delete(path.Value);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private static VideoOptimiserPlan BuildPlan(OptimisationRequest request, VideoOptimiserOptions options)
    {
        var metadata = request.Metadata;
        var mode = ParseMode(metadata, options);
        var forceMp4 = ReadBool(metadata, "video.forceMp4") ?? options.ForceMp4;
        var removeAudio = ReadBool(metadata, "video.removeAudio") ?? options.RemoveAudio;
        var capFps = ReadBool(metadata, "video.capFps") ?? options.CapFps;
        var targetFps = ReadInt(metadata, "video.targetFps") ?? options.TargetFps;
        targetFps = Math.Max(options.MinFps, targetFps);
        var playbackSpeed = ReadDouble(metadata, "video.playbackSpeed");
        var maxWidth = ReadInt(metadata, "video.maxWidth");
        var maxHeight = ReadInt(metadata, "video.maxHeight");
        var aggressive = ReadBool(metadata, "video.aggressive") ?? options.AggressiveQuality;
        var gifWidth = ReadInt(metadata, "video.gifMaxWidth") ?? options.GifMaxWidth;
        var gifFps = ReadInt(metadata, "video.gifFps") ?? options.GifFps;
        var gifQuality = ReadInt(metadata, "video.gifQuality") ?? options.GifQuality;
        var outputExtension = mode == VideoOptimiserMode.Gif
            ? "gif"
            : (ReadString(metadata, "video.extension") ?? (forceMp4 ? "mp4" : request.SourcePath.Extension ?? options.DefaultVideoExtension));
        outputExtension = string.IsNullOrWhiteSpace(outputExtension)
            ? options.DefaultVideoExtension
            : outputExtension.Trim().TrimStart('.');

        var filters = BuildFilters(maxWidth, maxHeight, playbackSpeed, options.EnforceEvenDimensions);

        return new VideoOptimiserPlan(
            request.SourcePath,
            mode,
            outputExtension,
            forceMp4,
            removeAudio,
            capFps,
            targetFps,
            playbackSpeed,
            maxWidth,
            maxHeight,
            filters,
            options.UseHardwareAcceleration,
            aggressive,
            options.StripMetadata,
            options.PreserveTimestamps,
            options.RequireSmallerSize,
            gifWidth,
            gifFps,
            gifQuality);
    }

    private static IReadOnlyList<string> BuildFilters(int? maxWidth, int? maxHeight, double? playbackSpeed, bool enforceEven)
    {
        var filters = new List<string>();
        if (maxWidth.HasValue || maxHeight.HasValue)
        {
            var width = maxWidth.HasValue ? EnsureEven(maxWidth.Value, enforceEven) : "-2";
            var height = maxHeight.HasValue ? EnsureEven(maxHeight.Value, enforceEven) : "-2";
            filters.Add($"scale=w={width}:h={height}:force_original_aspect_ratio=decrease");
        }

        if (playbackSpeed.HasValue && Math.Abs(playbackSpeed.Value - 1d) > 0.0001)
        {
            var factor = playbackSpeed.Value.ToString(CultureInfo.InvariantCulture);
            filters.Add($"setpts=PTS/{factor}");
        }

        return filters;
    }

    private static string EnsureEven(int value, bool enforceEven)
    {
        return enforceEven ? value.EvenInt().ToInvariantString() : value.ToInvariantString();
    }

    private static VideoOptimiserMode ParseMode(IReadOnlyDictionary<string, object?> metadata, VideoOptimiserOptions options)
    {
        if (ReadString(metadata, "video.mode") is { } value && options.GifTriggers.Any(trigger => trigger.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            return VideoOptimiserMode.Gif;
        }
        return VideoOptimiserMode.Video;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!TryGetMetadataValue(metadata, key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            bool value => value,
            string str when bool.TryParse(str, out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False => element.GetBoolean(),
            _ => null
        };
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!TryGetMetadataValue(metadata, key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            int value => value,
            long value => (int)value,
            double value => (int)Math.Round(value, MidpointRounding.AwayFromZero),
            string str when int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.TryGetInt32(out var parsed) ? parsed : null,
            _ => null
        };
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!TryGetMetadataValue(metadata, key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            double value => value,
            float value => value,
            int value => value,
            long value => value,
            string str when double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetDouble(),
            _ => null
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!TryGetMetadataValue(metadata, key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            string value => value,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => raw?.ToString()
        };
    }

    private static bool TryGetMetadataValue(IReadOnlyDictionary<string, object?> metadata, string key, out object? value)
    {
        if (metadata.TryGetValue(key, out value))
        {
            return true;
        }

        if (!key.StartsWith("video.", StringComparison.OrdinalIgnoreCase))
        {
            var videoKey = $"video.{key}";
            if (metadata.TryGetValue(videoKey, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }
}

public enum VideoOptimiserMode
{
    Video,
    Gif
}

public sealed record VideoOptimiserPlan(
    FilePath SourcePath,
    VideoOptimiserMode Mode,
    string OutputExtension,
    bool ForceMp4,
    bool RemoveAudio,
    bool CapFps,
    int TargetFps,
    double? PlaybackSpeedFactor,
    int? MaxWidth,
    int? MaxHeight,
    IReadOnlyList<string> Filters,
    bool UseHardwareAcceleration,
    bool AggressiveQuality,
    bool StripMetadata,
    bool PreserveTimestamps,
    bool RequireSizeReduction,
    int GifMaxWidth,
    int GifFps,
    int GifQuality)
{
    public bool RequiresFilters => Filters.Count > 0;
}

public interface IVideoToolchain
{
    Task<ToolchainResult> TranscodeAsync(VideoOptimiserPlan plan, FilePath tempOutput, OptimiserExecutionContext context, CancellationToken cancellationToken);

    Task<ToolchainResult> ConvertToGifAsync(VideoOptimiserPlan plan, FilePath tempOutput, OptimiserExecutionContext context, CancellationToken cancellationToken);
}

public sealed record ToolchainResult(bool Success, string? ErrorMessage = null)
{
    public static ToolchainResult Successful(string? message = null) => new(true, message);

    public static ToolchainResult Failure(string message) => new(false, message);
}

internal sealed class ExternalVideoToolchain : IVideoToolchain
{
    private readonly VideoOptimiserOptions _options;

    public ExternalVideoToolchain(VideoOptimiserOptions options)
    {
        _options = options;
    }

    public async Task<ToolchainResult> TranscodeAsync(VideoOptimiserPlan plan, FilePath tempOutput, OptimiserExecutionContext context, CancellationToken cancellationToken)
    {
        var args = BuildTranscodeArguments(plan, tempOutput);
        var tracker = new FfmpegProgressTracker("Encoding");
        return await RunProcessAsync(_options.FfmpegPath, args, context, cancellationToken, line => tracker.Process(line, context)).ConfigureAwait(false);
    }

    public async Task<ToolchainResult> ConvertToGifAsync(VideoOptimiserPlan plan, FilePath tempOutput, OptimiserExecutionContext context, CancellationToken cancellationToken)
    {
        var framesDirectory = CreateTempDirectory("clop-gif-frames");
        try
        {
            var framePattern = Path.Combine(framesDirectory.Value, "frame%05d.png");
            var extractArgs = BuildGifExtractionArgs(plan, framePattern);
            var tracker = new FfmpegProgressTracker("Extracting frames");
            var ffmpegResult = await RunProcessAsync(_options.FfmpegPath, extractArgs, context, cancellationToken, line => tracker.Process(line, context)).ConfigureAwait(false);
            if (!ffmpegResult.Success)
            {
                return ToolchainResult.Failure(ffmpegResult.ErrorMessage ?? "ffmpeg frame extraction failed");
            }

            var frames = Directory.EnumerateFiles(framesDirectory.Value, "frame*.png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (frames.Count == 0)
            {
                return ToolchainResult.Failure("Frame extraction produced no files");
            }

            var gifskiArgs = BuildGifskiArgs(plan, tempOutput, frames);
            var gifskiTracker = new GifskiProgressTracker(frames.Count);
            return await RunProcessAsync(_options.GifskiPath!, gifskiArgs, context, cancellationToken, line => gifskiTracker.Process(line, context)).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(framesDirectory);
        }
    }

    private IReadOnlyList<string> BuildTranscodeArguments(VideoOptimiserPlan plan, FilePath output)
    {
        var args = new List<string>
        {
            "-y",
            "-i", plan.SourcePath.Value
        };

        if (plan.CapFps && plan.TargetFps > 0)
        {
            args.Add("-fpsmax");
            args.Add(plan.TargetFps.ToInvariantString());
        }

        if (plan.RequiresFilters)
        {
            args.Add("-vf");
            args.Add(string.Join(',', plan.Filters));
        }

        args.AddRange(BuildCodecArguments(plan));

        if (plan.RemoveAudio)
        {
            args.Add("-an");
        }
        else
        {
            args.AddRange(new[] { "-c:a", "copy", "-map", "0:v", "-map", "0:a?" });
        }

        args.AddRange(new[] { "-movflags", "+faststart", "-progress", "pipe:2", "-nostats", "-hide_banner", "-stats_period", "0.2", output.Value });
        return args;
    }

    private IReadOnlyList<string> BuildCodecArguments(VideoOptimiserPlan plan)
    {
        if (plan.UseHardwareAcceleration)
        {
            return new[]
            {
                "-hwaccel", _options.HardwareAccelerationDevice,
                "-c:v", _options.HardwareEncoder,
                "-cq", _options.HardwareQuality.ToInvariantString(),
                "-b:v", "0"
            };
        }

        var preset = plan.AggressiveQuality ? "slower" : "faster";
        var crf = plan.AggressiveQuality ? Math.Max(18, _options.SoftwareCrf - 4) : _options.SoftwareCrf;
        return new[]
        {
            "-c:v", _options.SoftwareEncoder,
            "-preset", preset,
            "-crf", crf.ToInvariantString()
        };
    }

    private IReadOnlyList<string> BuildGifExtractionArgs(VideoOptimiserPlan plan, string framePattern)
    {
        var args = new List<string>
        {
            "-y",
            "-i", plan.SourcePath.Value
        };

        var filters = new List<string>();
        filters.Add($"scale=w={plan.GifMaxWidth}:h=-2:force_original_aspect_ratio=decrease");
        filters.Add($"fps={plan.GifFps}");
        if (plan.RequiresFilters)
        {
            filters.InsertRange(0, plan.Filters);
        }

        args.Add("-vf");
        args.Add(string.Join(',', filters));
        args.AddRange(new[] { "-progress", "pipe:2", "-nostats", "-hide_banner", "-stats_period", "0.2", framePattern });
        return args;
    }

    private IReadOnlyList<string> BuildGifskiArgs(VideoOptimiserPlan plan, FilePath output, IReadOnlyCollection<string> frames)
    {
        var args = new List<string>
        {
            "-o", output.Value,
            "--width", plan.GifMaxWidth.ToInvariantString(),
            "--fps", plan.GifFps.ToInvariantString(),
            "--quality", plan.GifQuality.ToInvariantString()
        };
        args.AddRange(frames);
        return args;
    }

    private static FilePath CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return FilePath.From(path);
    }

    private static void TryDeleteDirectory(FilePath directory)
    {
        try
        {
            if (Directory.Exists(directory.Value))
            {
                Directory.Delete(directory.Value, recursive: true);
            }
        }
        catch
        {
            // cleanup best effort
        }
    }

    private static async Task<ToolchainResult> RunProcessAsync(string executable, IReadOnlyList<string> arguments, OptimiserExecutionContext context, CancellationToken cancellationToken, Action<string>? progressCallback)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutCompletion.TrySetResult();
            }
            else
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrCompletion.TrySetResult();
            }
            else
            {
                stderr.AppendLine(e.Data);
                progressCallback?.Invoke(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return ToolchainResult.Failure($"Unable to start process '{executable}'");
            }
        }
        catch (Exception ex)
        {
            return ToolchainResult.Failure($"Unable to start '{executable}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignored
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(stdoutCompletion.Task, stderrCompletion.Task).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = $"Process '{executable}' exited with code {process.ExitCode}";
            Log.Error($"{error}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            return ToolchainResult.Failure(error);
        }

        return ToolchainResult.Successful();
    }

    private sealed class FfmpegProgressTracker
    {
        private static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d+):(\d+)\.(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly string _phase;
        private long? _durationUs;

        public FfmpegProgressTracker(string phase)
        {
            _phase = phase;
        }

        public void Process(string line, OptimiserExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var durationMatch = DurationRegex.Match(line);
            if (durationMatch.Success)
            {
                var hours = long.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var minutes = long.Parse(durationMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                var seconds = long.Parse(durationMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                var centiseconds = long.Parse(durationMatch.Groups[4].Value, CultureInfo.InvariantCulture);
                var totalMs = ((hours * 3600 + minutes * 60 + seconds) * 1000) + (centiseconds * 10);
                _durationUs = totalMs * 1000;
                context.ReportProgress(5, $"{_phase} – duration {FormatTime(_durationUs.Value)}");
                return;
            }

            if (line.StartsWith("out_time_us=", StringComparison.OrdinalIgnoreCase) && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
            {
                var percent = _durationUs.HasValue && _durationUs.Value > 0
                    ? Math.Clamp(micros / (double)_durationUs.Value * 100d, 0d, 99.0d)
                    : Math.Min(98d, micros / 10_000_000d);
                context.ReportProgress(percent, $"{_phase} {FormatTime(micros)}");
                return;
            }

            if (line.StartsWith("progress=end", StringComparison.OrdinalIgnoreCase))
            {
                context.ReportProgress(99, "Finalising");
            }
        }

        private static string FormatTime(long microseconds)
        {
            var ts = TimeSpan.FromMilliseconds(microseconds / 1000d);
            return ts.TotalHours >= 1
                ? ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                : ts.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    private sealed class GifskiProgressTracker
    {
        private static readonly Regex FrameRegex = new(@"Frame\s+(\d+)\s+/\s+(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly int _totalFrames;

        public GifskiProgressTracker(int totalFrames)
        {
            _totalFrames = Math.Max(1, totalFrames);
        }

        public void Process(string line, OptimiserExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var match = FrameRegex.Match(line);
            if (!match.Success)
            {
                return;
            }

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame))
            {
                return;
            }

            var percent = Math.Clamp(frame / (double)_totalFrames * 100d, 0d, 99d);
            context.ReportProgress(percent, $"GIF frame {frame} of {_totalFrames}");
        }
    }
}