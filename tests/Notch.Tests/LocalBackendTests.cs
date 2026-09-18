using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Notch.Services.Translation;
using Xunit;

namespace Notch.Tests;

public sealed class LocalBackendTests
{
    private static string Executable => Path.Combine(AppContext.BaseDirectory, "Notch.Backend.exe");

    [Fact]
    public async Task AutoServerUsesFreePortRequiresSessionTokenAndStops()
    {
        var first = new LocalBackendProcess();
        await using var second = new LocalBackendProcess();
        string endpoint;
        try
        {
            await first.StartAsync(default, Executable);
            await second.StartAsync(default, Executable);
            endpoint = first.Endpoint!;
            Assert.NotEqual(endpoint, second.Endpoint);
            Assert.NotEqual(first.Token, second.Token);
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            using var unauthorized = await client.GetAsync(endpoint + "/health");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", second.Token);
            using var wrongToken = await client.GetAsync(endpoint + "/health");
            Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
            var service = new BackendTranslationService(client, endpoint, first.Token);
            Assert.Equal("Hello", (await service.TranslateAsync(new("Hello", "en", "en"), default)).Text);
            if (Environment.GetEnvironmentVariable("NOTCH_LIVE_TRANSLATION") == "1")
                Assert.Contains("мир", (await service.TranslateAsync(new("Hello world", "en", "ru"), default)).Text, StringComparison.OrdinalIgnoreCase);
        }
        finally { await first.DisposeAsync(); }
        using var after = new HttpClient(new HttpClientHandler { UseProxy = false });
        await Assert.ThrowsAsync<HttpRequestException>(() => after.GetAsync(endpoint + "/health"));
    }

    [Fact]
    public async Task ServerExitsWhenParentPipeCloses()
    {
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--desktop");
        using var child = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await child.StandardInput.WriteLineAsync(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            await child.StandardInput.FlushAsync();
            Assert.StartsWith("http://127.0.0.1:", await child.StandardOutput.ReadLineAsync(timeout.Token));
            child.StandardInput.Close();
            await child.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, child.ExitCode);
        }
        finally { if (!child.HasExited) child.Kill(true); }
    }
}

