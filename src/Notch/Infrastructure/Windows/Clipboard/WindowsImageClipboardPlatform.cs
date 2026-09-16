using System.IO;
using System.Windows.Media.Imaging;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;

namespace Notch.Infrastructure.Windows.Clipboard;

public sealed class WindowsImageClipboardPlatform : IImageClipboardPlatform
{
    public void WriteImage(ClipboardContent.Image image)
    {
        using var stream = new MemoryStream(image.PngBytes.ToArray(), false);
        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        System.Windows.Clipboard.SetImage(bitmap);
    }
}
