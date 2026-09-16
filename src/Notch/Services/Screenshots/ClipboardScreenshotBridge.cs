using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;

namespace Notch.Services.Screenshots;

// Application-owned: imports happen even if neither module has ever been opened.
public sealed class ClipboardScreenshotBridge(IClipboardService clipboard, IScreenshotService screenshots) : IAsyncDisposable
{
    private readonly Queue<ClipboardContent.Image> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task _worker = Task.CompletedTask;
    private bool _started;
    private bool _disposed;
    public event EventHandler? ImportFailed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        clipboard.EntryAdded += OnEntryAdded;
    }

    private void OnEntryAdded(object? sender, ClipboardEntryAddedEventArgs e)
    {
        if (!e.IsExternal || e.Entry.Content is not ClipboardContent.Image image) return;
        if (_pending.Count == 3) _pending.Dequeue();
        _pending.Enqueue(image);
        if (_worker.IsCompleted) _worker = ImportPendingAsync();
    }

    private async Task ImportPendingAsync()
    {
        await Task.Yield();
        while (_pending.Count > 0 && !_lifetime.IsCancellationRequested)
        {
            var image = _pending.Dequeue();
            try { await screenshots.ImportAsync(image, _lifetime.Token); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or NotSupportedException
                or System.Runtime.InteropServices.ExternalException or ArgumentException or FormatException)
            { ImportFailed?.Invoke(this, EventArgs.Empty); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        clipboard.EntryAdded -= OnEntryAdded;
        _lifetime.Cancel();
        await _worker;
        _pending.Clear();
        _lifetime.Dispose();
    }
}
