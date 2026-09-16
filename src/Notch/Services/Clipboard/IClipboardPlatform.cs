using System;
using Notch.Services.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Services.Clipboard;

// Native access starts on the owning STA; expensive image encoding runs off-thread.
// ExternalException signals a temporary lock and can be retried by the service.
public interface IClipboardPlatform
{
    event EventHandler? Changed;
    uint SequenceNumber { get; }
    void Start();
    void Stop();
    Task<ClipboardContent?> ReadAsync(int maximumCharacters, CancellationToken cancellationToken);
    void WriteText(string text);
}

public interface IImageClipboardPlatform
{
    void WriteImage(ClipboardContent.Image image);
}
