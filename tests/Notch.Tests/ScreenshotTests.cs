using Notch.Services.Contracts;
using Notch.Services.Screenshots;
using Notch.Services.Clipboard;
using Xunit;

namespace Notch.Tests;

public sealed class ScreenshotTests
{
    [Fact]
    public async Task FourthCaptureEvictsOldestAndKeepsNewestThree()
    {
        var visibility = new FakeCaptureVisibility();
        var files = new FakeScreenshotFiles();
        await using var service = new ScreenshotService(new FakeScreenshotBackend(), visibility, files);
        var first = await service.CaptureAsync(new(), default);
        await service.CaptureAsync(new(), default);
        await service.CaptureAsync(new(), default);
        var last = await service.CaptureAsync(new(), default);
        Assert.Equal(3, service.Recent.Count);
        Assert.Equal(last.Id, service.Recent[0].Id);
        Assert.DoesNotContain(service.Recent, s => s.Id == first.Id);
        Assert.Contains(first.Id, files.Forgotten);
        Assert.False(visibility.IsHidden);
        Assert.Equal(4, visibility.Restorations);
    }

    [Fact]
    public async Task CaptureFailureRestoresVisibilityAndPreservesHistory()
    {
        var visibility = new FakeCaptureVisibility();
        var backend = new FakeScreenshotBackend();
        await using var service = new ScreenshotService(backend, visibility, new FakeScreenshotFiles());
        var first = await service.CaptureAsync(new(), default);
        backend.BeforeCapture = _ => throw new InvalidOperationException("capture failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(new(), default));
        Assert.False(visibility.IsHidden);
        Assert.Equal(first.Id, Assert.Single(service.Recent).Id);
    }

    [Fact]
    public async Task CancellationRestoresNotchWithoutAddingPartialImage()
    {
        var visibility = new FakeCaptureVisibility();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeScreenshotBackend
        {
            BeforeCapture = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
        };
        await using var service = new ScreenshotService(backend, visibility, new FakeScreenshotFiles());
        using var cancellation = new CancellationTokenSource();
        var capturing = service.CaptureAsync(new(), cancellation.Token);
        await entered.Task;
        Assert.True(visibility.IsHidden);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capturing);
        Assert.False(visibility.IsHidden);
        Assert.Empty(service.Recent);
    }

    [Fact]
    public async Task ActionsUseFullResolutionAndDeletionOnlyReleasesOwnedPreview()
    {
        var backend = new FakeScreenshotBackend();
        var files = new FakeScreenshotFiles();
        await using var service = new ScreenshotService(backend, new FakeCaptureVisibility(), files);
        var shot = await service.CaptureAsync(new(), default);
        var image = await service.GetImageAsync(shot.Id, default);
        Assert.Equal(1920, image.Width);
        Assert.Equal(backend.Frame.Png.ToArray(), image.PngBytes.ToArray());
        await service.SaveAsync(shot.Id, "user-export.png", default);
        await service.OpenAsync(shot.Id, default);
        Assert.Equal(shot.Id, files.Opened);
        await service.DeleteAsync(shot.Id, default);
        Assert.Empty(service.Recent);
        Assert.Contains(shot.Id, files.Forgotten);
        Assert.Equal("user-export.png", files.Destination);
        Assert.Equal(backend.Frame.Png.ToArray(), files.Saved);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetImageAsync(shot.Id, default));
    }

    [Fact]
    public async Task InvalidCaptureIsRejectedAfterVisibilityIsRestored()
    {
        var visibility = new FakeCaptureVisibility();
        var backend = new FakeScreenshotBackend { Frame = new CapturedScreenshot(0, 0, new byte[] { 1 }, new byte[] { 2 }) };
        await using var service = new ScreenshotService(backend, visibility, new FakeScreenshotFiles());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(new(), default));
        Assert.Empty(service.Recent);
        Assert.False(visibility.IsHidden);
    }

    [Fact]
    public async Task CapturesAreSerializedSoVisibilityScopesCannotOverlap()
    {
        var active = 0;
        var maximum = 0;
        var backend = new FakeScreenshotBackend
        {
            BeforeCapture = async token =>
            {
                maximum = Math.Max(maximum, ++active);
                await Task.Delay(20, token);
                active--;
            }
        };
        await using var service = new ScreenshotService(backend, new FakeCaptureVisibility(), new FakeScreenshotFiles());
        await Task.WhenAll(service.CaptureAsync(new(), default), service.CaptureAsync(new(), default));
        Assert.Equal(1, maximum);
        Assert.Equal(2, service.Recent.Count);
    }

    [Fact]
    public async Task ClipboardCanCopyScreenshotIntoMixedHistory()
    {
        var images = new FakeImageClipboard();
        var text = new FakeTextClipboardPlatform();
        await using var clipboard = new ClipboardService(text, new ClipboardHistory(), images);
        await clipboard.StartAsync(default);
        var image = new ClipboardContent.Image(new byte[] { 1, 2, 3 }, 1920, 1080);
        await clipboard.WriteAsync(image, default);
        Assert.Same(image, images.LastImage);
        Assert.IsType<ClipboardContent.Image>(Assert.Single(clipboard.History).Content);
        Assert.Equal(0, text.Writes);
    }
}
