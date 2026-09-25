using System.Text.Json;
using System.IO;

namespace DragonDeskPet.Services;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SettingsService(string? settingsDirectory = null)
    {
        SettingsDirectory = settingsDirectory
            ?? Environment.GetEnvironmentVariable("DRAGON_DESK_PET_DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
    }

    public string SettingsDirectory { get; }

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            settings.ApiKey = WindowsSecretProtector.Unprotect(settings.ProtectedApiKey);
            settings.Scale = Math.Clamp(settings.Scale, 0.6, 2.0);
            NormalizeProductivitySettings(settings);
            return settings;
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        settings.Scale = Math.Clamp(settings.Scale, 0.6, 2.0);
        NormalizeProductivitySettings(settings);
        settings.ProtectedApiKey = WindowsSecretProtector.Protect(settings.ApiKey);

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    private static void NormalizeProductivitySettings(AppSettings settings)
    {
        settings.PomodoroFocusMinutes = Math.Clamp(settings.PomodoroFocusMinutes, 1, 240);
        settings.PomodoroShortBreakMinutes = Math.Clamp(settings.PomodoroShortBreakMinutes, 1, 120);
        settings.PomodoroLongBreakMinutes = Math.Clamp(settings.PomodoroLongBreakMinutes, 1, 180);
        settings.PomodoroRoundsBeforeLongBreak = Math.Clamp(settings.PomodoroRoundsBeforeLongBreak, 1, 12);
    }
}
