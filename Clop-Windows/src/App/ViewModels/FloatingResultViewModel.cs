using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using ClopWindows.App.Infrastructure;
using ClopWindows.Core.Optimizers;
using ClopWindows.Core.Shared;

namespace ClopWindows.App.ViewModels;

public sealed class FloatingResultViewModel : ObservableObject
{
    private readonly Action<FloatingResultViewModel> _dismiss;
    private static readonly SolidColorBrush SuccessBrush = CreateBrush(122, 145, 255);
    private static readonly SolidColorBrush FailureBrush = CreateBrush(240, 120, 120);
    private static readonly SolidColorBrush RunningBrush = CreateBrush(200, 200, 200);

    private string _displayName = string.Empty;
    private string? _subtitle;
    private string _statusText = "Queued";
    private string? _sizeSummary;
    private double _progress;
    private bool _isRunning = true;
    private bool _isSuccess;
    private bool _isFailure;
    private TimeSpan? _duration;
    private Brush _statusBrush;

    private long? _originalBytes;
    private FilePath? _outputPath;

    public FloatingResultViewModel(string requestId, Action<FloatingResultViewModel> dismiss)
    {
        RequestId = requestId;
        _dismiss = dismiss;
        _statusBrush = RunningBrush;
        DismissCommand = new RelayCommand(_ => _dismiss(this));
    }

    public string RequestId { get; }

    public FilePath? SourcePath { get; private set; }

    public string SourceTag { get; private set; } = string.Empty;

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;

    public ICommand DismissCommand { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string? Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? SizeSummary
    {
        get => _sizeSummary;
        private set => SetProperty(ref _sizeSummary, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public bool IsSuccess
    {
        get => _isSuccess;
        private set => SetProperty(ref _isSuccess, value);
    }

    public bool IsFailure
    {
        get => _isFailure;
        private set => SetProperty(ref _isFailure, value);
    }

    public TimeSpan? Duration
    {
        get => _duration;
        private set => SetProperty(ref _duration, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public void ApplyRequest(OptimisationRequest request)
    {
        SourcePath = request.SourcePath;
        SourceTag = ExtractSourceTag(request.Metadata);
        StartedAt = DateTimeOffset.UtcNow;
        _originalBytes = TryGetLength(request.SourcePath);
        DisplayName = request.SourcePath.Name;
        Subtitle = BuildSubtitle(request);
        StatusText = "Queued";
        StatusBrush = RunningBrush;
        Progress = 0d;
        IsRunning = true;
        IsSuccess = false;
        IsFailure = false;
        SizeSummary = null;
    }

    public void UpdateProgress(OptimisationProgress progress)
    {
        Progress = Math.Clamp(progress.Percentage, 0d, 100d);
        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            StatusText = progress.Message;
        }
        else
        {
            StatusText = Progress >= 100d ? "Finalising" : "Optimising";
        }

        IsRunning = true;
        StatusBrush = RunningBrush;
    }

    public void ApplyResult(OptimisationResult result, OptimisationRequest? request)
    {
        IsRunning = false;
        IsSuccess = result.Status == OptimisationStatus.Succeeded;
        IsFailure = !IsSuccess && result.Status != OptimisationStatus.Cancelled;
        Progress = 100d;
        StatusBrush = IsSuccess ? SuccessBrush : FailureBrush;
        Duration = result.Duration;
        _outputPath = result.OutputPath ?? request?.SourcePath;

        StatusText = result.Status switch
        {
            OptimisationStatus.Succeeded => result.Message ?? "Optimised",
            OptimisationStatus.Unsupported => "Unsupported",
            OptimisationStatus.Cancelled => "Cancelled",
            OptimisationStatus.Failed => result.Message ?? "Failed",
            _ => result.Message ?? "Finished"
        };

        if (IsSuccess)
        {
            UpdateSizeSummary(_outputPath);
        }
        else
        {
            SizeSummary = null;
        }
    }

    private void UpdateSizeSummary(FilePath? outputPath)
    {
        if (_originalBytes is null || outputPath is null)
        {
            SizeSummary = null;
            return;
        }

        var optimisedBytes = TryGetLength(outputPath.Value);
        if (optimisedBytes is null)
        {
            SizeSummary = null;
            return;
        }

        var delta = _originalBytes.Value - optimisedBytes.Value;
        var magnitude = Math.Abs(delta);
        var total = optimisedBytes.Value;
        var changeAmount = magnitude.HumanSize();
        var original = _originalBytes.Value.HumanSize();
        var final = total.HumanSize();
        var percentage = _originalBytes.Value == 0 ? 0 : (double)delta / _originalBytes.Value * 100d;
        var direction = delta >= 0 ? "Saved" : "Added";
        SizeSummary = string.Format(CultureInfo.CurrentCulture, "{0} {1} ({2} → {3}, {4:+0.##;-0.##;0}%)", direction, changeAmount, original, final, percentage);
    }

    private static string ExtractSourceTag(IReadOnlyDictionary<string, object?> metadata)
    {
        if (metadata.TryGetValue("source", out var value) && value is string text)
        {
            return text;
        }

        return string.Empty;
    }

    private static string? BuildSubtitle(OptimisationRequest request)
    {
        var parts = new List<string>();
        if (request.SourcePath.Parent is { } parent)
        {
            parts.Add(parent.Value);
        }

        if (request.Metadata.TryGetValue("source", out var source) && source is string sourceText && !string.IsNullOrWhiteSpace(sourceText))
        {
            parts.Add(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(sourceText));
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join(" • ", parts);
    }

    private static long? TryGetLength(FilePath path)
    {
        try
        {
            var info = new FileInfo(path.Value);
            return info.Exists ? info.Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
