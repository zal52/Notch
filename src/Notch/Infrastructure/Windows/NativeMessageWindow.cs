using System;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Notch.Infrastructure.Windows;

public sealed class NativeMessageEventArgs(int message, nint wParam, nint lParam) : EventArgs
{
    public int Message { get; } = message;
    public nint WParam { get; } = wParam;
    public nint LParam { get; } = lParam;
}

// Application lifetime, independent of the visible notch and of module activation.
public sealed class NativeMessageWindow : IDisposable
{
    private readonly HwndSource _source;
    public nint Handle => _source.Handle;
    public Dispatcher Dispatcher => _source.Dispatcher;
    public event EventHandler<NativeMessageEventArgs>? MessageReceived;

    public NativeMessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("Notch.Messages")
        {
            ParentWindow = new nint(-3), // HWND_MESSAGE: never rendered or activated.
            WindowStyle = 0, Width = 0, Height = 0
        });
        _source.AddHook(WindowProc);
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        MessageReceived?.Invoke(this, new NativeMessageEventArgs(message, wParam, lParam));
        return 0;
    }

    public void Dispose()
    {
        _source.RemoveHook(WindowProc);
        _source.Dispose();
    }
}
