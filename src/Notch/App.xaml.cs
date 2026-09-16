using System;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Notch.Infrastructure.Composition;
using Notch.Infrastructure.Windows;
using Notch.Shell;
using Notch.Services;

namespace Notch;

public partial class App : Application
{
    private ServiceProvider? _services;
    private TrayIcon? _tray;
    private bool _exiting;
    private Mutex? _singleInstance;
    private bool _ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\Notch.Desktop", out _ownsMutex);
        if (!_ownsMutex) { Shutdown(); return; }
        try
        {
            _services = new ServiceCollection().AddNotch().BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            var window = _services.GetRequiredService<NotchWindow>();
            MainWindow = window;
            window.ExitRequested += (_, _) => ExitApplication();
            var coordinator = _services.GetRequiredService<ApplicationCoordinator>();
            coordinator.ToggleRequested += async (_, _) => await window.ToggleExpandedAsync();
            await coordinator.StartAsync(CancellationToken.None);
            _tray = new TrayIcon(window.ShowNotch, window.ToggleVisibility, ExitApplication);
            window.Show();
            var shell = _services.GetRequiredService<ShellViewModel>();
            if (shell.StatusMessage is { } message) _tray.ShowNotice(message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Notch startup failed: {0}", ex.GetType().Name);
            MessageBox.Show("Не удалось запустить Notch. Попробуйте запустить приложение повторно.", "Notch",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ExitApplication();
        }
    }

    private async void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _tray?.Dispose();
        if (MainWindow is NotchWindow window) window.AllowClose = true;
        try { if (_services is not null) await _services.DisposeAsync(); }
        finally { Shutdown(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
