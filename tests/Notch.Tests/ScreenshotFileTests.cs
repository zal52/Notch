using System.IO;
using Notch.Infrastructure.Windows.Capture;
using Xunit;

namespace Notch.Tests;

public sealed class ScreenshotFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "capture-file-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DeletingPreviewLeavesUserExportUntouched()
    {
        Directory.CreateDirectory(_directory);
        string? opened = null;
        using var files = new ScreenshotFiles(Path.Combine(_directory, "previews"), path => opened = path);
        byte[] png = [1, 2, 3];
        var export = Path.Combine(_directory, "saved.png");
        var id = Guid.NewGuid();
        await files.SaveAsync(png, export, default);
        await files.OpenAsync(id, png, default);
        Assert.NotNull(opened);
        Assert.True(File.Exists(opened));
        files.ForgetPreview(id);
        Assert.False(File.Exists(opened));
        Assert.Equal(png, await File.ReadAllBytesAsync(export));
    }

    [Fact]
    public async Task CancelledSaveDoesNotTruncateExistingFile()
    {
        Directory.CreateDirectory(_directory);
        var export = Path.Combine(_directory, "saved.png");
        await File.WriteAllBytesAsync(export, new byte[] { 42 });
        using var files = new ScreenshotFiles(Path.Combine(_directory, "previews"), _ => { });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.SaveAsync(new byte[] { 1 }, export, cancelled.Token));
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(export));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory)) return;
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
