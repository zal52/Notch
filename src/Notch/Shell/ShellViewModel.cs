using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notch.Modules;
using Notch.Modules.Contracts;

namespace Notch.Shell;

public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ModuleCatalog _catalog;
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private CancellationTokenSource? _navigation;
    private INotchModule? _active;
    private bool _activeReady;
    private ShellState _state = ShellState.Collapsed;
    private bool _isNavigating;
    private bool _isTopmost = true;
    private bool _animationsEnabled = true;
    private bool _clipboardEnabled = true;
    private string? _statusMessage;
    private bool _disposed;
    private string? _error;

    public ShellViewModel(ModuleCatalog catalog)
    {
        _catalog = catalog;
        ExpandCommand = new AsyncRelayCommand(() => NavigateAsync(ShellState.Expanded));
        CollapseCommand = new AsyncRelayCommand(() => NavigateAsync(ShellState.Collapsed));
        BackCommand = new AsyncRelayCommand(() => NavigateAsync(State.Mode == NotchMode.ModuleOpen
            ? ShellState.Expanded : ShellState.Collapsed));
        OpenModuleCommand = new AsyncRelayCommand<ModuleDescriptor>(descriptor => descriptor is null
            ? Task.CompletedTask : NavigateAsync(ShellState.OpenModule(descriptor.Id)),
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
    }

    public IReadOnlyList<ModuleDescriptor> Modules => _catalog.Descriptors;
    public ShellState State => _state;
    public bool IsCollapsed => State.Mode == NotchMode.Collapsed;
    public bool IsExpanded => State.Mode == NotchMode.Expanded;
    public bool IsModuleOpen => State.Mode == NotchMode.ModuleOpen;
    public object? ActiveContent => IsModuleOpen ? _active?.ViewModel : null;
    public ModuleDescriptor? ActiveDescriptor => IsModuleOpen ? _active?.Descriptor : null;
    public bool IsNavigating { get => _isNavigating; private set => SetProperty(ref _isNavigating, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public bool IsTopmost { get => _isTopmost; set => SetProperty(ref _isTopmost, value); }
    public bool AnimationsEnabled { get => _animationsEnabled; set => SetProperty(ref _animationsEnabled, value); }
    public bool ClipboardEnabled { get => _clipboardEnabled; set => SetProperty(ref _clipboardEnabled, value); }
    public string? StatusMessage
    {
        get => _statusMessage;
        set { if (SetProperty(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage)); }
    }
    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    public IAsyncRelayCommand ExpandCommand { get; }
    public IAsyncRelayCommand CollapseCommand { get; }
    public IAsyncRelayCommand BackCommand { get; }
    public IAsyncRelayCommand<ModuleDescriptor> OpenModuleCommand { get; }

    // Called on the UI dispatcher. The gate serializes module lifecycle calls;
    // cancellation ensures that a late activation never overwrites newer navigation.
    public async Task NavigateAsync(ShellState target)
    {
        if (_disposed) return;
        _navigation?.Cancel();
        using var request = new CancellationTokenSource();
        _navigation = request;
        var acquired = false;
        IsNavigating = true;
        Error = null;
        try
        {
            await _navigationGate.WaitAsync(request.Token);
            acquired = true;
            var next = target.ModuleId is null ? null : _catalog.Resolve(target.ModuleId);
            if (!ReferenceEquals(_active, next) || (next is not null && !_activeReady))
            {
                await DeactivateCurrentAsync();
                request.Token.ThrowIfCancellationRequested();
                _active = next;
                if (next is not null) await next.ActivateAsync(request.Token);
                request.Token.ThrowIfCancellationRequested();
                _activeReady = next is not null;
            }
            request.Token.ThrowIfCancellationRequested();
            SetState(target);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // The newer request owns the next visible state and lifecycle cleanup.
        }
        catch (Exception ex)
        {
            Trace.TraceError("Module navigation failed: {0}", ex.GetType().Name);
            await DeactivateCurrentAsync();
            if (ReferenceEquals(_navigation, request))
            {
                SetState(ShellState.Expanded);
                Error = "Не удалось открыть модуль. Попробуйте ещё раз.";
            }
        }
        finally
        {
            if (acquired) _navigationGate.Release();
            if (ReferenceEquals(_navigation, request))
            {
                _navigation = null;
                IsNavigating = false;
            }
        }
    }

    private async ValueTask DeactivateCurrentAsync()
    {
        var previous = _active;
        _active = null;
        _activeReady = false;
        if (previous is null) return;
        try { await previous.DeactivateAsync(CancellationToken.None); }
        catch (Exception ex) { Trace.TraceError("Module cleanup failed: {0}", ex.GetType().Name); }
    }

    private void SetState(ShellState state)
    {
        _state = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsCollapsed));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsModuleOpen));
        OnPropertyChanged(nameof(ActiveContent));
        OnPropertyChanged(nameof(ActiveDescriptor));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _navigation?.Cancel();
        await _navigationGate.WaitAsync();
        try { await DeactivateCurrentAsync(); }
        finally { _navigationGate.Release(); }
    }
}
