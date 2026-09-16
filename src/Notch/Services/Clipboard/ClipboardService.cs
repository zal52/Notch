using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;

namespace Notch.Services.Clipboard;

public sealed class ClipboardService : IClipboardService, IAsyncDisposable
{
    private readonly IClipboardPlatform _platform;
    private readonly ClipboardHistory _history;
    private readonly IImageClipboardPlatform? _images;
    private readonly IClipboardImageCodec? _codec;
    private uint? _ownWriteSequence;
    private CancellationTokenSource? _monitoring;
    private Task _readTask = Task.CompletedTask;
    private bool _readRequested;
    private bool _disposed;
    private int _historyRevision;
    public IReadOnlyList<ClipboardEntry> History => _history.Entries;
    public bool IsMonitoring => _monitoring is not null;
    public string? StatusMessage { get; private set; }
    public event EventHandler? HistoryChanged;
    public event EventHandler<ClipboardEntryAddedEventArgs>? EntryAdded;

    public ClipboardService(IClipboardPlatform platform, ClipboardHistory history, IImageClipboardPlatform? images = null,
        IClipboardImageCodec? codec = null)
    {
        _platform = platform;
        _history = history;
        _images = images;
        _codec = codec;
        _platform.Changed += OnClipboardChanged;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsMonitoring) return Task.CompletedTask;
        _platform.Start();
        _monitoring = new CancellationTokenSource();
        StatusMessage = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        // Do not import pre-existing clipboard data on startup or resume.
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var lifetime = _monitoring;
        if (lifetime is null) return;
        _monitoring = null;
        lifetime.Cancel();
        _platform.Stop();
        await _readTask;
        lifetime.Dispose();
        _readRequested = false;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (_monitoring is null) return;
        _readRequested = true;
        if (_readTask.IsCompleted) _readTask = ReadPendingAsync(_monitoring.Token);
    }

    private async Task ReadPendingAsync(CancellationToken cancellationToken)
    {
        // Exit the native window callback before touching clipboard data.
        await Task.Yield();
        while (_readRequested && !cancellationToken.IsCancellationRequested)
        {
            _readRequested = false;
            var historyRevision = _historyRevision;
            try
            {
                var content = await RetryAsync(async () =>
                {
                    var before = _platform.SequenceNumber;
                    if (_ownWriteSequence == before) return null;
                    var value = await _platform.ReadAsync(ClipboardHistory.MaximumTextLength, cancellationToken);
                    if (before != _platform.SequenceNumber) throw new ExternalException("Clipboard changed during read.");
                    return value;
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (historyRevision != _historyRevision) continue;
                var changed = StatusMessage is not null;
                StatusMessage = null;
                if (content is not null && _history.Add(content))
                {
                    changed = true;
                    EntryAdded?.Invoke(this, new ClipboardEntryAddedEventArgs(_history.Entries[0], true));
                }
                if (changed) HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                if (historyRevision != _historyRevision) continue;
                StatusMessage = "Буфер занят. Следующее копирование будет обработано автоматически.";
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or ArgumentException or FormatException or OverflowException)
            {
                if (historyRevision != _historyRevision) continue;
                StatusMessage = "Изображение слишком большое или его формат не поддерживается.";
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public async Task WriteAsync(ClipboardContent content, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (content is ClipboardContent.Text text)
        {
            await RetryAsync(() => { _platform.WriteText(text.Value); return true; }, cancellationToken);
        }
        else if (content is ClipboardContent.Image image && _images is not null)
        {
            if (_codec is not null && (image.PixelHash is null || image.ThumbnailPng.IsEmpty))
                image = await _codec.NormalizePngAsync(image.PngBytes, cancellationToken);
            content = image;
            await RetryAsync(() => { _images.WriteImage(image); return true; }, cancellationToken);
        }
        else throw new NotSupportedException("Unsupported clipboard content.");
        _ownWriteSequence = _platform.SequenceNumber;
        _historyRevision++; // An earlier read must not overtake this user action.
        if (IsMonitoring && _history.Add(content))
            EntryAdded?.Invoke(this, new ClipboardEntryAddedEventArgs(_history.Entries[0], false));
        // Do not import our own clipboard writes back into screenshot history.
        StatusMessage = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Task<T> RetryAsync<T>(Func<T> operation, CancellationToken cancellationToken) =>
        RetryAsync(() => Task.FromResult(operation()), cancellationToken);

    private static async Task<T> RetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await operation(); }
            catch (ExternalException) when (attempt < 4)
            {
                await Task.Delay(20 << attempt, cancellationToken);
            }
        }
    }

    public void ClearHistory()
    {
        _historyRevision++;
        _readRequested = false;
        _history.Clear();
        StatusMessage = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _platform.Changed -= OnClipboardChanged;
        await StopAsync(CancellationToken.None);
        _history.Clear();
    }
}
