using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Notch.Infrastructure.Windows.Windowing;
using Notch.Services.Contracts;
using Notch.Services.Screenshots;

namespace Notch.Infrastructure.Windows.Capture;

public sealed class GdiScreenshotBackend : IScreenshotCaptureBackend
{
    public Task<CapturedScreenshot> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        if (request.MonitorId is not null and not "primary") throw new NotSupportedException("Only the primary monitor is supported.");
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1), ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var bounds = info.Monitor;
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            if (width <= 0 || height <= 0 || (long)width * height > 24_000_000)
                throw new NotSupportedException("The display exceeds the current 24-megapixel capture limit.");
            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
                CapturePixels(graphics, bounds.Left, bounds.Top, width, height);
            cancellationToken.ThrowIfCancellationRequested();
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            var ratio = Math.Min(320d / width, 180d / height);
            using var thumbnail = new Bitmap(Math.Max(1, (int)Math.Round(width * ratio)), Math.Max(1, (int)Math.Round(height * ratio)));
            using (var graphics = Graphics.FromImage(thumbnail))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(bitmap, 0, 0, thumbnail.Width, thumbnail.Height);
            }
            using var thumbnailPng = new MemoryStream();
            thumbnail.Save(thumbnailPng, ImageFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();
            return new CapturedScreenshot(width, height, png.ToArray(), thumbnailPng.ToArray());
        }, cancellationToken);
    }

    private static void CapturePixels(Graphics graphics, int x, int y, int width, int height)
    {
        var source = GetDC(0);
        if (source == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var destination = graphics.GetHdc();
            try
            {
                // Include layered windows in the desktop image (SRCCOPY | CAPTUREBLT).
                if (!BitBlt(destination, 0, 0, width, height, source, x, y, 0x00CC0020 | 0x40000000))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { graphics.ReleaseHdc(destination); }
        }
        finally { ReleaseDC(0, source); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rasterOperation);
}
