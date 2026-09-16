using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Notch.Services.Screenshots;
using Notch.Shell;
using Notch.Infrastructure.Windows.Capture;

namespace Notch.Presentation.Behaviors;

public sealed class NotchCaptureVisibility(Func<NotchWindow> getWindow) : ICaptureVisibility
{
    public async Task<IDisposable> HideAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = getWindow();
        window.Dispatcher.VerifyAccess();
        var wasVisible = window.IsVisible;
        window.IsCaptureSuppressed = true;
        window.Hide();
        var restore = new Restore(() =>
        {
            window.IsCaptureSuppressed = false;
            if (wasVisible && window.IsLoaded && !window.AllowClose) window.Show();
        });
        try
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await DesktopCompositor.WaitForRefreshAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return restore;
        }
        catch { restore.Dispose(); throw; }
    }

    private sealed class Restore(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose() { _action?.Invoke(); _action = null; }
    }

}
