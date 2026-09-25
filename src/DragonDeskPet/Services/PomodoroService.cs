using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed class PomodoroService : IPomodoroService
{
    private readonly IProductivityStore _store;
    private readonly Func<AppSettings> _settings;

    public PomodoroService(IProductivityStore store, Func<AppSettings> settings)
    {
        _store = store;
        _settings = settings;
    }

    public event EventHandler? StateChanged;
    public PomodoroState State => _store.Data.Pomodoro;

    public TimeSpan GetRemaining(DateTimeOffset nowUtc)
    {
        if (State.IsPaused)
        {
            return State.PausedRemaining < TimeSpan.Zero ? TimeSpan.Zero : State.PausedRemaining;
        }

        if (!State.IsRunning || State.EndsAtUtc is null)
        {
            return TimeSpan.Zero;
        }

        var remaining = State.EndsAtUtc.Value - nowUtc.ToUniversalTime();
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public void Start(DateTimeOffset nowUtc)
    {
        if (State.IsRunning || State.IsPaused || State.IsAwaitingNextPhase)
        {
            return;
        }

        State.EndsAtUtc = nowUtc.ToUniversalTime().Add(GetPhaseDuration(State.Phase));
        State.IsRunning = true;
        State.IsPaused = false;
        State.PausedRemaining = TimeSpan.Zero;
        SaveAndNotify();
    }

    public void Pause(DateTimeOffset nowUtc)
    {
        if (!State.IsRunning)
        {
            return;
        }

        State.PausedRemaining = GetRemaining(nowUtc);
        State.EndsAtUtc = null;
        State.IsRunning = false;
        State.IsPaused = true;
        SaveAndNotify();
    }

    public void Resume(DateTimeOffset nowUtc)
    {
        if (!State.IsPaused)
        {
            return;
        }

        State.EndsAtUtc = nowUtc.ToUniversalTime().Add(State.PausedRemaining);
        State.PausedRemaining = TimeSpan.Zero;
        State.IsPaused = false;
        State.IsRunning = true;
        SaveAndNotify();
    }

    public void Cancel()
    {
        State.IsRunning = false;
        State.IsPaused = false;
        State.IsAwaitingNextPhase = false;
        State.EndsAtUtc = null;
        State.PausedRemaining = TimeSpan.Zero;
        State.Phase = PomodoroPhase.Focus;
        SaveAndNotify();
    }

    public void StartNextPhase(DateTimeOffset nowUtc)
    {
        if (!State.IsAwaitingNextPhase)
        {
            return;
        }

        State.IsAwaitingNextPhase = false;
        Start(nowUtc);
    }

    public void Tick(DateTimeOffset nowUtc)
    {
        if (!State.IsRunning || State.EndsAtUtc is null || State.EndsAtUtc > nowUtc.ToUniversalTime())
        {
            return;
        }

        var completedPhase = State.Phase;
        if (completedPhase == PomodoroPhase.Focus)
        {
            State.CompletedFocusRounds++;
            var rounds = Math.Clamp(_settings().PomodoroRoundsBeforeLongBreak, 1, 12);
            State.Phase = State.CompletedFocusRounds % rounds == 0
                ? PomodoroPhase.LongBreak
                : PomodoroPhase.ShortBreak;
        }
        else
        {
            State.Phase = PomodoroPhase.Focus;
        }

        var title = completedPhase == PomodoroPhase.Focus ? "专注完成啦" : "休息结束啦";
        var message = completedPhase == PomodoroPhase.Focus
            ? "做得很好，确认后开始休息。"
            : "休息得差不多了，确认后开始下一轮专注。";
        var occurrenceKey = $"pomodoro:{State.EndsAtUtc.Value.UtcTicks}";
        if (!_store.Data.PendingAlerts.Any(alert => alert.OccurrenceKey == occurrenceKey))
        {
            _store.Data.PendingAlerts.Add(new PendingAlert
            {
                Source = AlertSource.Pomodoro,
                OccurrenceKey = occurrenceKey,
                Title = title,
                Message = message,
                DueUtc = State.EndsAtUtc.Value
            });
        }

        State.IsRunning = false;
        State.IsPaused = false;
        State.IsAwaitingNextPhase = true;
        State.EndsAtUtc = null;
        State.PausedRemaining = TimeSpan.Zero;
        SaveAndNotify();
    }

    private TimeSpan GetPhaseDuration(PomodoroPhase phase)
    {
        var settings = _settings();
        var minutes = phase switch
        {
            PomodoroPhase.Focus => settings.PomodoroFocusMinutes,
            PomodoroPhase.ShortBreak => settings.PomodoroShortBreakMinutes,
            PomodoroPhase.LongBreak => settings.PomodoroLongBreakMinutes,
            _ => 25
        };
        return TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 240));
    }

    private void SaveAndNotify()
    {
        _store.Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
