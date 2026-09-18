using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notch.Services.Contracts;
using Notch.Services.Translation;
using System.Text;

namespace Notch.Modules.Translator;

public sealed record TranslationLanguage(string? Code, string Name);

public sealed class TranslatorViewModel : ObservableObject, IDisposable
{
    public static TranslationLanguage[] SourceLanguages { get; } = [new("ru", "Русский"), new("en", "English"), new("lv", "Latviešu"), new("de", "Deutsch"), new("fr", "Français"), new("es", "Español"), new("uk", "Українська")];
    public static TranslationLanguage[] TargetLanguages { get; } = SourceLanguages;
    private readonly ITranslationService _service;
    private readonly IClipboardService _clipboard;
    private CancellationTokenSource? _request;
    private string _input = "", _result = "", _status = "Текст переводится онлайн";
    private TranslationLanguage _source = SourceLanguages[1], _target = SourceLanguages[0];
    private bool _busy, _disposed;
    private int _revision;
    public TranslatorViewModel(ITranslationService service, IClipboardService clipboard)
    {
        _service = service; _clipboard = clipboard;
        TranslateCommand = new AsyncRelayCommand(TranslateAsync, () => !_disposed && !IsBusy && !string.IsNullOrWhiteSpace(Input) && Encoding.UTF8.GetByteCount(Input) <= TranslationLimits.MaximumUtf8Bytes);
        CopyCommand = new AsyncRelayCommand(CopyAsync, () => !_disposed && Result.Length > 0);
        SwapCommand = new RelayCommand(Swap, () => Source.Code is not null);
    }
    public string Input { get => _input; set { if (SetProperty(ref _input, value)) Invalidate(); } }
    public string Result { get => _result; private set { if (SetProperty(ref _result, value)) CopyCommand.NotifyCanExecuteChanged(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public TranslationLanguage Source { get => _source; set { if (value is not null && SetProperty(ref _source, value)) { Invalidate(); SwapCommand.NotifyCanExecuteChanged(); } } }
    public TranslationLanguage Target { get => _target; set { if (value is not null && SetProperty(ref _target, value)) Invalidate(); } }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) TranslateCommand.NotifyCanExecuteChanged(); } }
    public IAsyncRelayCommand TranslateCommand { get; }
    public IAsyncRelayCommand CopyCommand { get; }
    public IRelayCommand SwapCommand { get; }
    private void Invalidate()
    {
        _revision++; _request?.Cancel(); Result = "";
        Status = Encoding.UTF8.GetByteCount(Input) > TranslationLimits.MaximumUtf8Bytes ? "Текст слишком длинный. Переведите его по частям." : "Текст переводится онлайн";
        TranslateCommand.NotifyCanExecuteChanged();
    }
    private async Task TranslateAsync()
    {
        var revision = _revision;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _request = cancellation; IsBusy = true; Result = ""; Status = "Переводим…";
        try
        {
            var result = Source.Code == Target.Code ? new TranslationResult(Input, Source.Code)
                : await _service.TranslateAsync(new(Input, Source.Code, Target.Code!), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (revision != _revision || _disposed) return;
            Result = result.Text; Status = "Готово";
        }
        catch (TranslationServiceException ex) { if (revision == _revision && !_disposed) Status = ex.Message; }
        catch (OperationCanceledException) { if (revision == _revision && !_disposed) Status = "Перевод отменён или истекло время ожидания"; }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or InvalidOperationException or NotSupportedException or System.IO.IOException)
        { if (revision == _revision && !_disposed) Status = ex is NotSupportedException ? "Перевод временно недоступен" : "Не удалось перевести. Попробуйте ещё раз."; }
        finally { if (ReferenceEquals(_request, cancellation)) _request = null; IsBusy = false; }
    }
    private async Task CopyAsync()
    {
        try { await _clipboard.WriteAsync(new ClipboardContent.Text(Result), CancellationToken.None); Status = "Скопировано"; }
        catch (System.Runtime.InteropServices.ExternalException) { Status = "Буфер занят. Попробуйте ещё раз."; }
    }
    private void Swap()
    {
        var source = Source; var target = Target; var text = Result;
        Source = target; Target = source;
        if (text.Length > 0) Input = text;
    }
    public void Cancel() { _revision++; _request?.Cancel(); if (IsBusy) Status = "Перевод отменён"; }
    public void Dispose() { _disposed = true; Cancel(); }
}



