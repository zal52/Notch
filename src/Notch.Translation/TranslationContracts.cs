using System.Text;
namespace Notch.Services.Contracts;

public sealed record TranslationRequest(string Text, string? SourceLanguage, string TargetLanguage);
public sealed record TranslationResult(string Text, string? DetectedSourceLanguage);
public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}
public sealed class TranslationServiceException(string message) : Exception(message);
public static class TranslationLimits
{
    public const int MaximumUtf8Bytes = 500;
    public static bool Supported(string? language) => language is "ru" or "en" or "lv" or "de" or "fr" or "es" or "uk";
    public static bool IsValid(TranslationRequest? request) => request is not null
        && !string.IsNullOrWhiteSpace(request.Text) && Encoding.UTF8.GetByteCount(request.Text) <= MaximumUtf8Bytes
        && Supported(request.SourceLanguage) && Supported(request.TargetLanguage);
}
