using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;
using Notch.Services.Clipboard;

namespace Notch.Services.Screenshots;

public sealed class ScreenshotService(IScreenshotCaptureBackend backend, ICaptureVisibility visibility, IScreenshotFiles files,
    IClipboardImageCodec? codec = null)
    : IScreenshotService, IAsyncDisposable
{
    private sealed record Stored(ScreenshotEntry Entry, ClipboardContent.Image Image);
    private readonly List<Stored> _recent = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private bool _disposed;
    public IReadOnlyList<ScreenshotEntry> Recent => _recent.Select(s => s.Entry).ToArray();
    public event EventHandler? RecentChanged;

    public async Task<ScreenshotEntry> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CapturedScreenshot image;
            using (await visibility.HideAsync(cancellationToken))
                image = await backend.CaptureAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (image.Width <= 0 || image.Height <= 0 || image.Png.IsEmpty || image.ThumbnailPng.IsEmpty
                || image.Png.Length > 32 * 1024 * 1024 || image.ThumbnailPng.Length > 1024 * 1024)
                throw new InvalidOperationException("Invalid or oversized screenshot.");
            var content = new ClipboardContent.Image(image.Png, image.Width, image.Height) { ThumbnailPng = image.ThumbnailPng };
            if (codec is not null) content = await codec.NormalizePngAsync(image.Png, cancellationToken);
            return Add(content, false);
        }
        finally { _captureGate.Release(); }
    }

    public async Task<ScreenshotEntry> ImportAsync(ClipboardContent.Image image, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (codec is not null && (image.PixelHash is null || image.ThumbnailPng.IsEmpty))
                image = await codec.NormalizePngAsync(image.PngBytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ClipboardImageLimits.ValidDimensions(image.Width, image.Height) || image.PngBytes.IsEmpty
                || image.PngBytes.Length > ClipboardImageLimits.MaximumPngBytes || image.ThumbnailPng.IsEmpty
                || image.ThumbnailPng.Length > 1024 * 1024) throw new InvalidOperationException("Invalid clipboard image.");
            var existing = _recent.Find(item => ClipboardImageLimits.SameImage(item.Image, image));
            if (existing is not null)
            {
                if (!ReferenceEquals(_recent[0], existing))
                {
                    _recent.Remove(existing);
                    _recent.Insert(0, existing);
                    RecentChanged?.Invoke(this, EventArgs.Empty);
                }
                return existing.Entry;
            }
            return Add(image, true);
        }
        finally { _captureGate.Release(); }
    }

    private ScreenshotEntry Add(ClipboardContent.Image image, bool isFromClipboard)
    {
        var entry = new ScreenshotEntry(Guid.NewGuid(), DateTimeOffset.Now, image.Width, image.Height, image.ThumbnailPng, isFromClipboard);
        _recent.Insert(0, new Stored(entry, image));
        if (_recent.Count > 3)
        {
            var oldest = _recent[^1];
            _recent.RemoveAt(_recent.Count - 1);
            files.ForgetPreview(oldest.Entry.Id);
        }
        RecentChanged?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    private Stored Find(Guid id) => _recent.Find(s => s.Entry.Id == id)
        ?? throw new InvalidOperationException("Screenshot is no longer in history.");

    public Task<ClipboardContent.Image> GetImageAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = Find(id);
        return Task.FromResult(stored.Image);
    }

    public Task SaveAsync(Guid id, string destination, CancellationToken cancellationToken) =>
        files.SaveAsync(Find(id).Image.PngBytes, destination, cancellationToken);

    public Task OpenAsync(Guid id, CancellationToken cancellationToken) =>
        files.OpenAsync(id, Find(id).Image.PngBytes, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = Find(id);
        _recent.Remove(item);
        files.ForgetPreview(id); // Never touches a separately exported file.
        RecentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _captureGate.WaitAsync();
        try
        {
            foreach (var item in _recent) files.ForgetPreview(item.Entry.Id);
            _recent.Clear();
        }
        finally { _captureGate.Release(); }
    }
}
