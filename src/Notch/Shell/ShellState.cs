using System;

namespace Notch.Shell;

public enum NotchMode { Collapsed, Expanded, ModuleOpen }

public sealed record ShellState
{
    public NotchMode Mode { get; }
    public string? ModuleId { get; }
    private ShellState(NotchMode mode, string? moduleId = null) => (Mode, ModuleId) = (mode, moduleId);
    public static ShellState Collapsed { get; } = new(NotchMode.Collapsed);
    public static ShellState Expanded { get; } = new(NotchMode.Expanded);
    public static ShellState OpenModule(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new(NotchMode.ModuleOpen, id);
    }
}
