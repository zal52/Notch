using Notch.Modules;
using Notch.Modules.Contracts;
using Notch.Shell;
using Xunit;

namespace Notch.Tests;

public sealed class ShellTests
{
    private sealed class TestModule(string id) : INotchModule
    {
        public ModuleDescriptor Descriptor { get; } = new(id, id, "", "", 0);
        public object ViewModel { get; } = new();
        public int Activations { get; private set; }
        public int Deactivations { get; private set; }
        public Func<CancellationToken, Task>? OnActivate { get; set; }
        public async ValueTask ActivateAsync(CancellationToken cancellationToken)
        {
            Activations++;
            if (OnActivate is not null) await OnActivate(cancellationToken);
        }
        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            Deactivations++;
            return ValueTask.CompletedTask;
        }
    }

    private static ShellViewModel Create(params TestModule[] modules) => new(new ModuleCatalog(
        modules.Select(m => new ModuleRegistration(m.Descriptor, () => m))));

    [Fact]
    public async Task BackNavigatesFromModuleToCatalogThenCollapsed()
    {
        var module = new TestModule("one");
        await using var shell = Create(module);
        Assert.True(shell.IsCollapsed);
        await shell.ExpandCommand.ExecuteAsync(null);
        Assert.True(shell.IsExpanded);
        await shell.OpenModuleCommand.ExecuteAsync(module.Descriptor);
        Assert.True(shell.IsModuleOpen);
        Assert.Same(module.ViewModel, shell.ActiveContent);
        await shell.BackCommand.ExecuteAsync(null);
        Assert.True(shell.IsExpanded);
        Assert.Null(shell.ActiveContent);
        Assert.Equal(1, module.Deactivations);
        await shell.BackCommand.ExecuteAsync(null);
        Assert.True(shell.IsCollapsed);
    }

    [Fact]
    public async Task ReopeningPreservesViewModelAndDoesNotReactivateSameModule()
    {
        var module = new TestModule("one");
        await using var shell = Create(module);
        await shell.NavigateAsync(ShellState.OpenModule("one"));
        var original = shell.ActiveContent;
        await shell.NavigateAsync(ShellState.OpenModule("one"));
        Assert.Equal(1, module.Activations);
        await shell.NavigateAsync(ShellState.Collapsed);
        await shell.NavigateAsync(ShellState.OpenModule("one"));
        Assert.Same(original, shell.ActiveContent);
        Assert.Equal(2, module.Activations);
    }

    [Fact]
    public async Task CollapseCancelsPendingActivationAndCleansUp()
    {
        var module = new TestModule("slow")
        {
            OnActivate = token => Task.Delay(Timeout.Infinite, token)
        };
        await using var shell = Create(module);
        var opening = shell.NavigateAsync(ShellState.OpenModule("slow"));
        var collapsing = shell.NavigateAsync(ShellState.Collapsed);
        await Task.WhenAll(opening, collapsing).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(shell.IsCollapsed);
        Assert.Null(shell.ActiveContent);
        Assert.Null(shell.Error);
        Assert.False(shell.IsNavigating);
        Assert.Equal(1, module.Deactivations);
    }

    [Fact]
    public async Task LateCompletionCannotOverwriteNewerNavigation()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new TestModule("slow") { OnActivate = _ => release.Task };
        var next = new TestModule("next");
        await using var shell = Create(slow, next);
        var opening = shell.NavigateAsync(ShellState.OpenModule("slow"));
        var switching = shell.NavigateAsync(ShellState.OpenModule("next"));
        release.SetResult();
        await Task.WhenAll(opening, switching).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("next", shell.State.ModuleId);
        Assert.Same(next.ViewModel, shell.ActiveContent);
        Assert.Equal(1, slow.Deactivations);
    }

    [Fact]
    public async Task CancelledActivationOfSameModuleIsRetried()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new TestModule("one") { OnActivate = _ => release.Task };
        await using var shell = Create(module);
        var opening = shell.NavigateAsync(ShellState.OpenModule("one"));
        var reopening = shell.NavigateAsync(ShellState.OpenModule("one"));
        release.SetResult();
        await Task.WhenAll(opening, reopening).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(shell.IsModuleOpen);
        Assert.Equal(2, module.Activations);
        Assert.Equal(1, module.Deactivations);
    }

    [Fact]
    public async Task ActivationFailureReturnsToCatalogAndAllowsRecovery()
    {
        var broken = new TestModule("broken") { OnActivate = _ => throw new InvalidOperationException() };
        var healthy = new TestModule("healthy");
        await using var shell = Create(broken, healthy);
        await shell.NavigateAsync(ShellState.OpenModule("broken"));
        Assert.True(shell.IsExpanded);
        Assert.NotNull(shell.Error);
        Assert.Null(shell.ActiveContent);
        Assert.Equal(1, broken.Deactivations);
        await shell.NavigateAsync(ShellState.OpenModule("healthy"));
        Assert.Equal("healthy", shell.State.ModuleId);
        Assert.Null(shell.Error);
    }

    [Fact]
    public async Task UnknownModuleIsContainedAsNavigationError()
    {
        await using var shell = Create();
        await shell.NavigateAsync(ShellState.OpenModule("missing"));
        Assert.True(shell.IsExpanded);
        Assert.NotNull(shell.Error);
    }

    [Fact]
    public async Task DisposalCancelsActivationAndDeactivatesModule()
    {
        var module = new TestModule("slow") { OnActivate = token => Task.Delay(Timeout.Infinite, token) };
        var shell = Create(module);
        var opening = shell.NavigateAsync(ShellState.OpenModule("slow"));
        await shell.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await opening;
        Assert.Equal(1, module.Deactivations);
        await shell.NavigateAsync(ShellState.Expanded);
        Assert.True(shell.IsCollapsed);
    }

    [Fact]
    public void CatalogIsLazyAndRejectsDuplicateIds()
    {
        var count = 0;
        var module = new TestModule("one");
        var registration = new ModuleRegistration(module.Descriptor, () => { count++; return module; });
        var catalog = new ModuleCatalog([registration]);
        Assert.Equal(0, count);
        Assert.Single(catalog.Descriptors);
        Assert.Same(catalog.Resolve("one"), catalog.Resolve("one"));
        Assert.Equal(1, count);
        Assert.Throws<ArgumentException>(() => new ModuleCatalog([registration, registration]));
    }
}
