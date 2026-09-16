using System.Threading;
using System.Threading.Tasks;

namespace Notch.Services.Contracts;

public sealed record TranslationRequest(string Text, string? SourceLanguage, string TargetLanguage);
public sealed record TranslationResult(string Text, string? DetectedSourceLanguage);

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}
