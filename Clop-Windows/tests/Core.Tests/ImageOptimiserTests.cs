using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using ClopWindows.Core.Optimizers;
using ClopWindows.Core.Shared;
using Xunit;

namespace Core.Tests;

[SupportedOSPlatform("windows6.1")]
public sealed class ImageOptimiserTests
{
    [Fact]
    public async Task ConvertsPngToJpegWhenEligible()
    {
        var source = CreateSampleImage(ImageFormat.Png, width: 1600, height: 900, includeAlpha: false, includeMetadata: true);
        var outputs = new List<FilePath>();
        try
        {
            var optimiser = new ImageOptimiser(new ImageOptimiserOptions { TargetJpegQuality = 60 });
            await using var coordinator = new OptimisationCoordinator(new IOptimiser[] { optimiser });
            var ticket = coordinator.Enqueue(new OptimisationRequest(ItemType.Image, source));
            var result = await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(OptimisationStatus.Succeeded, result.Status);
            var output = EnsureOutputPath(result);
            outputs.Add(output);
            Assert.EndsWith(".jpg", output.Value, StringComparison.OrdinalIgnoreCase);

            var originalSize = new FileInfo(source.Value).Length;
            var newSize = new FileInfo(output.Value).Length;
            Assert.True(newSize < originalSize, $"Expected JPEG ({newSize}) to be smaller than PNG ({originalSize})");
        }
        finally
        {
            CleanupFiles(source, outputs);
        }
    }

    [Fact]
    public async Task DownscalesRetinaImages()
    {
        var source = CreateSampleImage(ImageFormat.Jpeg, width: 5200, height: 3400, includeAlpha: false, includeMetadata: false);
        var outputs = new List<FilePath>();
        try
        {
            var options = new ImageOptimiserOptions
            {
                TargetJpegQuality = 75,
                DownscaleRetina = true,
                RetinaLongEdgePixels = 2048
            };
            var optimiser = new ImageOptimiser(options);
            await using var coordinator = new OptimisationCoordinator(new IOptimiser[] { optimiser });
            var ticket = coordinator.Enqueue(new OptimisationRequest(ItemType.Image, source));
            var result = await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(OptimisationStatus.Succeeded, result.Status);
            var output = EnsureOutputPath(result);
            outputs.Add(output);

            using var bitmap = Image.FromFile(output.Value);
            Assert.True(Math.Max(bitmap.Width, bitmap.Height) <= options.RetinaLongEdgePixels,
                $"Expected long edge <= {options.RetinaLongEdgePixels}, got {bitmap.Width}x{bitmap.Height}");
        }
        finally
        {
            CleanupFiles(source, outputs);
        }
    }

    [Fact]
    public async Task PreservesMetadataWhenRequested()
    {
        var source = CreateSampleImage(ImageFormat.Jpeg, width: 800, height: 600, includeAlpha: false, includeMetadata: true);
        var outputs = new List<FilePath>();
        try
        {
            var options = new ImageOptimiserOptions { PreserveMetadata = true };
            var optimiser = new ImageOptimiser(options);
            await using var coordinator = new OptimisationCoordinator(new IOptimiser[] { optimiser });
            var ticket = coordinator.Enqueue(new OptimisationRequest(ItemType.Image, source));
            var result = await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(OptimisationStatus.Succeeded, result.Status);
            var output = EnsureOutputPath(result);
            outputs.Add(output);

            using var bitmap = Image.FromFile(output.Value);
            var description = bitmap.PropertyItems.FirstOrDefault(p => p.Id == 0x010E);
            Assert.NotNull(description);
            var descriptionBytes = description!.Value;
            Assert.NotNull(descriptionBytes);
            Assert.Contains("Clop Windows", Encoding.ASCII.GetString(descriptionBytes!).TrimEnd('\0'));
        }
        finally
        {
            CleanupFiles(source, outputs);
        }
    }

    private static FilePath EnsureOutputPath(OptimisationResult result)
    {
        Assert.NotNull(result.OutputPath);
        return result.OutputPath!.Value;
    }

    private static FilePath CreateSampleImage(ImageFormat format, int width, int height, bool includeAlpha, bool includeMetadata)
    {
        var extension = format switch
        {
            _ when format.Equals(ImageFormat.Png) => ".png",
            _ when format.Equals(ImageFormat.Gif) => ".gif",
            _ => ".jpg"
        };
        var path = FilePath.TempFile("image-optimiser-test", extension, addUniqueSuffix: true);
        path.EnsureParentDirectoryExists();

        using var bitmap = new Bitmap(width, height, includeAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var background = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.DeepSkyBlue, Color.MidnightBlue, LinearGradientMode.ForwardDiagonal);
        graphics.FillRectangle(background, new Rectangle(0, 0, width, height));
        using var pen = new Pen(Color.White, 5);
        graphics.DrawEllipse(pen, 10, 10, width - 20, height - 20);

        if (includeMetadata)
        {
            var description = CreatePropertyItem(0x010E, "Clop Windows Test");
            bitmap.SetPropertyItem(description);
        }

        bitmap.Save(path.Value, format);
        return path;
    }

    private static void CleanupFiles(FilePath source, IEnumerable<FilePath> outputs)
    {
        TryDelete(source);
        foreach (var output in outputs)
        {
            TryDelete(output);
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
            // ignore cleanup issues
        }
    }

#pragma warning disable SYSLIB0050
    private static PropertyItem CreatePropertyItem(int id, string value)
    {
        var property = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
        property.Id = id;
        property.Type = 2;
        property.Value = Encoding.ASCII.GetBytes(value + '\0');
        property.Len = property.Value.Length;
        return property;
    }
#pragma warning restore SYSLIB0050
}
