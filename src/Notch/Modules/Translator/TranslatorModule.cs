using Notch.Modules.Contracts;
using System.Threading;
using System.Threading.Tasks;
namespace Notch.Modules.Translator;
public sealed class TranslatorModule(TranslatorViewModel viewModel) : INotchModule
{
    public static ModuleDescriptor Metadata { get; } = new("translator", "Переводчик", "Слова без границ", "\uE8C1", 0);
    public ModuleDescriptor Descriptor => Metadata;
    public object ViewModel => viewModel;
    public ValueTask ActivateAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    public ValueTask DeactivateAsync(CancellationToken cancellationToken) { viewModel.Cancel(); return ValueTask.CompletedTask; }
}
