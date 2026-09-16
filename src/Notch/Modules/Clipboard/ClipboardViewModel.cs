using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notch.Services.Contracts;
using System.IO;
using System.Windows.Media.Imaging;

namespace Notch.Modules.Clipboard;

public sealed class ClipboardItemViewModel
{
    public ClipboardEntry Entry { get; }
    public bool IsImage => Entry.Content is ClipboardContent.Image;
    public BitmapSource? Thumbnail { get; }
    public ClipboardItemViewModel(ClipboardEntry entry)
    {
        Entry = entry;
        if (entry.Content is not ClipboardContent.Image image) return;
        using var stream = new MemoryStream((image.ThumbnailPng.IsEmpty ? image.PngBytes : image.ThumbnailPng).ToArray(), false);
        var thumbnail = new BitmapImage();
        thumbnail.BeginInit();
        thumbnail.CacheOption = BitmapCacheOption.OnLoad;
        thumbnail.DecodePixelWidth = 96;
        thumbnail.StreamSource = stream;
        thumbnail.EndInit();
        thumbnail.Freeze();
        Thumbnail = thumbnail;
    }
    public string Preview
    {
        get
        {
            if (IsImage) return "Изображение";
            var text = ((ClipboardContent.Text)Entry.Content).Value;
            var preview = text[..Math.Min(180, text.Length)].Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return string.IsNullOrWhiteSpace(preview) ? "Пробелы и переносы строк" : preview;
        }
    }
    public string Detail => Entry.Content is ClipboardContent.Image image
        ? $"{image.Width} × {image.Height} · {Entry.CapturedAt:HH:mm}"
        : $"{((ClipboardContent.Text)Entry.Content).Value.Length:N0} симв. · {Entry.CapturedAt:HH:mm}";
}

public sealed class ClipboardViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService _clipboard;
    private string? _message;
    private readonly CancellationTokenSource _lifetime = new();
    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();
    public bool IsEmpty => Items.Count == 0;
    public bool IsMonitoring => _clipboard.IsMonitoring;
    public string EmptyTitle => IsMonitoring ? "Пока пусто" : "История на паузе";
    public string EmptyDescription => IsMonitoring ? "Скопируйте текст или изображение." : "Включите историю в меню Notch.";
    public string Footer => _clipboard.StatusMessage ?? _message ?? (IsMonitoring ? "Нажмите, чтобы скопировать" : "Запись истории приостановлена");
    public IAsyncRelayCommand<ClipboardItemViewModel> CopyCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public ClipboardViewModel(IClipboardService clipboard)
    {
        _clipboard = clipboard;
        CopyCommand = new AsyncRelayCommand<ClipboardItemViewModel>(CopyAsync);
        ClearCommand = new RelayCommand(() => { _message = null; _clipboard.ClearHistory(); }, () => Items.Count > 0);
        _clipboard.HistoryChanged += OnHistoryChanged;
        Refresh();
    }

    private async Task CopyAsync(ClipboardItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            await _clipboard.WriteAsync(item.Entry.Content, _lifetime.Token);
            _message = "Скопировано";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or IOException or NotSupportedException)
        { _message = "Не удалось скопировать. Попробуйте ещё раз."; }
        OnPropertyChanged(nameof(Footer));
    }

    private void OnHistoryChanged(object? sender, EventArgs e) { _message = null; Refresh(); }

    private void Refresh()
    {
        var entries = _clipboard.History.ToArray();
        if (!Items.Select(item => item.Entry.Id).SequenceEqual(entries.Select(entry => entry.Id)))
        {
            var existing = Items.ToDictionary(item => item.Entry.Id);
            Items.Clear();
            foreach (var entry in entries) Items.Add(existing.TryGetValue(entry.Id, out var item) ? item : new ClipboardItemViewModel(entry));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsMonitoring));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(Footer));
        ClearCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _clipboard.HistoryChanged -= OnHistoryChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
