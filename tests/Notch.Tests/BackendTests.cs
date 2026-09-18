using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Notch.Backend;
using Notch.Services.Contracts;
using Notch.Services.Translation;
using Xunit;

namespace Notch.Tests;
public sealed class BackendTests
{
    [Fact]
    public async Task DesktopUsesPostBodyAndNeverProviderUrl()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var service = new BackendTranslationService(client, "https://notch.example/api");
        var result = await service.TranslateAsync(new("hello & secret", "en", "ru"), default);
        Assert.Equal("Привет", result.Text);
        Assert.Equal("https://notch.example/api/v1/translate", handler.Uri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Contains("hello", handler.Body);
        Assert.Empty(handler.Uri.Query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://untrusted.example")]
    [InlineData("https://user:password@example.org")]
    [InlineData("https://example.org?token=not-a-real-secret")]
    public async Task InvalidConfigurationDoesNotSendRequests(string? url)
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<TranslationServiceException>(() => new BackendTranslationService(client, url).TranslateAsync(new("hello", "en", "ru"), default));
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task ServerValidatesBeforeProviderAndSanitizesErrors()
    {
        var provider = new FakeProvider();
        await using var app = BackendApplication.Build([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<ITranslationService>(provider);
        });
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            var desktop = new BackendTranslationService(client, address);
            Assert.Equal("Привет", (await desktop.TranslateAsync(new("Hello", "en", "ru"), default)).Text);
            Assert.Equal(1, provider.Calls);
            using var invalid = await client.PostAsJsonAsync("/v1/translate", new { text = "Hello", sourceLanguage = "invalid", targetLanguage = "ru", url = "http://attacker.example" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var oversized = await client.PostAsJsonAsync("/v1/translate", new { text = new string('я', 251), sourceLanguage = "ru", targetLanguage = "en" });
            Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
            using var malformed = await client.PostAsync("/v1/translate", new StringContent("{broken", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            using var huge = await client.PostAsync("/v1/translate", new StringContent(new string('a', 9000), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
            Assert.Equal(1, provider.Calls);
            provider.Fail = true;
            using var failure = await client.PostAsJsonAsync("/v1/translate", new TranslationRequest("Hello", "en", "ru"));
            Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
            var body = await failure.Content.ReadAsStringAsync();
            Assert.DoesNotContain("provider-secret", body);
            Assert.Contains("translation_unavailable", body);
            Assert.True(failure.Headers.CacheControl!.NoStore);
            for (var i = 0; i < 31; i++)
            {
                using var response = await client.GetAsync("/health");
                if (i == 30) Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            }
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public void DailyBudgetIsAtomicAndResetsNextUtcDay()
    {
        var clock = new TestClock();
        var budget = new DailyTranslationBudget(clock, 500);
        var accepted = 0;
        Parallel.For(0, 30, _ => { if (budget.TryReserve(100)) Interlocked.Increment(ref accepted); });
        Assert.Equal(5, accepted);
        Assert.False(budget.TryReserve(1));
        clock.Now = clock.Now.AddDays(1);
        Assert.True(budget.TryReserve(500));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? Uri;
        public HttpMethod? Method;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uri = request.RequestUri; Method = request.Method;
            Body = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new TranslationResult("Привет", "en")) };
        }
    }
    private sealed class FakeProvider : ITranslationService
    {
        public int Calls;
        public bool Fail;
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token)
        {
            Calls++;
            if (Fail) throw new TranslationServiceException("provider-secret must not leave server");
            return Task.FromResult(new TranslationResult("Привет", "en"));
        }
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
