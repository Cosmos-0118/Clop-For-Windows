using System.Collections.Generic;

namespace ClopWindows.Core.Optimizers;

/// <summary>
/// Tunable switches for the video pipeline so callers can match macOS behaviour without
/// hard-coding ffmpeg/gifski arguments inside the optimiser itself.
/// </summary>
public sealed record VideoOptimiserOptions
{
    public static VideoOptimiserOptions Default { get; } = new();

    /// <summary>
    /// Absolute path (or PATH-resolved executable name) for ffmpeg.
    /// </summary>
    public string FfmpegPath { get; init; } = "ffmpeg";

    /// <summary>
    /// Optional path to gifski. Required only when GIF export is requested.
    /// </summary>
    public string? GifskiPath { get; init; } = "gifski";

    public bool ForceMp4 { get; init; } = true;

    public bool RemoveAudio { get; init; }
        = false;

    public bool CapFps { get; init; } = true;

    public int TargetFps { get; init; } = 60;

    public int MinFps { get; init; } = 24;

    public bool UseHardwareAcceleration { get; init; } = true;

    public string HardwareAccelerationDevice { get; init; } = "d3d11va";

    public string HardwareEncoder { get; init; } = "h264_amf";

    public string SoftwareEncoder { get; init; } = "libx264";

    public bool AggressiveQuality { get; init; }
        = false;

    public int SoftwareCrf { get; init; } = 26;

    public int HardwareQuality { get; init; } = 23;

    public bool PreserveTimestamps { get; init; } = true;

    public bool RequireSmallerSize { get; init; } = true;

    public bool StripMetadata { get; init; }
        = false;

    public string DefaultVideoExtension { get; init; } = "mp4";

    public bool EnableGifExport { get; init; } = true;

    public int GifMaxWidth { get; init; } = 720;

    public int GifFps { get; init; } = 18;

    public int GifQuality { get; init; } = 85;

    /// <summary>
    /// Ensures resize filters keep dimensions divisible by two (required by many codecs).
    /// </summary>
    public bool EnforceEvenDimensions { get; init; } = true;

    /// <summary>
    /// Allows feature flags (CLI, UI, automation) to request GIF export via metadata.
    /// </summary>
    public IReadOnlyCollection<string> GifTriggers { get; init; } = new[] { "gif", "animated" };
}