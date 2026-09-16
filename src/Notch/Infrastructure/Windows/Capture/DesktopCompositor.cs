using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Infrastructure.Windows.Capture;

public static class DesktopCompositor
{
    public static Task WaitForRefreshAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Marshal.ThrowExceptionForHR(DwmFlush()), cancellationToken);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
