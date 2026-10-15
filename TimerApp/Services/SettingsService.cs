using System;
using System.IO;
using System.Text.Json;
using TimerApp.Models;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace TimerApp.Services;

/// <summary>
/// Сохранение настроек между запусками.
///
/// Основное хранилище упакованного приложения — ApplicationData.LocalSettings
/// (тема, громкость, mute, последняя длительность) и JSON пользовательских
/// пресетов в том же контейнере. Если пакетная идентичность недоступна
/// (например, ручной запуск exe вне MSIX), используется резервный JSON-файл
/// в %LocalAppData%\TimerApp\settings.json — приложение при этом не падает.
/// </summary>
public sealed class SettingsService
{
    private const string ThemeKey = "Theme";
    private const string VolumeKey = "Volume";
    private const string IsMutedKey = "IsMuted";
    private const string SoundIdKey = "SoundId";
    private const string CustomSoundKey = "CustomSoundFileName";
    private const string LastHoursKey = "LastHours";
    private const string LastMinutesKey = "LastMinutes";
    private const string LastSecondsKey = "LastSeconds";
    private const string PresetsKey = "CustomPresetsJson";
    private const string SavedStateKey = "SavedTimerState";
    private const string SavedEndKey = "SavedTimerEndUtcTicks";
    private const string SavedDurationKey = "SavedTimerDurationSeconds";
    private const string SavedRemainingKey = "SavedTimerRemainingSeconds";
    private const string LastPresetNameKey = "LastPresetName";
    private const string LastTimerNameKey = "LastTimerName";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private bool _usePackagedStorage;

    public AppSettings Load()
    {
        try
        {
            // Доступ к ApplicationData.Current бросает исключение,
            // если у процесса нет пакетной идентичности.
            var values = ApplicationData.Current.LocalSettings.Values;
            _usePackagedStorage = true;

            var settings = new AppSettings
            {
                Theme = GetString(values, ThemeKey, "Dark"),
                Volume = GetDouble(values, VolumeKey, 0.7),
                IsMuted = GetBool(values, IsMutedKey, false),
                SoundId = GetString(values, SoundIdKey, "alarm"),
                CustomSoundFileName = GetString(values, CustomSoundKey, string.Empty),
                LastHours = GetDouble(values, LastHoursKey, 0),
                LastMinutes = GetDouble(values, LastMinutesKey, 5),
                LastSeconds = GetDouble(values, LastSecondsKey, 0),
                SavedTimerState = GetString(values, SavedStateKey, string.Empty),
                SavedTimerEndUtcTicks = GetLong(values, SavedEndKey, 0),
                SavedTimerDurationSeconds = GetDouble(values, SavedDurationKey, 0),
                SavedTimerRemainingSeconds = GetDouble(values, SavedRemainingKey, 0),
                LastPresetName = GetString(values, LastPresetNameKey, string.Empty),
                LastTimerName = GetString(values, LastTimerNameKey, string.Empty)
            };

            // Битый JSON пресетов не должен сбрасывать все остальные настройки:
            // в худшем случае просто начнём со списка пресетов пустым.
            try
            {
                string presetsJson = GetString(values, PresetsKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(presetsJson))
                {
                    settings.CustomPresets =
                        JsonSerializer.Deserialize<System.Collections.Generic.List<CustomPreset>>(
                            presetsJson, JsonOptions) ?? new();
                }
            }
            catch (JsonException)
            {
                settings.CustomPresets = new();
            }

            return settings;
        }
        catch (Exception)
        {
            _usePackagedStorage = false;
            return LoadFromFile();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Volume = Math.Clamp(settings.Volume, 0.0, 1.0);

        if (_usePackagedStorage)
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                values[ThemeKey] = settings.Theme;
                values[VolumeKey] = settings.Volume;
                values[IsMutedKey] = settings.IsMuted;
                values[SoundIdKey] = settings.SoundId;
                values[CustomSoundKey] = settings.CustomSoundFileName;
                values[LastHoursKey] = settings.LastHours;
                values[LastMinutesKey] = settings.LastMinutes;
                values[LastSecondsKey] = settings.LastSeconds;
                values[PresetsKey] = JsonSerializer.Serialize(settings.CustomPresets, JsonOptions);
                values[SavedStateKey] = settings.SavedTimerState;
                values[SavedEndKey] = settings.SavedTimerEndUtcTicks;
                values[SavedDurationKey] = settings.SavedTimerDurationSeconds;
                values[SavedRemainingKey] = settings.SavedTimerRemainingSeconds;
                values[LastPresetNameKey] = settings.LastPresetName;
                values[LastTimerNameKey] = settings.LastTimerName;
                return;
            }
            catch (Exception)
            {
                _usePackagedStorage = false;
            }
        }

        SaveToFile(settings);
    }

    /// <summary>Папка для пользовательского файла звука (копия выбранного файла).</summary>
    public string GetCustomSoundFolderPath()
    {
        try
        {
            string path = ApplicationData.Current.LocalFolder.Path;
            Directory.CreateDirectory(path);
            return path;
        }
        catch (Exception)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TimerApp");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Полный путь к пользовательскому звуку или null, если не выбран.</summary>
    public string? GetCustomSoundFilePath(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.CustomSoundFileName)
            ? null
            : Path.Combine(GetCustomSoundFolderPath(), settings.CustomSoundFileName);
    }

    private static string GetString(IPropertySet values, string key, string fallback) =>
        values.TryGetValue(key, out object? raw) && raw is string s ? s : fallback;

    private static double GetDouble(IPropertySet values, string key, double fallback) =>
        values.TryGetValue(key, out object? raw) && raw is double d ? d : fallback;

    private static long GetLong(IPropertySet values, string key, long fallback) =>
        values.TryGetValue(key, out object? raw) && raw is long l ? l : fallback;

    private static bool GetBool(IPropertySet values, string key, bool fallback) =>
        values.TryGetValue(key, out object? raw) && raw is bool b ? b : fallback;

    private static string FallbackFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TimerApp",
        "settings.json");

    private static AppSettings LoadFromFile()
    {
        try
        {
            string path = FallbackFilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                    return settings;
            }
        }
        catch (Exception)
        {
            // Повреждённый файл настроек не должен мешать запуску:
            // просто вернём значения по умолчанию ниже.
        }

        return new AppSettings();
    }

    private static void SaveToFile(AppSettings settings)
    {
        try
        {
            string path = FallbackFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception)
        {
            // Нет доступа к диску — настройки просто не сохранятся на этот раз.
        }
    }
}
