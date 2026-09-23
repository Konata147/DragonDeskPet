namespace DragonDeskPet.Core;

// Extension seams for later milestones. V0.1 intentionally ships no implementations.
public interface IScreenshotAssistant
{
    Task HandleScreenshotAsync(CancellationToken cancellationToken = default);
}

public interface IClipboardAssistant
{
    Task HandleClipboardAsync(CancellationToken cancellationToken = default);
}

public interface IDropPayloadHandler
{
    bool CanHandle(IReadOnlyList<string> paths);
    Task HandleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}

public interface IReminderService
{
    Task ScheduleAsync(DateTimeOffset when, string message, CancellationToken cancellationToken = default);
}

public interface ISystemActionService
{
    Task RequestAsync(string action, bool requiresConfirmation, CancellationToken cancellationToken = default);
}
