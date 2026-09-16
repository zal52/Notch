using System.IO;
using Notch.Infrastructure.Persistence;
using Notch.Services.Contracts;
using Xunit;

namespace Notch.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "settings-tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public async Task RoundTripPersistsPreferencesAndCustomHotkey()
    {
        var store = new JsonSettingsService(SettingsPath);
        Assert.Equal(new NotchSettings(), await store.LoadAsync(default));
        var expected = new NotchSettings(Topmost: false, AnimationsEnabled: false, ClipboardEnabled: false)
        { ToggleHotkey = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4E) };
        await store.SaveAsync(expected, default);
        Assert.Equal(expected, await new JsonSettingsService(SettingsPath).LoadAsync(default));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task CorruptFileIsPreservedAndDefaultsAreLoaded()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{broken");
        var store = new JsonSettingsService(SettingsPath);
        Assert.Equal(new NotchSettings(), await store.LoadAsync(default));
        Assert.NotNull(store.LoadWarning);
        var backup = Assert.Single(Directory.GetFiles(_directory, "settings.json.invalid-*"));
        Assert.Equal("{broken", await File.ReadAllTextAsync(backup));
        await store.SaveAsync(new(), default);
        Assert.Equal(new NotchSettings(), await store.LoadAsync(default));
    }

    [Fact]
    public async Task FutureSchemaIsNeverOverwritten()
    {
        Directory.CreateDirectory(_directory);
        const string future = "{\"SchemaVersion\":99,\"FutureSetting\":true}";
        await File.WriteAllTextAsync(SettingsPath, future);
        var store = new JsonSettingsService(SettingsPath);
        await store.LoadAsync(default);
        Assert.NotNull(store.LoadWarning);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new(), default));
        Assert.Equal(future, await File.ReadAllTextAsync(SettingsPath));
    }

    [Fact]
    public async Task InvalidHotkeyFallsBackWithoutResettingOtherSettings()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{\"Topmost\":false,\"ToggleHotkey\":{\"Modifiers\":\"None\",\"VirtualKey\":65}}");
        var store = new JsonSettingsService(SettingsPath);
        var result = await store.LoadAsync(default);
        Assert.False(result.Topmost);
        Assert.Equal(new NotchSettings().ToggleHotkey, result.ToggleHotkey);
        Assert.NotNull(store.LoadWarning);
    }

    [Fact]
    public async Task CancelledSavePreservesExistingFile()
    {
        var store = new JsonSettingsService(SettingsPath);
        await store.SaveAsync(new(), default);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(Topmost: false), cancelled.Token));
        Assert.True((await store.LoadAsync(default)).Topmost);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory)) return;
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
