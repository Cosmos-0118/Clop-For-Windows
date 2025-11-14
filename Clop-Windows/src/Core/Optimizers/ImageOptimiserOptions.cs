using System;
using System.Collections.Generic;

namespace ClopWindows.Core.Optimizers;

public sealed record ImageOptimiserOptions
{
    public IReadOnlySet<string> SupportedInputFormats { get; init; } = new HashSet<string>(new[] { "png", "jpg", "jpeg", "bmp", "gif", "tif", "tiff" }, StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> FormatsToConvertToJpeg { get; init; } = new HashSet<string>(new[] { "png", "bmp", "tif", "tiff", "gif" }, StringComparer.OrdinalIgnoreCase);

    public int TargetJpegQuality { get; init; } = 82;

    public bool DownscaleRetina { get; init; } = true;

    public int RetinaLongEdgePixels { get; init; } = 3840;

    public bool RequireSizeImprovement { get; init; } = true;

    public bool PreserveMetadata { get; init; } = false;

    public static ImageOptimiserOptions Default { get; } = new();
}
