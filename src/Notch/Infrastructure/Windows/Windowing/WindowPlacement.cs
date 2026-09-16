using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Notch.Infrastructure.Windows.Windowing;

public sealed class WindowPlacement : IDisposable
{
    private readonly Window _window;
    private HwndSource? _source;
    private nint _handle;
    private bool _refreshQueued;
    private bool _disposed;
    public event EventHandler? DisplayChanged;

    public WindowPlacement(Window window)
    {
        _window = window;
        window.SourceInitialized += OnSourceInitialized;
        window.SizeChanged += OnSizeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WindowProc);
        // A utility window belongs in neither the taskbar nor the Alt+Tab list.
        const int extendedStyle = -20;
        var style = NativeMethods.GetWindowLongPtr(_handle, extendedStyle);
        NativeMethods.SetWindowLongPtr(_handle, extendedStyle, (style | 0x80) & ~0x40000);
        Center();
    }

    public double DpiScale => _handle == 0 ? 1 : Math.Max(96, NativeMethods.GetDpiForWindow(_handle)) / 96d;

    public MonitorBounds GetPrimaryBounds()
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new MonitorBounds(info.Monitor.Left, info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
    }

    public Size Constrain(Size desired)
    {
        var bounds = GetPrimaryBounds();
        return new Size(Math.Max(1, Math.Min(desired.Width, bounds.Width / DpiScale - 16)),
            Math.Max(1, Math.Min(desired.Height, bounds.Height / DpiScale - 24)));
    }

    public void Center()
    {
        if (_handle == 0 || _disposed) return;
        var position = PlacementMath.TopCenter(GetPrimaryBounds(), _window.Width, DpiScale);
        if (NativeMethods.GetWindowRect(_handle, out var rect) && rect.Left == position.X && rect.Top == position.Y) return;
        if (!NativeMethods.SetWindowPos(_handle, 0, position.X, position.Y, 0, 0,
                NativeMethods.NoSize | NativeMethods.NoZOrder | NativeMethods.NoActivate))
            System.Diagnostics.Trace.TraceWarning("Could not reposition Notch: {0}", Marshal.GetLastWin32Error());
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Center();

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is NativeMethods.WmDpiChanged or NativeMethods.WmDisplayChange or NativeMethods.WmSettingChange)
        {
            // Let WPF apply its DPI update before reading the new transform.
            if (!_refreshQueued)
            {
                _refreshQueued = true;
                _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    _refreshQueued = false;
                    if (_disposed) return;
                    Center();
                    DisplayChanged?.Invoke(this, EventArgs.Empty);
                }));
            }
        }
        return 0;
    }

    public void Dispose()
    {
        _disposed = true;
        _window.SourceInitialized -= OnSourceInitialized;
        _window.SizeChanged -= OnSizeChanged;
        _source?.RemoveHook(WindowProc);
    }
}
