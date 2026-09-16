using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Infrastructure.Windows.Clipboard;

public sealed class WindowsClipboardPlatform : IClipboardPlatform, IDisposable
{
    private readonly NativeMessageWindow _messages;
    private readonly uint _excludeFormat;
    private readonly uint _historyFormat;
    private readonly uint _pngFormat;
    private readonly WpfClipboardImageCodec _codec;
    private bool _listening;
    public event EventHandler? Changed;
    public uint SequenceNumber => GetClipboardSequenceNumber();

    public WindowsClipboardPlatform(NativeMessageWindow messages, WpfClipboardImageCodec codec)
    {
        _messages = messages;
        _codec = codec;
        _pngFormat = RegisterClipboardFormat("PNG");
        _excludeFormat = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        _historyFormat = RegisterClipboardFormat("CanIncludeInClipboardHistory");
        messages.MessageReceived += OnMessage;
    }

    public void Start()
    {
        _messages.Dispatcher.VerifyAccess();
        if (_listening) return;
        if (!AddClipboardFormatListener(_messages.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
        _listening = true;
    }

    public void Stop()
    {
        _messages.Dispatcher.VerifyAccess();
        if (!_listening) return;
        RemoveClipboardFormatListener(_messages.Handle);
        _listening = false;
    }

    private void OnMessage(object? sender, NativeMessageEventArgs e)
    {
        if (_listening && e.Message == 0x031D) Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ClipboardContent?> ReadAsync(int maximumCharacters, CancellationToken cancellationToken)
    {
        _messages.Dispatcher.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? image = null;
        var isPng = false;
        if (!OpenClipboard(_messages.Handle)) throw new ExternalException("Clipboard is locked.");
        try
        {
            if (_excludeFormat != 0 && IsClipboardFormatAvailable(_excludeFormat)) return null;
            if (!AllowsHistory()) return null;
            if (_pngFormat != 0 && IsClipboardFormatAvailable(_pngFormat))
            {
                image = ReadBytes(_pngFormat, ClipboardImageLimits.MaximumPngBytes);
                isPng = true;
            }
            else if (IsClipboardFormatAvailable(17)) image = ReadBytes(17, ClipboardImageLimits.MaximumDibBytes);
            else if (IsClipboardFormatAvailable(8)) image = ReadBytes(8, ClipboardImageLimits.MaximumDibBytes);
            else if (IsClipboardFormatAvailable(13)) return ReadText(maximumCharacters);
        }
        finally { CloseClipboard(); }
        if (image is null) return null;
        // The native clipboard is closed before decoding, hashing, and encoding.
        return isPng ? await _codec.NormalizePngAsync(image, cancellationToken) : await _codec.FromDibAsync(image, cancellationToken);
    }

    private static ClipboardContent.Text? ReadText(int maximumCharacters)
    {
            var handle = GetClipboardData(13);
            if (handle == 0) throw new ExternalException("Clipboard data is not ready.");
            var bytes = (ulong)GlobalSize(handle);
            // Bound memory before allocating a managed string; never truncate a saved item.
            if (bytes < 2 || bytes % 2 != 0 || bytes > ((ulong)maximumCharacters + 1) * 2) return null;
            var pointer = GlobalLock(handle);
            if (pointer == 0) throw new ExternalException("Cannot lock clipboard data.");
            try
            {
                var value = Marshal.PtrToStringUni(pointer, (int)(bytes / 2))!;
                var terminator = value.IndexOf('\0');
                return terminator < 0 ? null : new ClipboardContent.Text(value[..terminator]);
            }
            finally { GlobalUnlock(handle); }
    }

    private static byte[] ReadBytes(uint format, int maximumBytes)
    {
        var handle = GetClipboardData(format);
        if (handle == 0) throw new ExternalException("Clipboard image is not ready.");
        var size = (ulong)GlobalSize(handle);
        if (size == 0 || size > (ulong)maximumBytes) throw new NotSupportedException("Clipboard image exceeds size limit.");
        var pointer = GlobalLock(handle);
        if (pointer == 0) throw new ExternalException("Cannot lock clipboard image.");
        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { GlobalUnlock(handle); }
    }

    private bool AllowsHistory()
    {
        if (_historyFormat == 0 || !IsClipboardFormatAvailable(_historyFormat)) return true;
        var handle = GetClipboardData(_historyFormat);
        if (handle == 0 || (ulong)GlobalSize(handle) < 4) return false;
        var pointer = GlobalLock(handle);
        if (pointer == 0) return false;
        try { return Marshal.ReadInt32(pointer) != 0; }
        finally { GlobalUnlock(handle); }
    }

    public void WriteText(string text)
    {
        _messages.Dispatcher.VerifyAccess();
        System.Windows.Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText);
    }

    public void Dispose()
    {
        Stop();
        _messages.MessageReceived -= OnMessage;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);
}
