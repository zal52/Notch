using System;
using System.Collections.Generic;
using System.Linq;
using Notch.Modules.Contracts;

namespace Notch.Modules;

public sealed record ModuleRegistration(ModuleDescriptor Descriptor, Func<INotchModule> Factory);

public sealed class ModuleCatalog
{
    private readonly Dictionary<string, Lazy<INotchModule>> _modules = new(StringComparer.Ordinal);
    public IReadOnlyList<ModuleDescriptor> Descriptors { get; }

    public ModuleCatalog(IEnumerable<ModuleRegistration> registrations)
    {
        var entries = registrations.OrderBy(r => r.Descriptor.Order).ToArray();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Descriptor.Id))
                throw new ArgumentException("Module IDs cannot be empty.");
            if (!_modules.TryAdd(entry.Descriptor.Id, new Lazy<INotchModule>(() =>
            {
                var module = entry.Factory();
                if (module.Descriptor != entry.Descriptor)
                    throw new InvalidOperationException("Module descriptor differs from registration.");
                return module;
            })))
                throw new ArgumentException($"Duplicate module ID: {entry.Descriptor.Id}");
        }
        Descriptors = Array.AsReadOnly(entries.Select(e => e.Descriptor).ToArray());
    }

    public INotchModule Resolve(string id) => _modules.TryGetValue(id, out var module)
        ? module.Value : throw new ArgumentException($"Unknown module: {id}", nameof(id));
}
