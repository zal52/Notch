using System.Threading;
using System.Threading.Tasks;

namespace Notch.Modules.Contracts;

public interface INotchModule
{
    ModuleDescriptor Descriptor { get; }
    object ViewModel { get; }
    ValueTask ActivateAsync(CancellationToken cancellationToken);
    ValueTask DeactivateAsync(CancellationToken cancellationToken);
}

public enum ModuleSize { Standard, Wide }

public sealed record ModuleDescriptor(
    string Id, string Title, string Description, string Glyph, int Order,
    ModuleSize PreferredSize = ModuleSize.Standard);
