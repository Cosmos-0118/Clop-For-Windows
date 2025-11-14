using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ClopWindows.Core.Shared;

namespace ClopWindows.Core.Optimizers;

[SupportedOSPlatform("windows6.1")]
public sealed class ImageOptimiser : IOptimiser
{
    private static readonly ImageCodecInfo[] ImageEncoders = ImageCodecInfo.GetImageEncoders();
    private const int MinJpegFallbackQuality = 50;
    private readonly ImageOptimiserOptions _options;

    public ImageOptimiser(ImageOptimiserOptions? options = null)
    {
        _options = options ?? ImageOptimiserOptions.Default;
    }

    public ItemType ItemType => ItemType.Image;

    public async Task<OptimisationResult> OptimiseAsync(OptimisationRequest request, OptimiserExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => OptimiseInternal(request, context, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private OptimisationResult OptimiseInternal(OptimisationRequest request, OptimiserExecutionContext context, CancellationToken cancellationToken)
    {
        var sourcePath = request.SourcePath;
        if (!File.Exists(sourcePath.Value))
        {
            return OptimisationResult.Failure(request.RequestId, $"Source file not found: {sourcePath.Value}");
        }

        var extension = (sourcePath.Extension ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !_options.SupportedInputFormats.Contains(extension))
        {
            return OptimisationResult.Unsupported(request.RequestId);
        }

        context.ReportProgress(5, "Analysing image");

        FilePath? tempOutput = null;
        Bitmap? downscaledBitmap = null;

        try
        {
            using var fileStream = new FileStream(sourcePath.Value, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var image = Image.FromStream(fileStream, useEmbeddedColorManagement: true, validateImageData: true);

            var sourceMetadata = image.PropertyItems ?? Array.Empty<PropertyItem>();
            var metadata = _options.PreserveMetadata ? CloneMetadata(sourceMetadata) : Array.Empty<PropertyItem>();
            var hasAlpha = Image.IsAlphaPixelFormat(image.PixelFormat);
            var isAnimated = IsAnimated(image);
            var profile = DetermineSaveProfile(extension, hasAlpha, isAnimated);
            var shouldDownscale = _options.DownscaleRetina && NeedsRetinaDownscale(image);
            var shouldStripMetadata = !_options.PreserveMetadata && sourceMetadata.Length > 0;

            if (!shouldDownscale && profile.IsSameExtension(extension) && !shouldStripMetadata)
            {
                context.ReportProgress(100, "Already optimised");
                return new OptimisationResult(request.RequestId, OptimisationStatus.Succeeded, sourcePath, "Already optimised");
            }

            using var baseBitmap = new Bitmap(image);
            var workingBitmap = baseBitmap;

            if (shouldDownscale)
            {
                var targetSize = CalculateRetinaSize(image);
                context.ReportProgress(25, $"Downscaling to {targetSize.Width}×{targetSize.Height}");
                downscaledBitmap = ResizeBitmap(workingBitmap, targetSize);
                workingBitmap = downscaledBitmap;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (shouldStripMetadata)
            {
                ClearMetadata(workingBitmap);
            }
            else if (_options.PreserveMetadata && metadata.Length > 0)
            {
                ApplyMetadata(workingBitmap, metadata);
            }

            var outputPath = BuildOutputPath(sourcePath, profile.Extension);
            outputPath.EnsureParentDirectoryExists();

            tempOutput = FilePath.TempFile("clop-image", profile.Extension, addUniqueSuffix: true);
            context.ReportProgress(60, profile.Description);
            SaveBitmap(workingBitmap, tempOutput.Value.Value, profile);

            cancellationToken.ThrowIfCancellationRequested();

            var originalSize = SafeFileSize(sourcePath);
            var optimisedSize = SafeFileSize(tempOutput.Value);

            if (_options.RequireSizeImprovement && optimisedSize >= originalSize)
            {
                if (profile.Format.Guid == ImageFormat.Jpeg.Guid &&
                    TryReduceJpegSize(workingBitmap, originalSize, profile.TargetQuality ?? 82L, cancellationToken, out var tunedPath, out var tunedSize))
                {
                    TryDelete(tempOutput.Value);
                    tempOutput = tunedPath;
                    optimisedSize = tunedSize;
                    context.ReportProgress(85, "Adjusted JPEG quality");
                }
                else
                {
                    context.ReportProgress(95, "No size improvement; keeping original");
                    return new OptimisationResult(request.RequestId, OptimisationStatus.Succeeded, sourcePath, "Original already optimal");
                }
            }

            File.Copy(tempOutput.Value.Value, outputPath.Value, overwrite: true);
            var message = DescribeImprovement(originalSize, optimisedSize);
            context.ReportProgress(100, message);

            return new OptimisationResult(request.RequestId, OptimisationStatus.Succeeded, outputPath, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Image optimisation failed: {ex.Message}");
            return OptimisationResult.Failure(request.RequestId, ex.Message);
        }
        finally
        {
            downscaledBitmap?.Dispose();
            if (tempOutput is { } temp && File.Exists(temp.Value))
            {
                TryDelete(temp);
            }
        }
    }

    private static void SaveBitmap(Image bitmap, string destination, ImageSaveProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? Path.GetTempPath());

        if (profile.Format.Guid == ImageFormat.Jpeg.Guid)
        {
            var codec = GetEncoder(ImageFormat.Jpeg);
            if (codec is null)
            {
                throw new InvalidOperationException("JPEG encoder not available on this platform.");
            }

            using var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, profile.TargetQuality ?? 82L);
            bitmap.Save(destination, codec, encoderParameters);
            return;
        }

        bitmap.Save(destination, profile.Format);
    }

    private ImageSaveProfile DetermineSaveProfile(string sourceExtension, bool hasAlpha, bool isAnimated)
    {
        var jpegQuality = Math.Clamp(_options.TargetJpegQuality, 30, 100);
        var extensionIsJpeg = sourceExtension is "jpg" or "jpeg";
        var canConvertToJpeg = _options.FormatsToConvertToJpeg.Contains(sourceExtension);
        var shouldUseJpeg = !isAnimated && !hasAlpha && sourceExtension != "gif" && canConvertToJpeg;

        if (extensionIsJpeg)
        {
            return ImageSaveProfile.Jpeg("Re-encoding JPEG", jpegQuality);
        }

        if (shouldUseJpeg)
        {
            return ImageSaveProfile.Jpeg("Converting to JPEG", jpegQuality);
        }

        return sourceExtension switch
        {
            "gif" => ImageSaveProfile.From(ImageFormat.Gif, "gif", "Normalising GIF"),
            "bmp" => ImageSaveProfile.From(ImageFormat.Png, "png", "Converting BMP to PNG"),
            "tif" or "tiff" => ImageSaveProfile.From(ImageFormat.Png, "png", "Normalising TIFF"),
            _ => ImageSaveProfile.From(ImageFormat.Png, "png", "Normalising PNG"),
        };
    }

    private bool NeedsRetinaDownscale(Image image)
    {
        if (!_options.DownscaleRetina || _options.RetinaLongEdgePixels <= 0)
        {
            return false;
        }
        var longestEdge = Math.Max(image.Width, image.Height);
        return longestEdge > _options.RetinaLongEdgePixels;
    }

    private Size CalculateRetinaSize(Image image)
    {
        var longestEdge = Math.Max(image.Width, image.Height);
        var scale = _options.RetinaLongEdgePixels / (double)longestEdge;
        var width = Math.Max(1, (image.Width * scale).EvenInt());
        var height = Math.Max(1, (image.Height * scale).EvenInt());
        return new Size(width, height);
    }

    private static Bitmap ResizeBitmap(Image source, Size targetSize)
    {
        var bitmap = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(source.HorizontalResolution, source.VerticalResolution);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(Point.Empty, targetSize), new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);

        return bitmap;
    }

    private static bool IsAnimated(Image image)
    {
        try
        {
            return image.FrameDimensionsList.Select(d => new FrameDimension(d)).Any(dimension => image.GetFrameCount(dimension) > 1);
        }
        catch
        {
            return false;
        }
    }

    private static PropertyItem[] CloneMetadata(IEnumerable<PropertyItem> items) => items.Select(ClonePropertyItem).ToArray();

    private static void ApplyMetadata(Image bitmap, PropertyItem[] metadata)
    {
        foreach (var property in metadata)
        {
            try
            {
                bitmap.SetPropertyItem(ClonePropertyItem(property));
            }
            catch (ArgumentException)
            {
                // Some formats reject specific EXIF payloads; ignore quietly to keep pipeline moving.
            }
        }
    }

    private static void ClearMetadata(Image bitmap)
    {
        foreach (var property in bitmap.PropertyItems.ToArray())
        {
            try
            {
                bitmap.RemovePropertyItem(property.Id);
            }
            catch
            {
                // Ignore if property cannot be removed.
            }
        }
    }

#pragma warning disable SYSLIB0050
    private static PropertyItem ClonePropertyItem(PropertyItem property)
    {
        var clone = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
        clone.Id = property.Id;
        clone.Len = property.Len;
        clone.Type = property.Type;
        clone.Value = property.Value?.ToArray() ?? Array.Empty<byte>();
        return clone;
    }
#pragma warning restore SYSLIB0050

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var guid = format.Guid;
        return ImageEncoders.FirstOrDefault(codec => codec.FormatID == guid);
    }

    private static FilePath BuildOutputPath(FilePath sourcePath, string extension)
    {
        var stem = sourcePath.Stem;
        if (stem.EndsWith(".clop", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^5];
        }
        var fileName = $"{stem}.clop.{extension}";
        return sourcePath.Parent.Append(fileName);
    }

    private static string DescribeImprovement(long originalSize, long optimisedSize)
    {
        if (originalSize <= 0 || optimisedSize <= 0)
        {
            return "Optimised";
        }
        var diff = originalSize - optimisedSize;
        return diff > 0
            ? $"Saved {diff.HumanSize()} ({originalSize.HumanSize()} → {optimisedSize.HumanSize()})"
            : "Re-encoded";
    }

    private static bool TryReduceJpegSize(Image bitmap, long maxBytes, long startingQuality, CancellationToken token, out FilePath path, out long size)
    {
        path = default;
        size = 0;

        var start = (int)Math.Clamp(startingQuality, 1L, 100L);
        var high = start - 1;
        if (high < MinJpegFallbackQuality)
        {
            return false;
        }

        var codec = GetEncoder(ImageFormat.Jpeg);
        if (codec is null)
        {
            return false;
        }

        var low = MinJpegFallbackQuality;
        var bestSize = long.MaxValue;
        FilePath? bestCandidate = null;
        var scratch = new List<FilePath>();

        while (low <= high)
        {
            token.ThrowIfCancellationRequested();

            var quality = (low + high) / 2;
            var candidate = FilePath.TempFile("clop-image-quality", "jpg", addUniqueSuffix: true);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(candidate.Value) ?? Path.GetTempPath());
                using var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bitmap.Save(candidate.Value, codec, encoderParameters);
            }
            catch
            {
                TryDelete(candidate);
                throw;
            }

            var candidateSize = SafeFileSize(candidate);
            if (candidateSize <= 0)
            {
                scratch.Add(candidate);
                break;
            }

            if (candidateSize < maxBytes)
            {
                if (candidateSize < bestSize)
                {
                    if (bestCandidate is { } previousBest)
                    {
                        scratch.Add(previousBest);
                    }

                    bestCandidate = candidate;
                    bestSize = candidateSize;
                }
                else
                {
                    scratch.Add(candidate);
                }

                high = quality - 1;
            }
            else
            {
                scratch.Add(candidate);
                low = quality + 1;
            }
        }

        foreach (var leftover in scratch)
        {
            TryDelete(leftover);
        }

        if (bestCandidate is null)
        {
            return false;
        }

        path = bestCandidate.Value;
        size = bestSize;
        return true;
    }

    private static long SafeFileSize(FilePath path)
    {
        try
        {
            return File.Exists(path.Value) ? new FileInfo(path.Value).Length : 0;
        }
        catch
        {
            return 0;
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
            // ignored
        }
    }

    private sealed record ImageSaveProfile(ImageFormat Format, string Extension, string Description, long? TargetQuality)
    {
        public static ImageSaveProfile Jpeg(string description, long quality) => new(ImageFormat.Jpeg, "jpg", description, quality);

        public static ImageSaveProfile From(ImageFormat format, string extension, string description) => new(format, extension, description, null);

        public bool IsSameExtension(string value) => string.Equals(Extension, value, StringComparison.OrdinalIgnoreCase);
    }
}
