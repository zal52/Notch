using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Notch.Infrastructure.Windows.Windowing;
using Notch.Presentation.Behaviors;

namespace Notch.Shell;

public partial class NotchWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly WindowPlacement _placement;
    private readonly NotchAnimator _animator;
    public bool AllowClose { get; set; }
    public bool IsCaptureSuppressed { get; internal set; }
    public event EventHandler? ExitRequested;

    public NotchWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _placement = new WindowPlacement(this);
        _animator = new NotchAnimator(this, _placement);
        _viewModel.PropertyChanged += OnViewModelChanged;
        _placement.DisplayChanged += OnDisplayChanged;
        Loaded += (_, _) => _animator.TransitionTo(_viewModel.State, _viewModel.ActiveDescriptor, false);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _placement.DisplayChanged -= OnDisplayChanged;
            _placement.Dispose();
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.AnimationsEnabled) && !_viewModel.AnimationsEnabled)
        {
            _animator.TransitionTo(_viewModel.State, _viewModel.ActiveDescriptor, false);
            ExpandedContent.BeginAnimation(OpacityProperty, null);
            ModuleContent.BeginAnimation(OpacityProperty, null);
        }
        if (e.PropertyName != nameof(ShellViewModel.State)) return;
        _animator.TransitionTo(_viewModel.State, _viewModel.ActiveDescriptor, _viewModel.AnimationsEnabled);
        if (!_viewModel.IsCollapsed && _viewModel.AnimationsEnabled && SystemParameters.ClientAreaAnimation)
        {
            var content = _viewModel.IsExpanded ? ExpandedContent : ModuleContent;
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                BeginTime = TimeSpan.FromMilliseconds(60), FillBehavior = FillBehavior.Stop
            });
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsActive) return;
            Keyboard.Focus(_viewModel.IsCollapsed ? ExpandButton : _viewModel.IsExpanded ? CollapseButton : BackButton);
        }));
    }

    private void OnDisplayChanged(object? sender, EventArgs e) =>
        _animator.TransitionTo(_viewModel.State, _viewModel.ActiveDescriptor, false);

    public async void ShowNotch()
    {
        if (IsCaptureSuppressed) return;
        Show();
        Activate();
        await _viewModel.NavigateAsync(ShellState.Expanded);
    }

    public async Task ToggleExpandedAsync()
    {
        if (IsCaptureSuppressed) return;
        if (IsVisible && !_viewModel.IsCollapsed)
            await _viewModel.NavigateAsync(ShellState.Collapsed);
        else
        {
            Show();
            Activate();
            await _viewModel.NavigateAsync(ShellState.Expanded);
        }
    }

    public async void ToggleVisibility()
    {
        if (IsCaptureSuppressed) return;
        if (IsVisible)
        {
            await _viewModel.NavigateAsync(ShellState.Collapsed);
            Hide();
        }
        else ShowNotch();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(() => ExitRequested?.Invoke(this, EventArgs.Empty)));
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
