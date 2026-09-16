using System;

namespace Notch.Infrastructure.Windows.Windowing;

public readonly record struct MonitorBounds(int Left, int Top, int Width, int Height);
public readonly record struct PixelPosition(int X, int Y);

public static class PlacementMath
{
    public static PixelPosition TopCenter(MonitorBounds monitor, double widthDip, double dpiScale, double topOffsetDip = 8)
    {
        if (dpiScale <= 0) throw new ArgumentOutOfRangeException(nameof(dpiScale));
        var widthPixels = (int)Math.Round(widthDip * dpiScale);
        return new PixelPosition(monitor.Left + (monitor.Width - widthPixels) / 2,
            monitor.Top + (int)Math.Round(topOffsetDip * dpiScale));
    }
}
