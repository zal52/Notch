using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;
namespace Notch.Services.Translation;

public sealed class BackendTranslationService(HttpClient client, string? backendUrl, string? localToken = null) : ITranslationService
{
    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (!TranslationLimits.IsValid(request)) throw new TranslationServiceException("Проверьте текст и выбранные языки");
        if (!Uri.TryCreate(backendUrl, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback))
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new TranslationServiceException("Сервер перевода не настроен");
        var uri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/v1/translate");
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(request) };
        if (localToken is not null) message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", localToken);
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode == 429) throw new TranslationServiceException("Лимит переводов исчерпан. Попробуйте позже.");
        if (!response.IsSuccessStatusCode) throw new TranslationServiceException("Сервер перевода временно недоступен");
        await response.Content.LoadIntoBufferAsync(1024 * 1024, cancellationToken);
        try
        {
            var result = await response.Content.ReadFromJsonAsync<TranslationResult>(cancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.Text)) throw new TranslationServiceException("Перевод не найден");
            return result;
        }
        catch (JsonException) { throw new TranslationServiceException("Не удалось получить перевод"); }
    }
}

