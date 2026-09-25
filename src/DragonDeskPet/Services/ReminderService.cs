using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed class ReminderService : IReminderService
{
    private readonly IProductivityStore _store;

    public ReminderService(IProductivityStore store) => _store = store;

    public event EventHandler? PendingAlertsChanged;

    public IReadOnlyList<ReminderItem> GetReminders() => _store.Data.Reminders
        .OrderBy(item => item.NextDueUtc)
        .ToList();

    public IReadOnlyList<PendingAlert> GetPendingAlerts(DateTimeOffset nowUtc) => _store.Data.PendingAlerts
        .Where(item => item.SnoozedUntilUtc is null || item.SnoozedUntilUtc <= nowUtc)
        .OrderBy(item => item.SnoozedUntilUtc ?? item.DueUtc)
        .ToList();

    public ReminderItem Create(ReminderDraft draft)
    {
        var item = new ReminderItem
        {
            Message = draft.Message.Trim(),
            Repeat = draft.Repeat,
            NextDueUtc = draft.DueUtc.ToUniversalTime(),
            DailyLocalTime = draft.DailyLocalTime,
            IsEnabled = true
        };
        _store.Data.Reminders.Add(item);
        _store.Save();
        return item;
    }

    public void Update(ReminderItem item)
    {
        var target = _store.Data.Reminders.FirstOrDefault(candidate => candidate.Id == item.Id)
            ?? throw new InvalidOperationException("找不到要更新的提醒。");
        target.Message = item.Message.Trim();
        target.Repeat = item.Repeat;
        target.NextDueUtc = item.NextDueUtc.ToUniversalTime();
        target.DailyLocalTime = item.DailyLocalTime;
        target.IsEnabled = item.IsEnabled;
        _store.Save();
    }

    public void Delete(Guid id)
    {
        _store.Data.Reminders.RemoveAll(item => item.Id == id);
        _store.Data.PendingAlerts.RemoveAll(item => item.Source == AlertSource.Reminder && item.SourceId == id);
        _store.Save();
        PendingAlertsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetEnabled(Guid id, bool enabled)
    {
        var item = _store.Data.Reminders.FirstOrDefault(candidate => candidate.Id == id);
        if (item is null)
        {
            return;
        }

        item.IsEnabled = enabled;
        _store.Save();
    }

    public void Tick(DateTimeOffset nowUtc)
    {
        nowUtc = nowUtc.ToUniversalTime();
        var changed = false;
        foreach (var reminder in _store.Data.Reminders.Where(item => item.IsEnabled && item.NextDueUtc <= nowUtc).ToList())
        {
            var occurrenceKey = $"{reminder.Id:N}:{reminder.NextDueUtc.UtcTicks}";
            if (!_store.Data.PendingAlerts.Any(alert => alert.OccurrenceKey == occurrenceKey))
            {
                _store.Data.PendingAlerts.Add(new PendingAlert
                {
                    Source = AlertSource.Reminder,
                    SourceId = reminder.Id,
                    OccurrenceKey = occurrenceKey,
                    Title = "提醒时间到啦",
                    Message = reminder.Message,
                    DueUtc = reminder.NextDueUtc
                });
                changed = true;
            }

            if (reminder.Repeat == ReminderRepeat.Daily && reminder.DailyLocalTime is { } localTime)
            {
                reminder.NextDueUtc = GetNextDailyOccurrence(localTime, nowUtc);
            }
            else
            {
                reminder.IsEnabled = false;
            }

            changed = true;
        }

        if (changed)
        {
            _store.Save();
            PendingAlertsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Snooze(Guid alertId, TimeSpan delay, DateTimeOffset nowUtc)
    {
        var alert = _store.Data.PendingAlerts.FirstOrDefault(item => item.Id == alertId);
        if (alert is null)
        {
            return;
        }

        alert.SnoozedUntilUtc = nowUtc.ToUniversalTime().Add(delay);
        alert.TrayNotificationShown = false;
        _store.Save();
        PendingAlertsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Complete(Guid alertId)
    {
        if (_store.Data.PendingAlerts.RemoveAll(item => item.Id == alertId) == 0)
        {
            return;
        }

        _store.Save();
        PendingAlertsChanged?.Invoke(this, EventArgs.Empty);
    }

    public static DateTimeOffset GetNextDailyOccurrence(TimeOnly localTime, DateTimeOffset afterUtc)
    {
        var afterLocal = TimeZoneInfo.ConvertTime(afterUtc, TimeZoneInfo.Local);
        var date = DateOnly.FromDateTime(afterLocal.DateTime);
        var candidateLocal = date.ToDateTime(localTime);
        var candidate = new DateTimeOffset(candidateLocal, TimeZoneInfo.Local.GetUtcOffset(candidateLocal));
        if (candidate <= afterLocal)
        {
            candidateLocal = date.AddDays(1).ToDateTime(localTime);
            candidate = new DateTimeOffset(candidateLocal, TimeZoneInfo.Local.GetUtcOffset(candidateLocal));
        }

        return candidate.ToUniversalTime();
    }
}
