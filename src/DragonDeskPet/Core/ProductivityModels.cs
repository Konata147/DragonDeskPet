namespace DragonDeskPet.Core;

public enum ReminderRepeat
{
    None,
    Daily
}

public enum AlertSource
{
    Reminder,
    Course,
    Pomodoro
}

public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak
}

public enum CourseWeekPattern
{
    All,
    Odd,
    Even
}

public sealed class ReminderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Message { get; set; } = string.Empty;
    public ReminderRepeat Repeat { get; set; }
    public DateTimeOffset NextDueUtc { get; set; }
    public TimeOnly? DailyLocalTime { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PendingAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AlertSource Source { get; set; }
    public Guid? SourceId { get; set; }
    public string OccurrenceKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset DueUtc { get; set; }
    public DateTimeOffset? SnoozedUntilUtc { get; set; }
    public bool TrayNotificationShown { get; set; }
}

public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public DateOnly AssignedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CourseItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Teacher { get; set; } = string.Empty;
    public int StartWeek { get; set; } = 1;
    public int EndWeek { get; set; } = 20;
    public CourseWeekPattern WeekPattern { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int? ReminderMinutes { get; set; } = 30;
    public HashSet<DateOnly> ExcludedDates { get; set; } = [];
    public HashSet<DateOnly> IncludedDates { get; set; } = [];
    public DateOnly? LastAlertedDate { get; set; }
}

public sealed class SemesterSettings
{
    public string Name { get; set; } = "当前学期";
    public DateOnly StartDate { get; set; } = StartOfCurrentWeek();

    private static DateOnly StartOfCurrentWeek()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var offset = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-offset);
    }
}

public sealed class PomodoroState
{
    public PomodoroPhase Phase { get; set; } = PomodoroPhase.Focus;
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public bool IsAwaitingNextPhase { get; set; }
    public DateTimeOffset? EndsAtUtc { get; set; }
    public TimeSpan PausedRemaining { get; set; }
    public int CompletedFocusRounds { get; set; }
}

public sealed class ProductivityData
{
    public int SchemaVersion { get; set; } = 1;
    public List<ReminderItem> Reminders { get; set; } = [];
    public List<PendingAlert> PendingAlerts { get; set; } = [];
    public List<TodoItem> Todos { get; set; } = [];
    public List<CourseItem> Courses { get; set; } = [];
    public SemesterSettings Semester { get; set; } = new();
    public PomodoroState Pomodoro { get; set; } = new();
    public DateTimeOffset? LastCourseSchedulerCheckUtc { get; set; }
}

public sealed record ReminderDraft(
    string Message,
    ReminderRepeat Repeat,
    DateTimeOffset DueUtc,
    TimeOnly? DailyLocalTime = null);

public sealed record CourseOccurrence(CourseItem Course, DateOnly Date, int TeachingWeek)
{
    public string OccurrenceKey => $"{Course.Id:N}:{Date:yyyy-MM-dd}";
}

public enum CourseImportDisposition
{
    Add,
    Update,
    Duplicate,
    Invalid
}

public sealed record CourseImportRow(
    CourseImportDisposition Disposition,
    CourseItem? Course,
    string SourceDescription,
    string? ErrorMessage = null);

public sealed class CourseImportPreview
{
    public List<CourseImportRow> Rows { get; } = [];
    public int AddedCount => Rows.Count(row => row.Disposition == CourseImportDisposition.Add);
    public int UpdatedCount => Rows.Count(row => row.Disposition == CourseImportDisposition.Update);
    public int DuplicateCount => Rows.Count(row => row.Disposition == CourseImportDisposition.Duplicate);
    public int InvalidCount => Rows.Count(row => row.Disposition == CourseImportDisposition.Invalid);
}
