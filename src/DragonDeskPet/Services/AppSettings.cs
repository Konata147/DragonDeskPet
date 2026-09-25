using System.Text.Json.Serialization;

namespace DragonDeskPet.Services;

public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1.0;
    public bool AlwaysOnTop { get; set; } = true;
    public bool AutoHideInFullscreen { get; set; } = true;
    public bool ReminderSoundEnabled { get; set; } = true;
    public int PomodoroFocusMinutes { get; set; } = 25;
    public int PomodoroShortBreakMinutes { get; set; } = 5;
    public int PomodoroLongBreakMinutes { get; set; } = 15;
    public int PomodoroRoundsBeforeLongBreak { get; set; } = 4;
    public bool StartWithWindows { get; set; }
    public bool HasCompletedOnboarding { get; set; }
    public string Provider { get; set; } = "Offline";
    public string Model { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ProtectedApiKey { get; set; } = string.Empty;

    [JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;
}
