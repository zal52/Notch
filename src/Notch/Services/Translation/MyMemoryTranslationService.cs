using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;
namespace Notch.Services.Translation;

public sealed class TranslationServiceException(string message) : Exception(message);

public sealed class MyMemoryTranslationService(HttpClient client) : ITranslationService
{
    public const int MaximumUtf8Bytes = 500;
    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Text)) throw new TranslationServiceException("Введите текст для перевода");
        if (Encoding.UTF8.GetByteCount(request.Text) > MaximumUtf8Bytes)
            throw new TranslationServiceException("Текст слишком длинный. Переведите его по частям.");
        if (!Supported(request.SourceLanguage) || !Supported(request.TargetLanguage))
            throw new TranslationServiceException("Выберите исходный язык и язык перевода");
        if (request.SourceLanguage == request.TargetLanguage) return new(request.Text, request.SourceLanguage);
        var uri = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(request.Text)
            + "&langpair=" + Uri.EscapeDataString(request.SourceLanguage + "|" + request.TargetLanguage);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new TranslationServiceException("Лимит переводов исчерпан. Попробуйте позже.");
        response.EnsureSuccessStatusCode();
        // Bound the response before parsing; never log URLs containing user text.
        await response.Content.LoadIntoBufferAsync(1024 * 1024, cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            if (root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
                throw new TranslationServiceException("Достигнут дневной лимит переводов");
            if (!root.TryGetProperty("responseStatus", out var status) || status.ToString() != "200")
                throw new TranslationServiceException("Не удалось перевести текст. Попробуйте позже.");
            if (!root.TryGetProperty("responseData", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("translatedText", out var translated) || translated.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(translated.GetString()))
                throw new TranslationServiceException("Перевод не найден. Попробуйте изменить текст.");
            return new(WebUtility.HtmlDecode(translated.GetString()!), request.SourceLanguage);
        }
        catch (JsonException) { throw new TranslationServiceException("Не удалось получить перевод. Попробуйте позже."); }
    }
    private static bool Supported(string? language) => language is "ru" or "en" or "lv" or "de" or "fr" or "es" or "uk";
}

