using System;
using System.Windows;
using System.Windows.Media.Animation;
using Notch.Infrastructure.Windows.Windowing;
using Notch.Modules.Contracts;
using Notch.Shell;

namespace Notch.Presentation.Behaviors;

public sealed class NotchAnimator(Window window, WindowPlacement placement)
{
    public void TransitionTo(ShellState state, ModuleDescriptor? descriptor, bool animate = true)
    {
        var desired = state.Mode switch
        {
            NotchMode.Collapsed => new Size(112, 28),
            NotchMode.Expanded => new Size(320, 136),
            _ => new Size(descriptor?.PreferredSize == ModuleSize.Wide ? 440 : 360, 260)
        };
        var target = placement.Constrain(desired);
        // Read the current animated values first: interrupted transitions continue
        // from the on-screen size rather than jumping back to their old base value.
        var fromWidth = window.Width;
        var fromHeight = window.Height;
        window.BeginAnimation(FrameworkElement.WidthProperty, null);
        window.BeginAnimation(FrameworkElement.HeightProperty, null);
        window.Width = target.Width;
        window.Height = target.Height;
        if (animate && SystemParameters.ClientAreaAnimation)
        {
            Animate(FrameworkElement.WidthProperty, fromWidth, target.Width);
            Animate(FrameworkElement.HeightProperty, fromHeight, target.Height);
        }
        placement.Center();
    }

    private void Animate(DependencyProperty property, double from, double to)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        window.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
