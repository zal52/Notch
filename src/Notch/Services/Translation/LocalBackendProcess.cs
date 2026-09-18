using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Services.Translation;

public sealed class LocalBackendProcess : IAsyncDisposable
{
    private Process? _process;
    public string? Endpoint { get; private set; }
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public async Task StartAsync(CancellationToken cancellationToken, string? executable = null)
    {
        if (_process is not null) throw new InvalidOperationException("Backend already started.");
        var start = new ProcessStartInfo(executable ?? Path.Combine(AppContext.BaseDirectory, "backend", "Notch.Backend.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add("--desktop");
        _process = Process.Start(start) ?? throw new IOException("Backend could not start.");
        // Drain stderr without persisting request data or server diagnostics.
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await _process.StandardInput.WriteLineAsync(Token.AsMemory(), deadline.Token);
            await _process.StandardInput.FlushAsync(deadline.Token);
            var address = await _process.StandardOutput.ReadLineAsync(deadline.Token);
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "http"
                || uri.Host != "127.0.0.1" || uri.Port <= 0 || uri.UserInfo.Length != 0
                || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
                throw new IOException("Invalid backend startup response.");
            Endpoint = uri.GetLeftPart(UriPartial.Authority);
        }
        catch { await DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        var process = _process;
        _process = null;
        Endpoint = null;
        if (process is null) return;
        try
        {
            process.StandardInput.Close();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        finally { process.Dispose(); }
    }
}
