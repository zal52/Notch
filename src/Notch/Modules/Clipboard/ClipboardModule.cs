using Notch.Modules.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Notch.Modules.Clipboard;

public sealed class ClipboardModule(ClipboardViewModel viewModel) : INotchModule
{
    public static ModuleDescriptor Metadata { get; } = new(
        "clipboard", "Буфер обмена", "Всё под рукой", "\uE77F", 1);
    public ModuleDescriptor Descriptor => Metadata;
    public object ViewModel => viewModel;
    public ValueTask ActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
    public ValueTask DeactivateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
