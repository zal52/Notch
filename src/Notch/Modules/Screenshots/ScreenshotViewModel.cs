using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notch.Services.Contracts;
using Notch.Services.Screenshots;

namespace Notch.Modules.Screenshots;

public sealed class ScreenshotItemViewModel
{
    public ScreenshotEntry Entry { get; }
    public BitmapSource Thumbnail { get; }
    public string Detail => $"{(Entry.IsFromClipboard ? "Буфер" : "Экран")} · {Entry.CapturedAt:HH:mm} · {Entry.Width} × {Entry.Height}";
    public ScreenshotItemViewModel(ScreenshotEntry entry)
    {
        Entry = entry;
        using var stream = new MemoryStream(entry.ThumbnailPng.ToArray(), false);
        Thumbnail = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Thumbnail.Freeze();
    }
}

public sealed class ScreenshotViewModel : ObservableObject, IDisposable
{
    private readonly IScreenshotService _screenshots;
    private readonly IClipboardService _clipboard;
    private readonly ISaveFilePicker _filePicker;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isBusy;
    private bool _disposed;
    private string _status = "Последние три изображения";
    public string AutoCaptureLabel => _clipboard.IsMonitoring ? "Авто из буфера" : "Авто на паузе";
    public string EmptyHint => _clipboard.IsMonitoring ? "Win+Shift+S — снимок появится здесь." : "Можно снять экран кнопкой выше.";
    public ObservableCollection<ScreenshotItemViewModel> Items { get; } = new();
    public bool IsEmpty => Items.Count == 0;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CaptureCommand.NotifyCanExecuteChanged();
            CopyCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            OpenCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public IAsyncRelayCommand CaptureCommand { get; }
    public IAsyncRelayCommand<ScreenshotItemViewModel> CopyCommand { get; }
    public IAsyncRelayCommand<ScreenshotItemViewModel> SaveCommand { get; }
    public IAsyncRelayCommand<ScreenshotItemViewModel> OpenCommand { get; }
    public IAsyncRelayCommand<ScreenshotItemViewModel> DeleteCommand { get; }

    public ScreenshotViewModel(IScreenshotService screenshots, IClipboardService clipboard, ISaveFilePicker filePicker)
    {
        _screenshots = screenshots;
        _clipboard = clipboard;
        _filePicker = filePicker;
        CaptureCommand = new AsyncRelayCommand(() => RunAsync(async () =>
            { await _screenshots.CaptureAsync(new CaptureRequest(), _lifetime.Token); }, "Снимок готов"), CanRun);
        CopyCommand = new AsyncRelayCommand<ScreenshotItemViewModel>(item => item is null ? Task.CompletedTask : RunAsync(async () =>
        {
            var image = await _screenshots.GetImageAsync(item.Entry.Id, _lifetime.Token);
            await _clipboard.WriteAsync(image, _lifetime.Token);
        }, "Скопировано"), item => item is not null && CanRun());
        SaveCommand = new AsyncRelayCommand<ScreenshotItemViewModel>(SaveAsync, item => item is not null && CanRun());
        OpenCommand = new AsyncRelayCommand<ScreenshotItemViewModel>(item => item is null ? Task.CompletedTask : RunAsync(
            () => _screenshots.OpenAsync(item.Entry.Id, _lifetime.Token), "Открыто в приложении для изображений"), item => item is not null && CanRun());
        DeleteCommand = new AsyncRelayCommand<ScreenshotItemViewModel>(item => item is null ? Task.CompletedTask : RunAsync(
            () => _screenshots.DeleteAsync(item.Entry.Id, _lifetime.Token), "Удалено из истории"), item => item is not null && CanRun());
        _screenshots.RecentChanged += OnRecentChanged;
        _clipboard.HistoryChanged += OnClipboardChanged;
        Refresh();
    }

    private bool CanRun() => !IsBusy && !_disposed;

    private Task SaveAsync(ScreenshotItemViewModel? item) => item is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var path = _filePicker.ChoosePngPath($"Notch-{item.Entry.CapturedAt:yyyy-MM-dd-HHmmss}.png");
        if (path is null) { Status = "Сохранение отменено"; return; }
        await _screenshots.SaveAsync(item.Entry.Id, path, _lifetime.Token);
        Status = "Сохранено";
    }, null);

    private async Task RunAsync(Func<Task> action, string? success)
    {
        if (!CanRun()) return;
        IsBusy = true;
        Status = "Подождите…";
        try
        {
            await action();
            if (success is not null) Status = success;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Runtime.InteropServices.ExternalException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Status = "Не удалось выполнить действие. Попробуйте ещё раз.";
            System.Diagnostics.Trace.TraceWarning("Screenshot action failed: {0}", ex.GetType().Name);
        }
        finally { IsBusy = false; }
    }

    private void OnRecentChanged(object? sender, EventArgs e)
    {
        Refresh();
        if (!IsBusy) Status = "Последние три изображения";
    }
    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AutoCaptureLabel));
        OnPropertyChanged(nameof(EmptyHint));
    }
    private void Refresh()
    {
        Items.Clear();
        foreach (var entry in _screenshots.Recent) Items.Add(new ScreenshotItemViewModel(entry));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _screenshots.RecentChanged -= OnRecentChanged;
        _clipboard.HistoryChanged -= OnClipboardChanged;
        // Pending command continuations may still inspect the cancellation source.
        _lifetime.Dispose();
    }
}
