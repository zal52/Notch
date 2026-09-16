using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Screenshots;

namespace Notch.Infrastructure.Windows.Capture;

public sealed class ScreenshotFiles(string previewDirectory, Action<string> openFile) : IScreenshotFiles, IDisposable
{
    private readonly Dictionary<Guid, string> _previews = new();

    public async Task SaveAsync(ReadOnlyMemory<byte> png, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(destination);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await stream.WriteAsync(png, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, true);
        }
        finally { TryDelete(temporary); }
    }

    public async Task OpenAsync(Guid id, ReadOnlyMemory<byte> png, CancellationToken cancellationToken)
    {
        if (!_previews.TryGetValue(id, out var path) || !File.Exists(path))
        {
            Directory.CreateDirectory(previewDirectory);
            path = Path.Combine(previewDirectory, id.ToString("N") + ".png");
            await SaveAsync(png, path, cancellationToken);
            _previews[id] = path;
        }
        cancellationToken.ThrowIfCancellationRequested();
        openFile(path);
    }

    public void ForgetPreview(Guid id)
    {
        if (_previews.TryGetValue(id, out var path) && TryDelete(path)) _previews.Remove(id);
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        foreach (var path in _previews.Values) TryDelete(path);
        _previews.Clear();
        try { if (Directory.Exists(previewDirectory)) Directory.Delete(previewDirectory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
