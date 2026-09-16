using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Notch.Services.Contracts;

namespace Notch.Infrastructure.Persistence;

public sealed class JsonSettingsService(string path) : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _newerSchema;
    public string? LoadWarning { get; private set; }

    public async Task<NotchSettings> LoadAsync(CancellationToken cancellationToken)
    {
        LoadWarning = null;
        if (!File.Exists(path)) return new();
        try
        {
            await using var stream = File.OpenRead(path);
            var settings = await JsonSerializer.DeserializeAsync<NotchSettings>(stream, Options, cancellationToken)
                ?? throw new JsonException("Empty settings.");
            if (settings.SchemaVersion > 1)
            {
                _newerSchema = true;
                LoadWarning = "Настройки созданы новой версией Notch. Исходный файл сохранён без изменений.";
                return new();
            }
            if (settings.SchemaVersion != 1 || !IsValidHotkey(settings.ToggleHotkey))
            {
                LoadWarning = "Некорректная горячая клавиша заменена на Ctrl+Alt+N.";
                settings = settings with { SchemaVersion = 1, ToggleHotkey = new NotchSettings().ToggleHotkey };
            }
            return settings;
        }
        catch (JsonException)
        {
            // Preserve corrupt input for recovery; never replace it silently.
            var backup = path + ".invalid-" + Guid.NewGuid().ToString("N");
            File.Move(path, backup);
            LoadWarning = "Настройки восстановлены по умолчанию. Копия старого файла сохранена.";
            return new();
        }
    }

    public async Task SaveAsync(NotchSettings settings, CancellationToken cancellationToken)
    {
        if (_newerSchema) throw new InvalidOperationException("Refusing to overwrite a newer settings schema.");
        if (!IsValidHotkey(settings.ToggleHotkey)) throw new ArgumentException("Invalid hotkey.", nameof(settings));
        await _saveGate.WaitAsync(cancellationToken);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            _saveGate.Release();
        }
    }

    public static bool IsValidHotkey(HotkeyGesture? gesture) => gesture?.IsValid() == true;
}
