using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;
using Notch.Shell;
using Notch.Services.Screenshots;

namespace Notch.Services;

// Coordinates application-wide work independently of module visibility.
public sealed class ApplicationCoordinator(
    ShellViewModel shell, ISettingsService settings, IClipboardService clipboard, IHotkeyService hotkeys,
    ClipboardScreenshotBridge? screenshotBridge = null)
    : IAsyncDisposable
{
    private NotchSettings _current = new();
    private IDisposable? _hotkey;
    private Task _saveTask = Task.CompletedTask;
    private Task _monitoringTask = Task.CompletedTask;
    private bool _started;
    public event EventHandler? ToggleRequested;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started) return;
        try
        {
            _current = await settings.LoadAsync(cancellationToken);
            shell.StatusMessage = settings.LoadWarning;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { shell.StatusMessage = "Не удалось прочитать настройки. Используются значения по умолчанию."; }
        shell.IsTopmost = _current.Topmost;
        shell.AnimationsEnabled = _current.AnimationsEnabled;
        shell.ClipboardEnabled = _current.ClipboardEnabled;
        try { _hotkey = hotkeys.Register(_current.ToggleHotkey, () => ToggleRequested?.Invoke(this, EventArgs.Empty)); }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException)
        { shell.StatusMessage = "Горячая клавиша недоступна. Откройте Notch через значок в трее."; }
        if (screenshotBridge is not null)
        {
            screenshotBridge.ImportFailed += OnScreenshotImportFailed;
            screenshotBridge.Start();
        }
        await SetMonitoringAfterAsync(Task.CompletedTask, shell.ClipboardEnabled);
        _started = true;
        shell.PropertyChanged += OnShellPropertyChanged;
    }

    private void OnScreenshotImportFailed(object? sender, EventArgs e) =>
        shell.StatusMessage = "Не удалось добавить изображение из буфера в скриншоты.";

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ShellViewModel.IsTopmost) or nameof(ShellViewModel.AnimationsEnabled)
            or nameof(ShellViewModel.ClipboardEnabled))) return;
        _current = _current with
        {
            Topmost = shell.IsTopmost,
            AnimationsEnabled = shell.AnimationsEnabled,
            ClipboardEnabled = shell.ClipboardEnabled
        };
        _saveTask = SaveAfterAsync(_saveTask, _current);
        if (e.PropertyName == nameof(ShellViewModel.ClipboardEnabled))
            _monitoringTask = SetMonitoringAfterAsync(_monitoringTask, shell.ClipboardEnabled);
    }

    private async Task SaveAfterAsync(Task previous, NotchSettings snapshot)
    {
        await previous;
        try { await settings.SaveAsync(snapshot, CancellationToken.None); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        { shell.StatusMessage = "Не удалось сохранить настройки. Изменения действуют до закрытия Notch."; }
    }

    private async Task SetMonitoringAfterAsync(Task previous, bool enabled)
    {
        await previous;
        try
        {
            if (enabled) await clipboard.StartAsync(CancellationToken.None);
            else await clipboard.StopAsync(CancellationToken.None);
        }
        catch (Win32Exception)
        { shell.StatusMessage = "Не удалось подключиться к буферу обмена. Перезапустите Notch."; }
    }

    public async ValueTask DisposeAsync()
    {
        shell.PropertyChanged -= OnShellPropertyChanged;
        if (screenshotBridge is not null)
        {
            screenshotBridge.ImportFailed -= OnScreenshotImportFailed;
            await screenshotBridge.DisposeAsync();
        }
        _hotkey?.Dispose();
        _hotkey = null;
        await _monitoringTask;
        await clipboard.StopAsync(CancellationToken.None);
        await _saveTask;
        _started = false;
    }
}
