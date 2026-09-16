# Notch

Windows desktop shell inspired by Dynamic Island. C# / .NET 10 / WPF / MVVM.

## Current milestone

Roadmap stages 1–6 and the screenshot stage are implemented: the compact shell, module catalog, persistent preferences, global hotkey, text clipboard history, and screenshots. Translator remains a placeholder; general image clipboard history is still deferred.

- Black rounded panel at the top center of the primary display.
- Monochrome, reduced chrome: 112×28 DIPs collapsed, 320×136 expanded, 360×260 for standard modules (previously 152×36, 460×244, and 520×350).
- Collapsed → Expanded → ModuleOpen navigation, with Back and Escape.
- Interruptible 220 ms resize transitions, content fade, and OS reduced-animation support.
- PerMonitorV2 manifest, physical-pixel placement, and display/DPI change handling.
- Persistent Topmost, animation, and clipboard-monitoring preferences; no taskbar or Alt+Tab entry.
- Lazy module activation and retained module ViewModels.
- Serialized lifecycle calls, cancellation, and contained module errors.
- Service contracts for translation, clipboard, screenshots, hotkeys, and settings.
- Ctrl+Alt+N toggles the shell, including when hidden through the tray.
- Text and image clipboard history runs independently of the current module and uses Windows notifications instead of polling.
- Click a history entry to copy it again; clear the history without changing the current Windows clipboard.
- Thin dark scrollbars with no arrow buttons or bright track; wheel, thumb dragging and page scrolling remain available.
- Capture the primary screen, retain the three newest screenshots, and copy/open/save/delete each one inside the notch.
- A per-session mutex prevents duplicate application instances.

**Still deferred:** startup registration and an installer. Translator now has source/target selection, text input, swap, Ctrl+Enter, selectable output and copy. MyMemory supplies online translation without an API key. Text is sent only when Translate or Ctrl+Enter is used; source language is selected explicitly. Requests are limited to 500 UTF-8 bytes (Cyrillic uses more bytes than ASCII), and anonymous service usage is limited to 5,000 characters/day. The app reports quota and network errors without showing error payloads as translations. Provider documentation: https://mymemory.translated.net/doc/spec.php and https://mymemory.translated.net/doc/usagelimits.php.

## Run

Windows x64 and the .NET 10 SDK are required to build. Double-click `Start-Notch.cmd`, or run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

The launcher prefers a project-local SDK in `.tools/dotnet`, otherwise it uses `dotnet` from PATH. The initial development checkout has a local SDK; it is deliberately excluded from Git. Restore requires access to NuGet. For an existing build, use `scripts/run.ps1 -NoBuild`.

The binary is framework-dependent. Opening `Notch.exe` directly requires the .NET 10 Desktop Runtime to be discoverable by the app host; the script sets `DOTNET_ROOT` for the local SDK. A distributable self-contained build is a later milestone.

Click the small notch or press **Ctrl+Alt+N** to open the module catalog. The same hotkey collapses an open panel. Escape returns to the catalog, then collapses the panel. Right-click the panel to toggle Topmost, animation, or clipboard monitoring, or to exit. The system tray menu can reopen or hide Notch; double-clicking its icon opens the catalog. Alt+F4 exits cleanly. If another application owns the hotkey, Notch continues running and reports the conflict; use the tray to open it.

## Preferences and clipboard

Preferences are saved automatically to `%LOCALAPPDATA%/Notch/settings.json`. Writes are serialized and use a temporary file followed by replacement. Shutdown waits for pending saves. Malformed JSON is preserved as a `.invalid-*` backup; a newer schema is never overwritten. The hotkey can be changed by editing `ToggleHotkey` while Notch is closed, then relaunching (default `Modifiers: "Alt, Control"`, `VirtualKey: 78`, the N key).

Clipboard history is **memory-only**, with up to 50 mixed text/image entries, 65,536 UTF-16 code units per entry, and a 524,288-code-unit total budget. Entries exceeding the limit are skipped, not truncated. It preserves original whitespace and Unicode; previews flatten line breaks. Only consecutive duplicate entries are suppressed, so copying A → B → A keeps all three actions.

Monitoring starts with new clipboard notifications; existing clipboard content is not imported on startup or resume. Pausing keeps existing history available. The trash button clears only Notch history. History disappears on exit and is never written to settings or logs. Native exclusion/history opt-out formats are respected when supplied by the originating app. PNG, CF_DIB and CF_DIBV5 images are decoded off the UI thread and shown with thumbnails. Unsupported formats are skipped. Image history has a separate 64 MiB encoded-data budget; each image is limited to 24 megapixels and 32 MiB of PNG data.

Temporary clipboard locks have bounded asynchronous retries; there is no idle polling. Native reading uses a bounded `CF_UNICODETEXT` buffer. Clipboard reads still run on the STA dispatcher; unusually slow delayed-rendering clipboard owners can delay that thread, so a dedicated clipboard STA remains a potential hardening step.

