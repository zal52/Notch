using System.Threading;
using System.Threading.Tasks;
using Notch.Modules.Contracts;

namespace Notch.Modules;

// Lifecycle is deliberately idle until a module gains real functionality.
public abstract class PlaceholderModule : INotchModule
{
    public abstract ModuleDescriptor Descriptor { get; }
    public abstract object ViewModel { get; }
    public ValueTask ActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
    public ValueTask DeactivateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
