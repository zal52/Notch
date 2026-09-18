using System.Net;
using System.Net.Http;
using Notch.Services.Contracts;
using Notch.Backend.Translation;
using Xunit;
namespace Notch.Tests;
public sealed class MyMemoryTranslationTests
{
 [Fact]
 public async Task EncodesRequestAndDecodesResult()
 {
  var handler = new Handler("""{"responseStatus":200,"quotaFinished":false,"responseData":{"translatedText":"Привет &amp; мир"}}""");
  using var client = new HttpClient(handler);
  var result = await new MyMemoryTranslationService(client).TranslateAsync(new("hello & ? # +", "en", "ru"), default);
  Assert.Equal("Привет & мир", result.Text);
  Assert.Contains("q=hello%20%26%20%3F%20%23%20%2B", handler.Uri!.OriginalString);
  Assert.Contains("langpair=en%7Cru", handler.Uri.OriginalString);
 }
 [Theory]
 [InlineData("{\"responseStatus\":403,\"quotaFinished\":true}")]
 [InlineData("{\"responseStatus\":500}")]
 [InlineData("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"\"}}")]
 [InlineData("not json")]
 public async Task ErrorsNeverAppearAsTranslatedText(string body)
 {
  using var client = new HttpClient(new Handler(body));
  await Assert.ThrowsAsync<TranslationServiceException>(() => new MyMemoryTranslationService(client).TranslateAsync(new("hello", "en", "ru"), default));
 }
 [Fact]
 public async Task Utf8LimitAndMissingLanguageRejectWithoutNetwork()
 {
  var handler = new Handler("{}");
  using var client = new HttpClient(handler);
  var service = new MyMemoryTranslationService(client);
  await Assert.ThrowsAsync<TranslationServiceException>(() => service.TranslateAsync(new(new string('я', 251), "ru", "en"), default));
  await Assert.ThrowsAsync<TranslationServiceException>(() => service.TranslateAsync(new("hello", null, "ru"), default));
  Assert.Null(handler.Uri);
  using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TranslateAsync(new("hello", "en", "ru"), cancellation.Token));
  Assert.Null(handler.Uri);
 }
 [Fact]
 public async Task HttpRateLimitIsExplained()
 {
  using var client = new HttpClient(new Handler("") { Status = HttpStatusCode.TooManyRequests });
  var error = await Assert.ThrowsAsync<TranslationServiceException>(() => new MyMemoryTranslationService(client).TranslateAsync(new("hello", "en", "ru"), default));
  Assert.Contains("Лимит", error.Message);
 }
 [Fact]
 public async Task OptionalLiveProviderCheck()
 {
  if (Environment.GetEnvironmentVariable("NOTCH_LIVE_TRANSLATION") != "1") return;
  using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
  var result = await new MyMemoryTranslationService(client).TranslateAsync(new("Hello world", "en", "ru"), default);
  Assert.Contains("мир", result.Text.ToLowerInvariant());
 }
 private sealed class Handler(string body) : HttpMessageHandler
 {
  public Uri? Uri { get; private set; }
  public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
  { Uri = request.RequestUri; return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body) }); }
 }
}

