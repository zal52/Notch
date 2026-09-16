using System;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;

namespace Notch.Services.Screenshots;

public sealed record CapturedScreenshot(int Width, int Height, ReadOnlyMemory<byte> Png, ReadOnlyMemory<byte> ThumbnailPng);

public interface IScreenshotCaptureBackend
{
    Task<CapturedScreenshot> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);
}

public interface ICaptureVisibility
{
    // Dispose on the UI context to restore the original visibility, including on failure.
    Task<IDisposable> HideAsync(CancellationToken cancellationToken);
}

public interface IScreenshotFiles
{
    Task SaveAsync(ReadOnlyMemory<byte> png, string destination, CancellationToken cancellationToken);
    Task OpenAsync(Guid id, ReadOnlyMemory<byte> png, CancellationToken cancellationToken);
    void ForgetPreview(Guid id);
}

public interface ISaveFilePicker
{
    string? ChoosePngPath(string suggestedName);
}
