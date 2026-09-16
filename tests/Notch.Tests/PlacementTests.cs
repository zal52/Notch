using Notch.Infrastructure.Windows.Windowing;
using Xunit;

namespace Notch.Tests;

public sealed class PlacementTests
{
    [Theory]
    [InlineData(1.0, 730, 8)]
    [InlineData(1.25, 672, 10)]
    [InlineData(1.5, 615, 12)]
    [InlineData(2.0, 500, 16)]
    public void CentersInPhysicalPixelsAtEachDpi(double scale, int x, int y)
    {
        var position = PlacementMath.TopCenter(new MonitorBounds(0, 0, 1920, 1080), 460, scale);
        Assert.Equal(new PixelPosition(x, y), position);
    }

    [Fact]
    public void PreservesNegativeMonitorOrigin()
    {
        var position = PlacementMath.TopCenter(new MonitorBounds(-1920, -200, 1920, 1080), 460, 1.5);
        Assert.Equal(new PixelPosition(-1305, -188), position);
    }
}
