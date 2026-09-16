using Notch.Services.Screenshots;
using Notch.Services.Contracts;
using Notch.Services.Clipboard;

namespace Notch.Tests;

internal sealed class FakeScreenshotBackend : IScreenshotCaptureBackend
{
    public CapturedScreenshot Frame { get; set; } = new(1920, 1080, new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });
    public Func<CancellationToken, Task>? BeforeCapture { get; set; }
    public async Task<CapturedScreenshot> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        if (BeforeCapture is not null) await BeforeCapture(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Frame;
    }
}

internal sealed class FakeCaptureVisibility : ICaptureVisibility
{
    public bool IsHidden { get; private set; }
    public int Restorations { get; private set; }
    public Task<IDisposable> HideAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsHidden = true;
        return Task.FromResult<IDisposable>(new Restore(() => { IsHidden = false; Restorations++; }));
    }
    private sealed class Restore(Action action) : IDisposable { public void Dispose() => action(); }
}

internal sealed class FakeScreenshotFiles : IScreenshotFiles
{
    public List<Guid> Forgotten { get; } = new();
    public Guid? Opened { get; private set; }
    public byte[]? Saved { get; private set; }
    public string? Destination { get; private set; }
    public Task SaveAsync(ReadOnlyMemory<byte> png, string destination, CancellationToken cancellationToken)
    { Saved = png.ToArray(); Destination = destination; return Task.CompletedTask; }
    public Task OpenAsync(Guid id, ReadOnlyMemory<byte> png, CancellationToken cancellationToken)
    { Opened = id; return Task.CompletedTask; }
    public void ForgetPreview(Guid id) => Forgotten.Add(id);
}

internal sealed class FakeSaveFilePicker : ISaveFilePicker
{
    public string? Path { get; set; }
    public string? ChoosePngPath(string suggestedName) => Path;
}

internal sealed class FakeImageClipboard : IImageClipboardPlatform
{
    public ClipboardContent.Image? LastImage { get; private set; }
    public void WriteImage(ClipboardContent.Image image) => LastImage = image;
}
