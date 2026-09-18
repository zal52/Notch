using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Notch.Infrastructure.Composition;
using Notch.Shell;
using Notch.Modules.Clipboard;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;
using Notch.Infrastructure.Windows;
using Notch.Infrastructure.Windows.Capture;
using Notch.Services.Screenshots;
using Notch.Modules.Screenshots;
using System.Windows.Controls.Primitives;
using Xunit;

namespace Notch.Tests;

public sealed class WpfSmokeTests
{
    [Fact]
    public async Task RealWindowRendersEveryStateWithModuleTemplates()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                Application? app = null;
                ServiceProvider? services = null;
                NotchWindow? window = null;
                Exception? failure = null;
                try
                {
                    app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Notch;component/Presentation/Themes/NotchTheme.xaml", UriKind.Relative) });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Notch;component/Modules/ModuleTemplates.xaml", UriKind.Relative) });
                    var fakeClipboard = new FakeTextClipboardPlatform();
                    var fakeImages = new FakeImageClipboard();
                    var fakeCapture = new FakeScreenshotBackend { Frame = MakeSampleImage() };
                    var fakeFiles = new FakeScreenshotFiles();
                    var fakePicker = new FakeSaveFilePicker();
                    var registrations = new ServiceCollection().AddNotch();
                    registrations.AddSingleton<IClipboardService>(_ => new ClipboardService(fakeClipboard, new ClipboardHistory(), fakeImages));
                    registrations.AddSingleton<IScreenshotCaptureBackend>(fakeCapture);
                    registrations.AddSingleton<IScreenshotFiles>(fakeFiles);
                    registrations.AddSingleton<ISaveFilePicker>(fakePicker);
                    services = registrations.BuildServiceProvider();
                    var clipboard = services.GetRequiredService<IClipboardService>();
                    await clipboard.StartAsync(default);
                    window = services.GetRequiredService<NotchWindow>();
                    var shell = services.GetRequiredService<ShellViewModel>();
                    window.Show();
                    await SettleAsync(window);
                    Assert.True(window.IsVisible);
                    Assert.False(window.ShowInTaskbar);
                    Assert.True(window.Topmost);
                    Assert.Equal(WindowStyle.None, window.WindowStyle);
                    Assert.True(window.AllowsTransparency);
                    Assert.Equal(112, window.ActualWidth);
                    Assert.Equal(28, window.ActualHeight);
                    SaveRender(window, "01-collapsed");

                    await shell.NavigateAsync(ShellState.Expanded);
                    await SettleAsync(window);
                    Assert.Equal(320, window.ActualWidth);
                    Assert.Equal(136, window.ActualHeight);
                    SaveRender(window, "02-expanded");

                    foreach (var descriptor in shell.Modules)
                    {
                        await shell.NavigateAsync(ShellState.OpenModule(descriptor.Id));
                        await SettleAsync(window);
                        Assert.Equal(360, window.ActualWidth);
                        Assert.Equal(260, window.ActualHeight);
                        Assert.True(FindModuleView(window, shell.ActiveContent!), $"No view rendered for {descriptor.Id}.");
                        SaveRender(window, "03-" + descriptor.Id);
                        if (descriptor.Id == "translator" && Environment.GetEnvironmentVariable("NOTCH_LIVE_TRANSLATION") == "1")
                        {
                            var translator = services.GetRequiredService<Notch.Modules.Translator.TranslatorViewModel>();
                            translator.Input = "Hello world";
                            await translator.TranslateCommand.ExecuteAsync(null);
                            Assert.Contains("мир", translator.Result.ToLowerInvariant());
                            await translator.CopyCommand.ExecuteAsync(null);
                            Assert.Equal(translator.Result, fakeClipboard.CurrentText);
                            await SettleAsync(window);
                            SaveRender(window, "09-live-translation");
                            clipboard.ClearHistory();
                        }
                        if (descriptor.Id == "clipboard")
                        {
                            var previousWrites = fakeClipboard.Writes;
                            fakeClipboard.Emit("Идея: меньше деталей, больше воздуха.");
                            await ClipboardTests.UntilAsync(() => clipboard.History.Count == 1);
                            fakeClipboard.Emit("https://example.com/design");
                            await ClipboardTests.UntilAsync(() => clipboard.History.Count == 2);
                            fakeClipboard.Emit("Обсудить макет завтра в 10:30");
                            await ClipboardTests.UntilAsync(() => clipboard.History.Count == 3);
                            await SettleAsync(window);
                            SaveRender(window, "04-clipboard-history");
                            var clipboardVm = services.GetRequiredService<ClipboardViewModel>();
                            var selected = clipboardVm.Items[1];
                            await clipboardVm.CopyCommand.ExecuteAsync(selected);
                            Assert.Equal(((ClipboardContent.Text)selected.Entry.Content).Value, fakeClipboard.CurrentText);
                            for (var i = 0; i < 6; i++)
                            {
                                var count = clipboard.History.Count;
                                fakeClipboard.Emit($"Дополнительная запись {i + 1}");
                                await ClipboardTests.UntilAsync(() => clipboard.History.Count == count + 1);
                            }
                            await SettleAsync(window);
                            var view = Descendants<ClipboardView>(window).Single();
                            var scroll = Descendants<ScrollViewer>(view).Single();
                            var bar = Descendants<ScrollBar>(scroll).Single();
                            Assert.True(scroll.ScrollableHeight > 0);
                            Assert.Equal(8, bar.ActualWidth);
                            SaveRender(window, "05-clipboard-scrollbar");
                            scroll.PageDown();
                            await SettleAsync(window);
                            Assert.True(scroll.VerticalOffset > 0);
                            var offset = scroll.VerticalOffset;
                            ScrollBar.PageUpCommand.Execute(null, bar);
                            await SettleAsync(window);
                            Assert.True(scroll.VerticalOffset < offset);
                            clipboardVm.ClearCommand.Execute(null);
                            Assert.True(clipboardVm.IsEmpty);
                            Assert.Equal(previousWrites + 1, fakeClipboard.Writes);
                        }
                        if (descriptor.Id == "screenshots")
                        {
                            var screenshots = services.GetRequiredService<ScreenshotViewModel>();
                            fakeCapture.BeforeCapture = _ =>
                            {
                                Assert.False(window.IsVisible);
                                Assert.True(window.IsCaptureSuppressed);
                                return Task.CompletedTask;
                            };
                            for (var i = 0; i < 4; i++) await screenshots.CaptureCommand.ExecuteAsync(null);
                            Assert.Equal(3, screenshots.Items.Count);
                            Assert.True(window.IsVisible);
                            Assert.False(window.IsCaptureSuppressed);
                            await SettleAsync(window);
                            SaveRender(window, "06-screenshot-history");
                            var selected = screenshots.Items[0];
                            await screenshots.CopyCommand.ExecuteAsync(selected);
                            Assert.NotNull(fakeImages.LastImage);
                            await screenshots.SaveCommand.ExecuteAsync(selected);
                            Assert.Equal("Сохранение отменено", screenshots.Status);
                            Assert.Null(fakeFiles.Saved);
                            fakePicker.Path = "selected.png";
                            await screenshots.SaveCommand.ExecuteAsync(selected);
                            Assert.Equal("selected.png", fakeFiles.Destination);
                            await screenshots.OpenCommand.ExecuteAsync(selected);
                            Assert.Equal(selected.Entry.Id, fakeFiles.Opened);
                            await screenshots.DeleteCommand.ExecuteAsync(selected);
                            Assert.Equal(2, screenshots.Items.Count);

                            if (Environment.GetEnvironmentVariable("NOTCH_LIVE_CAPTURE") == "1")
                            {
                                CapturedScreenshot live;
                                using (await services.GetRequiredService<ICaptureVisibility>().HideAsync(default))
                                    live = await new GdiScreenshotBackend().CaptureAsync(new(), default);
                                Assert.True(live.Width > 0 && live.Height > 0);
                                using var png = new MemoryStream(live.Png.ToArray());
                                var decoded = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                                Assert.Equal(live.Width, decoded.PixelWidth);
                                Assert.Equal(live.Height, decoded.PixelHeight);
                                Assert.True(window.IsVisible);
                                // Real desktop pixels stay in memory and are never written to artifacts.
                            }
                        }
                    }

                    var bridge = services.GetRequiredService<ClipboardScreenshotBridge>();
                    bridge.Start();
                    var screenshotService = services.GetRequiredService<IScreenshotService>();
                    foreach (var entry in screenshotService.Recent.ToArray())
                        await screenshotService.DeleteAsync(entry.Id, default);
                    clipboard.ClearHistory();
                    var sample = MakeSampleImage();
                    fakeClipboard.EmitImage(new ClipboardContent.Image(sample.Png, sample.Width, sample.Height)
                    { ThumbnailPng = sample.ThumbnailPng });
                    await ClipboardTests.UntilAsync(() => screenshotService.Recent.Count == 1);
                    Assert.True(screenshotService.Recent[0].IsFromClipboard);
                    await shell.NavigateAsync(ShellState.OpenModule("clipboard"));
                    await SettleAsync(window);
                    Assert.NotNull(services.GetRequiredService<ClipboardViewModel>().Items[0].Thumbnail);
                    SaveRender(window, "07-image-clipboard");
                    await shell.NavigateAsync(ShellState.OpenModule("screenshots"));
                    await SettleAsync(window);
                    SaveRender(window, "08-auto-screenshots");

                    // Interrupt a real WPF resize before it finishes.
                    await shell.NavigateAsync(ShellState.Expanded);
                    await Task.Delay(35);
                    await shell.NavigateAsync(ShellState.Collapsed);
                    await SettleAsync(window);
                    Assert.Equal(112, window.ActualWidth);
                    Assert.Equal(28, window.ActualHeight);
                    shell.IsTopmost = false;
                    await SettleAsync(window);
                    Assert.False(window.Topmost);
                    shell.AnimationsEnabled = false;
                    await window.ToggleExpandedAsync();
                    window.UpdateLayout();
                    Assert.True(shell.IsExpanded);
                    Assert.Equal(320, window.Width);
                    await window.ToggleExpandedAsync();
                    Assert.True(shell.IsCollapsed);

                    // Native registration smoke checks; no user clipboard reads/writes
                    // and no synthetic key input are performed by this test.
                    var nativeClipboard = services.GetRequiredService<IClipboardPlatform>();
                    nativeClipboard.Start();
                    nativeClipboard.Stop();
                    var hotkeys = services.GetRequiredService<IHotkeyService>();
                    var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x87);
                    using (hotkeys.Register(gesture, () => { }))
                        Assert.Throws<System.ComponentModel.Win32Exception>(() => hotkeys.Register(gesture, () => { }));
                    using (hotkeys.Register(gesture, () => { })) { }

                }
                catch (Exception exception) { failure = exception; }
                finally
                {
                    try
                    {
                        if (window is not null) { window.AllowClose = true; window.Close(); }
                        if (services is not null) await services.DisposeAsync();
                        app?.Shutdown();
                    }
                    catch (Exception exception) { failure ??= exception; }
                    dispatcher.InvokeShutdown();
                    if (failure is not null) finished.TrySetException(failure);
                    else finished.TrySetResult();
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Notch WPF integration test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(40));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "WPF test thread did not shut down.");
    }

private static async Task SettleAsync(Window window)
    {
        await Task.Delay(320);
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
    }

    private static bool FindModuleView(DependencyObject parent, object viewModel)
    {
        if (parent is UserControl view && ReferenceEquals(view.DataContext, viewModel)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindModuleView(VisualTreeHelper.GetChild(parent, i), viewModel)) return true;
        return false;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static CapturedScreenshot MakeSampleImage()
    {
        const int width = 160, height = 90;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(100 + y);
                pixels[i + 1] = (byte)(35 + x / 2);
                pixels[i + 2] = (byte)(35 + y / 2);
                pixels[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();
        return new CapturedScreenshot(width, height, png, png);
    }

    private static void SaveRender(Window window, string name)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("NOTCH_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;
        Directory.CreateDirectory(outputDirectory);
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(outputDirectory, name + ".png"));
        encoder.Save(stream);
    }
}






