using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using Notch.Infrastructure.Persistence;
using Notch.Infrastructure.Windows;
using Notch.Infrastructure.Windows.Clipboard;
using Notch.Infrastructure.Windows.Hotkeys;
using Notch.Modules;
using Notch.Modules.Clipboard;
using Notch.Modules.Contracts;
using Notch.Modules.Screenshots;
using Notch.Modules.Translator;
using Notch.Shell;
using Notch.Services;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;
using Notch.Services.Screenshots;
using Notch.Infrastructure.Windows.Capture;
using Notch.Presentation.Behaviors;
using System.Diagnostics;

namespace Notch.Infrastructure.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddNotch(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService>(_ => new JsonSettingsService(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notch", "settings.json")));
        services.AddSingleton<NativeMessageWindow>();
        services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
        services.AddSingleton<WpfClipboardImageCodec>();
        services.AddSingleton<IClipboardImageCodec>(provider => provider.GetRequiredService<WpfClipboardImageCodec>());
        services.AddSingleton<IClipboardPlatform, WindowsClipboardPlatform>();
        services.AddSingleton<IImageClipboardPlatform, WindowsImageClipboardPlatform>();
        services.AddSingleton<ClipboardHistory>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ClipboardViewModel>();
        services.AddSingleton<IScreenshotCaptureBackend, GdiScreenshotBackend>();
        services.AddSingleton<ICaptureVisibility>(provider => new NotchCaptureVisibility(provider.GetRequiredService<NotchWindow>));
        services.AddSingleton<IScreenshotFiles>(_ => new ScreenshotFiles(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notch", "Previews", Guid.NewGuid().ToString("N")),
            path => { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }));
        services.AddSingleton<ISaveFilePicker>(_ => new SaveFilePicker(() => System.Windows.Application.Current.MainWindow));
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<ClipboardScreenshotBridge>();
        services.AddSingleton<ScreenshotViewModel>();
        services.AddSingleton(_ => new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) });
        services.AddSingleton<Notch.Services.Translation.LocalBackendProcess>();
        services.AddSingleton<ITranslationService>(provider => new Notch.Services.Translation.BackendTranslationService(
            provider.GetRequiredService<System.Net.Http.HttpClient>(),
            Environment.GetEnvironmentVariable("NOTCH_BACKEND_URL") ?? provider.GetRequiredService<Notch.Services.Translation.LocalBackendProcess>().Endpoint,
            Environment.GetEnvironmentVariable("NOTCH_BACKEND_URL") is null ? provider.GetRequiredService<Notch.Services.Translation.LocalBackendProcess>().Token : null));
        services.AddSingleton<TranslatorViewModel>();
        services.AddSingleton<ApplicationCoordinator>();
        services.AddModule<TranslatorModule>(TranslatorModule.Metadata);
        services.AddModule<ClipboardModule>(ClipboardModule.Metadata);
        services.AddModule<ScreenshotModule>(ScreenshotModule.Metadata);
        services.AddSingleton<ModuleCatalog>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<NotchWindow>();
        return services;
    }

    public static IServiceCollection AddModule<T>(this IServiceCollection services, ModuleDescriptor descriptor)
        where T : class, INotchModule
    {
        services.AddSingleton<T>();
        services.AddSingleton(provider => new ModuleRegistration(descriptor, provider.GetRequiredService<T>));
        return services;
    }
}




