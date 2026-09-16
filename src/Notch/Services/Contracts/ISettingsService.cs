using System.Threading;
using System.Threading.Tasks;

namespace Notch.Services.Contracts;

public sealed record NotchSettings(int SchemaVersion = 1, bool Topmost = true, string? MonitorId = null,
    bool AnimationsEnabled = true, bool ClipboardEnabled = true)
{
    public HotkeyGesture ToggleHotkey { get; init; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4E);
}

public interface ISettingsService
{
    string? LoadWarning { get; }
    Task<NotchSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(NotchSettings settings, CancellationToken cancellationToken);
}
