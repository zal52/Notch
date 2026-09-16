using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Services.Contracts;

public sealed record ScreenshotEntry(Guid Id, DateTimeOffset CapturedAt, int Width, int Height,
    ReadOnlyMemory<byte> ThumbnailPng, bool IsFromClipboard = false);
public sealed record CaptureRequest(string? MonitorId = null);

public interface IScreenshotService
{
    IReadOnlyList<ScreenshotEntry> Recent { get; }
    event EventHandler? RecentChanged;
    Task<ScreenshotEntry> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);
    Task<ScreenshotEntry> ImportAsync(ClipboardContent.Image image, CancellationToken cancellationToken);
    Task<ClipboardContent.Image> GetImageAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(Guid id, string destination, CancellationToken cancellationToken);
    Task OpenAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
