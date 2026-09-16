using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Services.Contracts;

public abstract record ClipboardContent
{
    public sealed record Text(string Value) : ClipboardContent;
    public sealed record Image(ReadOnlyMemory<byte> PngBytes, int Width, int Height) : ClipboardContent
    {
        public ReadOnlyMemory<byte> ThumbnailPng { get; init; }
        public string? PixelHash { get; init; }
    }
}
public sealed record ClipboardEntry(Guid Id, DateTimeOffset CapturedAt, ClipboardContent Content);
public sealed class ClipboardEntryAddedEventArgs(ClipboardEntry entry, bool isExternal) : EventArgs
{
    public ClipboardEntry Entry { get; } = entry;
    public bool IsExternal { get; } = isExternal;
}

public interface IClipboardService
{
    IReadOnlyList<ClipboardEntry> History { get; }
    bool IsMonitoring { get; }
    string? StatusMessage { get; }
    // Events are raised on the creating UI/STA context, including status changes.
    event EventHandler? HistoryChanged;
    event EventHandler<ClipboardEntryAddedEventArgs>? EntryAdded;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task WriteAsync(ClipboardContent content, CancellationToken cancellationToken);
    void ClearHistory();
}
