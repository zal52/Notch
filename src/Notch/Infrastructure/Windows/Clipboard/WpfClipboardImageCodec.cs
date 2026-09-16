using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;

namespace Notch.Infrastructure.Windows.Clipboard;

public sealed class WpfClipboardImageCodec : IClipboardImageCodec
{
    public Task<ClipboardContent.Image> NormalizePngAsync(ReadOnlyMemory<byte> png, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var data = png.Span;
            if (data.Length < 33 || data.Length > ClipboardImageLimits.MaximumPngBytes
                || !data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                || BinaryPrimitives.ReadUInt32BigEndian(data[8..]) != 13
                || !data.Slice(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("Invalid PNG header.");
            var width = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
            var height = BinaryPrimitives.ReadInt32BigEndian(data[20..]);
            ValidateSize(width, height);
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(png.ToArray(), false);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            return Normalize(decoder.Frames[0], width, height, cancellationToken);
        }, cancellationToken);

    public Task<ClipboardContent.Image> FromDibAsync(ReadOnlyMemory<byte> dib, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var data = dib.Span;
            if (data.Length < 40 || data.Length > ClipboardImageLimits.MaximumDibBytes) throw new InvalidDataException("Invalid DIB size.");
            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data);
            if (headerSize is not (40 or 108 or 124) || headerSize > data.Length) throw new NotSupportedException("Unsupported DIB header.");
            if (headerSize == 124 && (BinaryPrimitives.ReadUInt32LittleEndian(data[112..]) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(data[116..]) != 0))
                throw new NotSupportedException("DIB color profiles are not supported.");
            var width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
            var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
            if (signedHeight == int.MinValue) throw new InvalidDataException("Invalid DIB height.");
            var height = Math.Abs(signedHeight);
            ValidateSize(width, height);
            var planes = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
            var depth = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
            var compression = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
            var colors = BinaryPrimitives.ReadUInt32LittleEndian(data[32..]);
            if (planes != 1 || depth is not (1 or 4 or 8 or 16 or 24 or 32) || compression is not (0 or 3)
                || (compression == 3 && depth is not (16 or 32)) || colors > 256)
                throw new NotSupportedException("Unsupported DIB layout.");
            if (depth <= 8 && colors == 0) colors = 1u << depth;
            var masks = headerSize == 40 && compression == 3 ? 12 : 0;
            var pixelOffset = checked(headerSize + masks + (int)colors * 4);
            var stride = ((long)width * depth + 31) / 32 * 4;
            if (pixelOffset + stride * height > data.Length) throw new InvalidDataException("Truncated DIB pixels.");
            cancellationToken.ThrowIfCancellationRequested();
            // WIC's BMP decoder requires the 14-byte file header, which CF_DIB omits.
            var bmp = new byte[checked(data.Length + 14)];
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), pixelOffset + 14);
            data.CopyTo(bmp.AsSpan(14));
            using var stream = new MemoryStream(bmp, false);
            var decoder = new BmpBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            return Normalize(decoder.Frames[0], width, height, cancellationToken);
        }, cancellationToken);

    private static void ValidateSize(int width, int height)
    {
        if (!ClipboardImageLimits.ValidDimensions(width, height)) throw new NotSupportedException("Image exceeds pixel limit.");
    }

    private static ClipboardContent.Image Normalize(BitmapSource source, int width, int height, CancellationToken token)
    {
        if (source.PixelWidth != width || source.PixelHeight != height) throw new InvalidDataException("Image dimensions do not match the header.");
        token.ThrowIfCancellationRequested();
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(width * height * 4)];
        converted.CopyPixels(pixels, width * 4, 0);
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] == 0) pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
        var hash = Convert.ToHexString(SHA256.HashData(pixels));
        var normalized = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        normalized.Freeze();
        var png = Encode(normalized);
        if (png.Length > ClipboardImageLimits.MaximumPngBytes) throw new NotSupportedException("Encoded image exceeds memory limit.");
        token.ThrowIfCancellationRequested();
        var scale = Math.Min(1, Math.Min(160d / width, 90d / height));
        var thumbnail = new TransformedBitmap(normalized, new ScaleTransform(scale, scale));
        thumbnail.Freeze();
        var thumbnailPng = Encode(thumbnail);
        token.ThrowIfCancellationRequested();
        return new ClipboardContent.Image(png, width, height) { PixelHash = hash, ThumbnailPng = thumbnailPng };
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
