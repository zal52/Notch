using System;

namespace Notch.Services.Contracts;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    public bool IsValid() => VirtualKey is >= 0x30 and <= 0xFE and not 0x7B
        && (Modifiers & ~(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Windows)) == 0
        && (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) != 0;
}

public interface IHotkeyService
{
    // Disposal unregisters the hotkey. A conflict must be reported to the caller.
    IDisposable Register(HotkeyGesture gesture, Action callback);
}
