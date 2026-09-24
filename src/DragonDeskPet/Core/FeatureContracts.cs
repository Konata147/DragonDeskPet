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
    Task ScheduleAsync(DateTimeOffset when, string message, CancellationToken cancellationToken = default);
}

public interface ISystemActionService
{
    Task RequestAsync(string action, bool requiresConfirmation, CancellationToken cancellationToken = default);
}
