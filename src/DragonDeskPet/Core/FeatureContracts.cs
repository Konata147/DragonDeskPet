namespace DragonDeskPet.Core;

// Extension seams reserved for later milestones.
public enum ClipboardContentKind
{
    Empty,
    Text,
    Image,
    Failed
}

public sealed record ClipboardContentResult(
    ClipboardContentKind Kind,
    string? Text = null,
    CapturedScreenshot? Image = null,
    string? ErrorMessage = null);

public interface IClipboardContentService
{
    Task<ClipboardContentResult> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IImageFileService
{
    bool CanLoad(string path);
    Task<CapturedScreenshot> LoadAsync(string path, CancellationToken cancellationToken = default);
}

public interface IReminderService
{
    event EventHandler? PendingAlertsChanged;
    IReadOnlyList<ReminderItem> GetReminders();
    IReadOnlyList<PendingAlert> GetPendingAlerts(DateTimeOffset nowUtc);
    ReminderItem Create(ReminderDraft draft);
    void Update(ReminderItem item);
    void Delete(Guid id);
    void SetEnabled(Guid id, bool enabled);
    void Tick(DateTimeOffset nowUtc);
    void Snooze(Guid alertId, TimeSpan delay, DateTimeOffset nowUtc);
    void Complete(Guid alertId);
}

public interface IProductivityStore
{
    ProductivityData Data { get; }
    string DataPath { get; }
    string? RecoveryNotice { get; }
    void Save();
}

public interface IPomodoroService
{
    event EventHandler? StateChanged;
    PomodoroState State { get; }
    TimeSpan GetRemaining(DateTimeOffset nowUtc);
    void Start(DateTimeOffset nowUtc);
    void Pause(DateTimeOffset nowUtc);
    void Resume(DateTimeOffset nowUtc);
    void Cancel();
    void StartNextPhase(DateTimeOffset nowUtc);
    void Tick(DateTimeOffset nowUtc);
}

public interface ICourseScheduleService
{
    int GetTeachingWeek(DateOnly date);
    IReadOnlyList<CourseOccurrence> GetCoursesForDate(DateOnly date);
    void AddOrUpdate(CourseItem course);
    void Delete(Guid id);
    void SetEnabled(Guid id, bool enabled);
    void SkipDate(Guid id, DateOnly date);
    void Tick(DateTimeOffset nowLocal);
}

public interface ICourseScheduleImporter
{
    CourseImportPreview Preview(string path, IReadOnlyList<CourseItem> existingCourses, SemesterSettings semester);
    void Apply(CourseImportPreview preview, bool replaceCurrentSemester);
}

public interface ISystemActionService
{
    Task RequestAsync(string action, bool requiresConfirmation, CancellationToken cancellationToken = default);
}
