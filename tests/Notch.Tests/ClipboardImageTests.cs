using System.Buffers.Binary;
using System.IO;
using Notch.Infrastructure.Windows.Clipboard;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;
using Notch.Services.Screenshots;
using Xunit;

namespace Notch.Tests;

public sealed class ClipboardImageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowsDibAndPngRoundTripPreservesPixels(bool v5)
    {
        var codec = new WpfClipboardImageCodec();
        var image = await codec.FromDibAsync(Dib(v5), default);
        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.False(image.ThumbnailPng.IsEmpty);
        var roundTrip = await codec.NormalizePngAsync(image.PngBytes, default);
        Assert.Equal(image.PixelHash, roundTrip.PixelHash);
        Assert.True(ClipboardImageLimits.SameImage(image, roundTrip));
    }

    [Fact]
    public async Task RejectsTruncatedAndOversizedImagesBeforeDecoding()
    {
        var codec = new WpfClipboardImageCodec();
        await Assert.ThrowsAsync<InvalidDataException>(() => codec.FromDibAsync(Dib(false)[..^1], default));
        var oversized = Dib(false);
        BinaryPrimitives.WriteInt32LittleEndian(oversized.AsSpan(4), 25_000_000);
        await Assert.ThrowsAsync<NotSupportedException>(() => codec.FromDibAsync(oversized, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => codec.NormalizePngAsync(new byte[33], default));
    }

    [Fact]
    public void MixedHistoryAccountsForImageMemoryAndDeduplicatesPixels()
    {
        var history = new ClipboardHistory(imageByteBudget: 8);
        var first = Image(1);
        Assert.True(history.Add(first));
        Assert.False(history.Add(first with { PngBytes = new byte[] { 9, 9, 9 } }));
        Assert.True(history.AddText("keep text"));
        Assert.True(history.Add(Image(2)));
        Assert.True(history.Add(Image(3)));
        Assert.Equal(3, history.Entries.Count);
        Assert.DoesNotContain(history.Entries, e => ReferenceEquals(e.Content, first));
        history.Clear();
        Assert.True(history.Add(first));
    }

    [Fact]
    public async Task BackgroundImportKeepsThreeAndDoesNotReimportOwnCopiesOrDeletedItems()
    {
        var platform = new FakeTextClipboardPlatform();
        await using var clipboard = new ClipboardService(platform, new ClipboardHistory(), new FakeImageClipboard());
        await using var screenshots = new ScreenshotService(new FakeScreenshotBackend(), new FakeCaptureVisibility(), new FakeScreenshotFiles());
        await using var bridge = new ClipboardScreenshotBridge(clipboard, screenshots);
        bridge.Start();
        await clipboard.StartAsync(default);
        for (byte i = 1; i <= 4; i++)
        {
            platform.EmitImage(Image(i));
            await ClipboardTests.UntilAsync(() => screenshots.Recent.Count == Math.Min(i, (byte)3)
                && screenshots.GetImageAsync(screenshots.Recent[0].Id, default).Result.PixelHash == i.ToString());
        }
        Assert.All(screenshots.Recent, entry => Assert.True(entry.IsFromClipboard));
        var restored = await screenshots.GetImageAsync(screenshots.Recent[2].Id, default);
        var newest = screenshots.Recent[0].Id;
        await clipboard.WriteAsync(restored, default);
        await screenshots.DeleteAsync(newest, default);
        platform.Emit("text does not resurrect images");
        await ClipboardTests.UntilAsync(() => clipboard.History[0].Content is ClipboardContent.Text);
        await bridge.DisposeAsync();
        Assert.Equal(2, screenshots.Recent.Count);
        Assert.DoesNotContain(screenshots.Recent, entry => entry.Id == newest);
    }

    internal static ClipboardContent.Image Image(byte value) => new(new byte[] { value, 2, 3 }, 2, 1)
    { ThumbnailPng = new byte[] { 1 }, PixelHash = value.ToString() };

    private static byte[] Dib(bool v5)
    {
        var size = v5 ? 124 : 40;
        var dib = new byte[size + 8];
        BinaryPrimitives.WriteInt32LittleEndian(dib, size);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), -1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        if (v5)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(40), 0x00ff0000);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(44), 0x0000ff00);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(48), 0x000000ff);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(52), 0xff000000);
            BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(56), 0x73524742);
        }
        new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }.CopyTo(dib, size);
        return dib;
    }
}
