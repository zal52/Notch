using System.ComponentModel;
using Notch.Modules;
using Notch.Services;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;
using Notch.Shell;
using Xunit;

namespace Notch.Tests;

public sealed class CoordinatorTests
{
    private sealed class Settings : ISettingsService
    {
        public string? LoadWarning => null;
        public NotchSettings Stored { get; set; } = new();
        public int Saves { get; private set; }
        public Task<NotchSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Stored);
        public async Task SaveAsync(NotchSettings settings, CancellationToken cancellationToken)
        {
            // Deliberately asynchronous to catch out-of-order persistence.
            await Task.Delay(10, cancellationToken);
            Stored = settings;
            Saves++;
        }
    }

    private sealed class Hotkeys : IHotkeyService
    {
        public bool Conflict { get; set; }
        public Action? Callback { get; private set; }
        public IDisposable Register(HotkeyGesture gesture, Action callback)
        {
            if (Conflict) throw new Win32Exception(1409);
            Callback = callback;
            return new Cleanup(() => Callback = null);
        }
        private sealed class Cleanup(Action action) : IDisposable { public void Dispose() => action(); }
    }

    [Fact]
    public async Task PreferencesAreAppliedBeforeMonitoringAndFinalSnapshotIsFlushed()
    {
        var settings = new Settings { Stored = new NotchSettings(Topmost: false, ClipboardEnabled: false) };
        var hotkeys = new Hotkeys();
        var platform = new FakeTextClipboardPlatform();
        await using var clipboard = new ClipboardService(platform, new ClipboardHistory());
        await using var shell = new ShellViewModel(new ModuleCatalog([]));
        var coordinator = new ApplicationCoordinator(shell, settings, clipboard, hotkeys);
        await coordinator.StartAsync(default);
        Assert.False(shell.IsTopmost);
        Assert.False(clipboard.IsMonitoring);
        Assert.Equal(0, settings.Saves);
        shell.ClipboardEnabled = true;
        Assert.True(clipboard.IsMonitoring);
        shell.IsTopmost = true;
        shell.AnimationsEnabled = false;
        shell.IsTopmost = false;
        await coordinator.DisposeAsync();
        Assert.False(settings.Stored.Topmost);
        Assert.False(settings.Stored.AnimationsEnabled);
        Assert.True(settings.Stored.ClipboardEnabled);
        Assert.False(clipboard.IsMonitoring);
        Assert.Null(hotkeys.Callback);
        Assert.Equal(4, settings.Saves);
    }

    [Fact]
    public async Task HotkeyConflictDoesNotPreventClipboardStartup()
    {
        var settings = new Settings();
        var hotkeys = new Hotkeys { Conflict = true };
        await using var clipboard = new ClipboardService(new FakeTextClipboardPlatform(), new ClipboardHistory());
        await using var shell = new ShellViewModel(new ModuleCatalog([]));
        await using var coordinator = new ApplicationCoordinator(shell, settings, clipboard, hotkeys);
        await coordinator.StartAsync(default);
        Assert.NotNull(shell.StatusMessage);
        Assert.True(clipboard.IsMonitoring);
    }

    [Fact]
    public async Task RegisteredHotkeyRequestsShellToggleAndIsRemovedOnShutdown()
    {
        var hotkeys = new Hotkeys();
        await using var clipboard = new ClipboardService(new FakeTextClipboardPlatform(), new ClipboardHistory());
        await using var shell = new ShellViewModel(new ModuleCatalog([]));
        var coordinator = new ApplicationCoordinator(shell, new Settings(), clipboard, hotkeys);
        var toggles = 0;
        coordinator.ToggleRequested += (_, _) => toggles++;
        await coordinator.StartAsync(default);
        hotkeys.Callback!();
        Assert.Equal(1, toggles);
        await coordinator.DisposeAsync();
        Assert.Null(hotkeys.Callback);
    }
}