## Screenshots

Open **Скриншоты → Снять экран**. Notch hides, waits for the desktop compositor, captures the primary monitor in physical pixels, and restores its previous visibility even on error/cancellation. Capture and PNG encoding run off the UI thread. Global/tray show requests are ignored while capture is in progress so Notch cannot reappear in its own image.

Images copied through Win+Shift+S or another application automatically appear here, even while the module is closed. Windows does not reliably distinguish screenshots from other copied images, so both are accepted. Pausing clipboard monitoring pauses automatic imports. Copying an image from Notch does not import it again. Pixel-identical imports reuse the existing screenshot entry. The three most recent images stay in memory. The fourth evicts the oldest. Each row provides Copy (full-resolution image), Open (system image viewer), Save (PNG file dialog), and Delete (only the retained capture). Deletion and eviction never delete user-exported PNGs. Actions are serialized in the ViewModel while busy.

Open creates an application-owned temporary PNG under `%LOCALAPPDATA%/Notch/Previews/<session-id>`. These files are removed on eviction/deletion/exit when unlocked; an image viewer holding a file open may leave a preview behind. No capture files are written until Open or Save is selected.

The initial GDI backend includes layered windows but has no region selection, cursor capture, HDR fidelity guarantee or protected-content support. Capture is limited to 24 megapixels and 32 MiB of encoded PNG per image (at most 96 MiB of retained full-size PNG data). The notch remains the same compact 360×260 DIPs.

## Test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

The 56 tests cover shell navigation, clipboard behavior, settings, native registration, screenshot retention/cancellation/failure recovery, image-copy routing, and file-export safety. A WPF integration test renders the real views, checks page scrolling with the custom scrollbar, and runs all screenshot commands against synthetic images while exercising real hide/restore/compositor behavior. It does not read or replace the user's clipboard. Set `NOTCH_ARTIFACTS` to save UI renders. Set `NOTCH_LIVE_CAPTURE=1` to additionally verify the real GDI capture; those desktop pixels stay in memory and are not written to artifacts. Mixed-monitor/HDR behavior and actual external image-viewer/paste compatibility still need broader testing.

## Architecture

`App` is the composition root and owns process lifetime. `ShellViewModel` owns the single `ShellState`. `NotchWindow` handles presentation only. `ModuleCatalog` maps IDs to lazy module factories. WPF data templates resolve ViewModels to views. Native APIs are isolated in `Infrastructure/Windows`; transitions live in `Presentation/Behaviors`.

Module lifecycle calls run on the UI dispatcher. Activation must honor cancellation and leave deactivation safe after partial initialization. `ApplicationCoordinator` owns preferences, hotkey registration and monitoring lifetime. `NativeMessageWindow` supplies an invisible message-only HWND shared by the native services. Clipboard service events run on the owning STA/UI context. The screenshot service coordinates `IScreenshotCaptureBackend`, `ICaptureVisibility` and `IScreenshotFiles`; view code owns presentation and the file picker is an injected OS adapter. Policies can be tested without reading the desktop or clipboard. Translation uses a replaceable ITranslationService implemented by MyMemoryTranslationService. Input or language changes invalidate prior results and cancel pending work; leaving the module cancels the request.

### Add a module

1. Add an `INotchModule` implementation, its own ViewModel and UserControl.
2. Register it with `services.AddModule<YourModule>(YourModule.Metadata)` in `ServiceRegistration`.
3. Add the ViewModel-to-view `DataTemplate` in `ModuleTemplates.xaml`.
4. Register any module services via constructor injection.

No changes to `NotchWindow`, its code-behind, or the navigation state machine are needed. `PlaceholderModule` is only a convenience for the initial inert modules; real modules can implement `INotchModule` directly. Size preferences are shell-level categories, not arbitrary window manipulation.

## Deliberate limits and next stages

- Primary monitor only. Placement already accepts physical monitor bounds, but monitor selection and simultaneous notches are deferred.
- The initial target is x64. Other architectures require an interop and packaging pass.
- Top offset is 8 DIPs; the panel overlays the desktop and does not reserve an appbar region. A top-edge taskbar can overlap it.
- Transparent WPF window rendering and animated layout need testing on older GPUs, remote sessions, and mixed-DPI hardware.
- A second launch exits without creating another window; use the hotkey or tray to reopen the first instance.
- Windows 10 edition/build support must be specified before distribution. Current .NET support policies do not cover every Windows 10 edition.

Next: startup registration and distribution packaging. Set NOTCH_LIVE_TRANSLATION=1 to exercise MyMemory using the synthetic Hello world phrase, including the WPF input/result/copy flow. Ordinary tests do not make translation network calls.



