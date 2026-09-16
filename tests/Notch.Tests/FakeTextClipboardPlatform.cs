using System.Runtime.InteropServices;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;

namespace Notch.Tests;

internal sealed class FakeTextClipboardPlatform : IClipboardPlatform
{
    public event EventHandler? Changed;
    public uint SequenceNumber { get; private set; }
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public int ReadFailures { get; set; }
    public int WriteFailures { get; set; }
    public bool IsStarted { get; private set; }
    public string? CurrentText { get; private set; }
    public ClipboardContent? CurrentContent { get; private set; }

    public void Start() => IsStarted = true;
    public void Stop() => IsStarted = false;
    public void Emit(string? text)
    {
        CurrentText = text;
        CurrentContent = text is null ? null : new ClipboardContent.Text(text);
        SequenceNumber++;
        if (IsStarted) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void EmitImage(ClipboardContent.Image image)
    {
        CurrentText = null;
        CurrentContent = image;
        SequenceNumber++;
        if (IsStarted) Changed?.Invoke(this, EventArgs.Empty);
    }
    public Task<ClipboardContent?> ReadAsync(int maximumCharacters, CancellationToken cancellationToken)
    {
        Reads++;
        if (ReadFailures-- > 0) throw new ExternalException("Simulated clipboard lock.");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CurrentContent is ClipboardContent.Text text && text.Value.Length > maximumCharacters ? null : CurrentContent);
    }
    public void WriteText(string text)
    {
        Writes++;
        if (WriteFailures-- > 0) throw new ExternalException("Simulated clipboard lock.");
        Emit(text);
    }
}
