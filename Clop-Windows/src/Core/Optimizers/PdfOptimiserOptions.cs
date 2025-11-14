using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClopWindows.Core.Optimizers;

public sealed record PdfOptimiserOptions
{
    public static PdfOptimiserOptions Default { get; } = new();

    public string GhostscriptPath { get; init; } = ResolveGhostscriptPath();

    public string? GhostscriptResourceDirectory { get; init; } = ResolveGhostscriptResourceDirectory();

    public string FontSearchPath { get; init; } = ResolveFontSearchPath();

    public bool AggressiveByDefault { get; init; } = false;

    public bool StripMetadata { get; init; } = true;

    public bool PreserveTimestamps { get; init; } = true;

    public bool RequireSmallerSize { get; init; } = true;

    public IReadOnlyList<string> BaseArguments { get; init; } = DefaultGhostscriptArguments.Base;

    public IReadOnlyList<string> LossyArguments { get; init; } = DefaultGhostscriptArguments.Lossy;

    public IReadOnlyList<string> LosslessArguments { get; init; } = DefaultGhostscriptArguments.Lossless;

    public IReadOnlyList<string> MetadataPreArguments { get; init; } = DefaultGhostscriptArguments.MetadataPre;

    public IReadOnlyList<string> MetadataPostArguments { get; init; } = DefaultGhostscriptArguments.MetadataPost;

