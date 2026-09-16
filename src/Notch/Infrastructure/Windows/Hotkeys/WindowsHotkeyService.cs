using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Notch.Services.Contracts;

namespace Notch.Infrastructure.Windows.Hotkeys;

public sealed class WindowsHotkeyService : IHotkeyService, IDisposable
{
    private readonly NativeMessageWindow _messages;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId;
    private bool _disposed;

    public WindowsHotkeyService(NativeMessageWindow messages)
    {
        _messages = messages;
        _messages.MessageReceived += OnMessage;
    }

    public IDisposable Register(HotkeyGesture gesture, Action callback)
    {
        _messages.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        if (!gesture.IsValid()) throw new ArgumentException("Invalid hotkey.", nameof(gesture));
        var id = ++_nextId;
        if (id > 0xBFFF) throw new InvalidOperationException("Hotkey IDs exhausted.");
        if (!RegisterHotKey(_messages.Handle, id, (uint)gesture.Modifiers | 0x4000, (uint)gesture.VirtualKey))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _callbacks.Add(id, callback);
        return new Registration(() => Unregister(id));
    }

    private void OnMessage(object? sender, NativeMessageEventArgs e)
    {
        if (e.Message != 0x0312 || !_callbacks.ContainsKey((int)e.WParam)) return;
        var id = (int)e.WParam;
        _messages.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_callbacks.TryGetValue(id, out var callback)) callback();
        }));
    }

    private void Unregister(int id)
    {
        _messages.Dispatcher.VerifyAccess();
        if (_callbacks.Remove(id)) UnregisterHotKey(_messages.Handle, id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _messages.Dispatcher.VerifyAccess();
        _disposed = true;
        _messages.MessageReceived -= OnMessage;
        foreach (var id in _callbacks.Keys) UnregisterHotKey(_messages.Handle, id);
        _callbacks.Clear();
    }

    private sealed class Registration(Action unregister) : IDisposable
    {
        private Action? _unregister = unregister;
        public void Dispose() { _unregister?.Invoke(); _unregister = null; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
