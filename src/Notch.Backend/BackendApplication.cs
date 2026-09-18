using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Notch.Backend.Translation;
using Notch.Services.Contracts;

namespace Notch.Backend;
public static class BackendApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null, string? localToken = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5187");
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 8192;
            options.Limits.MaxConcurrentConnections = 100;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });
        // No request/body logs or HttpClientFactory URL logs containing upstream secrets/text.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DailyTranslationBudget>();
        builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = TimeSpan.FromSeconds(15) });
        builder.Services.AddSingleton<ITranslationService>(services => new MyMemoryTranslationService(
            services.GetRequiredService<HttpClient>(), Environment.GetEnvironmentVariable("NOTCH_MYMEMORY_KEY")));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetFixedWindowLimiter("global", _ => new()
                    { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })),
                PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter("global", _ => new()
                    { PermitLimit = 4, QueueLimit = 0 })));
        });
        configure?.Invoke(builder);
        var app = builder.Build();
        // Never return developer exception pages or raw provider errors.
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception) { if (!context.Response.HasStarted) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "translation_unavailable" }); } }
        });
        if (localToken is not null)
        {
            app.Use(async (context, next) =>
            {
                var expected = System.Text.Encoding.UTF8.GetBytes("Bearer " + localToken);
                var actual = System.Text.Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
                if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual))
                { context.Response.StatusCode = 401; return; }
                await next(context);
            });
        }
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapPost("/v1/translate", TranslateAsync);
        return app;
    }

    private static async Task<IResult> TranslateAsync(HttpContext context, ITranslationService provider, DailyTranslationBudget budget)
    {
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
        if (context.Request.ContentLength > 8192)
        {
            if (context.Request.Protocol.StartsWith("HTTP/1", StringComparison.Ordinal)) context.Response.Headers.Connection = "close";
            return Results.StatusCode(413);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        TranslationRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<TranslationRequest>(cancellationToken: deadline.Token); }
        catch (JsonException) { return Results.BadRequest(new { error = "invalid_request" }); }
        catch (BadHttpRequestException ex) { return Results.StatusCode(ex.StatusCode); }
        catch (OperationCanceledException) { return Results.StatusCode(408); }
        if (!TranslationLimits.IsValid(request)) return Results.BadRequest(new { error = "invalid_request" });
        if (request!.SourceLanguage == request.TargetLanguage) return Results.Ok(new TranslationResult(request.Text, request.SourceLanguage));
        if (!budget.TryReserve(request.Text.Length)) return Results.Json(new { error = "daily_limit" }, statusCode: 429);
        try { return Results.Ok(await provider.TranslateAsync(request, deadline.Token)); }
        catch (OperationCanceledException) { return Results.StatusCode(504); }
        catch (Exception ex) when (ex is TranslationServiceException or HttpRequestException or IOException or InvalidOperationException)
        { return Results.Json(new { error = "translation_unavailable" }, statusCode: 502); }
    }
}