    private static string ResolveGhostscriptPath()
    {
        var env = Environment.GetEnvironmentVariable("CLOP_GS") ?? Environment.GetEnvironmentVariable("GS_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env!;
        }

        var baseDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "tools", "ghostscript", "gswin64c.exe");
        return File.Exists(bundled) ? bundled : "gswin64c.exe";
    }

    private static string? ResolveGhostscriptResourceDirectory()
    {
        var env = Environment.GetEnvironmentVariable("CLOP_GS_LIB") ?? Environment.GetEnvironmentVariable("GS_LIB");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env!;
        }

        var baseDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "tools", "ghostscript", "Resource", "Init");
        return Directory.Exists(bundled) ? bundled : null;
    }

    private static string ResolveFontSearchPath()
    {
        var env = Environment.GetEnvironmentVariable("CLOP_FONT_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env!;
        }

        var paths = new[]
        {
            CombineSafe(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            CombineSafe(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Adobe", "Fonts")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path!))
        .Select(path => path!);

        var joined = string.Join(';', paths);
        if (!string.IsNullOrWhiteSpace(joined))
        {
            return joined;
        }

        var fallback = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return string.IsNullOrWhiteSpace(fallback) ? "C:/Windows/Fonts" : fallback;
    }

    private static string? CombineSafe(string? root, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        return Path.Combine(new[] { root! }.Concat(segments).ToArray());
    }
}

internal static class DefaultGhostscriptArguments
{
    public static readonly string[] Base =
    {
        "-r150",
        "-dALLOWPSTRANSPARENCY",
        "-dAutoRotatePages=/None",
        "-dBATCH",
        "-dCannotEmbedFontPolicy=/Warning",
        "-dColorConversionStrategy=/LeaveColorUnchanged",
        "-dColorConversionStrategy=/sRGB",
        "-dColorImageDownsampleThreshold=1.0",
        "-dColorImageDownsampleType=/Bicubic",
        "-dColorImageResolution=150",
        "-dCompressFonts=true",
        "-dCompressPages=true",
        "-dCompressStreams=true",
        "-dConvertCMYKImagesToRGB=true",
        "-dConvertImagesToIndexed=false",
        "-dCreateJobTicket=false",
        "-dDetectDuplicateImages=true",
        "-dDoThumbnails=false",
        "-dEmbedAllFonts=true",
        "-dEncodeColorImages=true",
        "-dEncodeGrayImages=true",
        "-dEncodeMonoImages=true",
        "-dFastWebView=false",
        "-dGrayDetection=true",
        "-dGrayImageDownsampleThreshold=1.0",
        "-dGrayImageDownsampleType=/Bicubic",
        "-dGrayImageResolution=150",
        "-dHaveTransparency=true",
        "-dLZWEncodePages=true",
        "-dMaxBitmap=0",
        "-dMonoImageDownsampleThreshold=1.0",
        "-dMonoImageDownsampleType=/Bicubic",
        "-dMonoImageFilter=/CCITTFaxEncode",
        "-dMonoImageResolution=150",
        "-dNOPAUSE",
        "-dNOPROMPT",
        "-dOptimize=false",
        "-dParseDSCComments=false",
        "-dParseDSCCommentsForDocInfo=false",
        "-dPDFNOCIDFALLBACK",
        "-dPDFNOCIDFALLBACK",
        "-dPDFSETTINGS=/screen",
        "-dPreserveAnnots=true",
        "-dPreserveCopyPage=false",
        "-dPreserveDeviceN=false",
        "-dPreserveDeviceN=true",
        "-dPreserveEPSInfo=false",
        "-dPreserveEPSInfo=false",
        "-dPreserveHalftoneInfo=false",
        "-dPreserveOPIComments=false",
        "-dPreserveOverprintSettings=false",
        "-dPreserveOverprintSettings=true",
        "-dPreserveSeparation=false",
        "-dPreserveSeparation=true",
        "-dPrinted=false",
        "-dProcessColorModel=/DeviceRGB",
        "-dSAFER",
        "-dSubsetFonts=true",
        "-dTransferFunctionInfo=/Apply",
        "-dTransferFunctionInfo=/Preserve",
        "-dUCRandBGInfo=/Remove"
    };

    public static readonly string[] Lossy =
    {
        "-dAutoFilterGrayImages=false",
        "-dAutoFilterColorImages=false",
        "-dAutoFilterMonoImages=true",
        "-dColorImageFilter=/DCTEncode",
        "-dDownsampleColorImages=true",
        "-dDownsampleGrayImages=true",
        "-dDownsampleMonoImages=true",
        "-dGrayImageFilter=/DCTEncode",
        "-dPassThroughJPEGImages=false",
        "-dPassThroughJPXImages=false",
        "-dShowAcroForm=false"
    };

    public static readonly string[] Lossless =
    {
        "-dAutoFilterGrayImages=false",
        "-dAutoFilterColorImages=false",
        "-dAutoFilterMonoImages=false",
        "-dColorImageFilter=/DCTEncode",
        "-dDownsampleColorImages=false",
        "-dDownsampleGrayImages=false",
        "-dDownsampleMonoImages=false",
        "-dGrayImageFilter=/DCTEncode",
        "-dPassThroughJPEGImages=true",
        "-dPassThroughJPXImages=true",
        "-dShowAcroForm=true"
    };

    public static readonly string[] MetadataPre =
    {
        "-c",
        "<< /ColorImageDict << /QFactor 0.76 /Blend 1 /HSamples [2 1 1 2] /VSamples [2 1 1 2] >> >> setdistillerparams << /ColorACSImageDict << /QFactor 0.76 /Blend 1 /HSamples [2 1 1 2] /VSamples [2 1 1 2] >> >> setdistillerparams << /GrayImageDict << /QFactor 0.76 /Blend 1 /HSamples [2 1 1 2] /VSamples [2 1 1 2] >> >> setdistillerparams << /GrayACSImageDict << /QFactor 0.76 /Blend 1 /HSamples [2 1 1 2] /VSamples [2 1 1 2] >> >> setdistillerparams << /AlwaysEmbed [ ] >> setdistillerparams << /NeverEmbed [/Courier /Courier-Bold /Courier-Oblique /Courier-BoldOblique /Helvetica /Helvetica-Bold /Helvetica-Oblique /Helvetica-BoldOblique /Times-Roman /Times-Bold /Times-Italic /Times-BoldItalic /Symbol /ZapfDingbats /Arial] >> setdistillerparams",
        "-f",
        "-c",
        "/originalpdfmark { //pdfmark } bind def /pdfmark { { { counttomark pop } stopped { /pdfmark errordict /unmatchedmark get exec stop } if dup type /nametype ne { /pdfmark errordict /typecheck get exec stop } if dup /DOCINFO eq { (Skipping DOCINFO pdfmark\n) print cleartomark exit } if originalpdfmark exit } loop } def",
        "-f"
    };

    public static readonly string[] MetadataPost =
    {
        "-c", "/pdfmark { originalpdfmark } bind def", "-f",
        "-c", "[ /Producer () /ModDate () /CreationDate () /DOCINFO pdfmark", "-f"
    };
}
