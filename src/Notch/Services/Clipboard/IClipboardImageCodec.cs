using System;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;

namespace Notch.Services.Clipboard;

public interface IClipboardImageCodec
{
    Task<ClipboardContent.Image> NormalizePngAsync(ReadOnlyMemory<byte> png, CancellationToken cancellationToken);
}

public static class ClipboardImageLimits
{
    public const int MaximumPixels = 24_000_000;
    public const int MaximumPngBytes = 32 * 1024 * 1024;
    public const int MaximumDibBytes = MaximumPixels * 4 + 2048;

    public static bool ValidDimensions(int width, int height) =>
        width > 0 && height > 0 && (long)width * height <= MaximumPixels;

    public static bool SameImage(ClipboardContent.Image left, ClipboardContent.Image right) =>
        left.Width == right.Width && left.Height == right.Height &&
        (left.PixelHash is not null && right.PixelHash is not null
            ? left.PixelHash == right.PixelHash : left.PngBytes.Span.SequenceEqual(right.PngBytes.Span));
}
