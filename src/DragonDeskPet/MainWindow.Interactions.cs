using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DragonDeskPet.Core;
using DragonDeskPet.Services;

namespace DragonDeskPet;

public partial class MainWindow
{
    private PetAnimator _petAnimator = null!;
    private PetActivity? _activePetActivity;
    private bool _preserveCharacterImageForAction;
    private bool _restingByChoice;
    private bool _closing;
    private bool _settingsOpen;
    private DateTimeOffset _nextAmbientAction = DateTimeOffset.UtcNow.AddSeconds(40);
    private DateTimeOffset _nextManualAction;
    private int _ambientIndex;
    private int _clickReactionIndex;
    private bool _wakeOnClick;
    private readonly PetAnimationLibrary _animationLibrary = new();
    private readonly PetAnimationPlayer _animationPlayer = new();
    private PetCompanionAnimator _companionAnimator = null!;
    private DateTimeOffset _nextBlink = DateTimeOffset.UtcNow.AddSeconds(6);

    private bool IsFocusing => _app.PomodoroService is { State: { IsRunning: true, Phase: PomodoroPhase.Focus } };
    private bool PetCanInteract => PetActivities.CanStart(_stateMachine.Current,
        _isBusy, _mouseDown || _dragged, IsVisible && !_isFullscreenActive && !_closing);

    private void InitializePetInteractions()
    {
        _petAnimator = new PetAnimator(PetStateScaleTransform, PetRotateTransform,
            PetTranslateTransform, PetAccentText);
        _companionAnimator = new PetCompanionAnimator(AiCompanion, AiCompanionScale,
            AiCompanionRotate, AiCompanionTranslate);
        _animationPlayer.FrameChanged += frame =>
        {
            CharacterImage.Source = frame;
            if (_activePetActivity == PetActivity.Wake && AiCompanion.Source is not null)
                AiCompanion.Visibility = _animationPlayer.CurrentFrameIndex == 0
                    ? Visibility.Collapsed : Visibility.Visible;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!_loaded) return;
            if (!IsVisible)
            {
                CancelTreat();
                _strokeRecognizer.Reset();
                _animationPlayer.Stop();
                ((ContextMenu)FindResource("InteractionMenu")).IsOpen = false;
                if (_activePetActivity is not null)
                    _stateMachine.TransitionTo(_restingByChoice ? PetState.Sleeping : PetState.Idle);
                _petAnimator.Stop();
                _companionAnimator.Suspend();
            }
            else
            {
                _nextAmbientAction = DateTimeOffset.UtcNow.AddSeconds(40);
                _nextBlink = DateTimeOffset.UtcNow.AddSeconds(6);
                ApplyStateVisual(_stateMachine.Current);
            }
        };
        _app.PomodoroService.StateChanged += PomodoroPetStateChanged;
    }

    private void PomodoroPetStateChanged(object? sender, EventArgs e)
    {
        if (_closing || !_loaded) return;
        if (_stateMachine.Current is PetState.Idle or PetState.Hover)
            ApplyStateVisual(_stateMachine.Current);
    }

    private void InteractMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HideQuickBar();
        var menu = (ContextMenu)FindResource("InteractionMenu");
        menu.PlacementTarget = CharacterHost;
        menu.Placement = PlacementMode.Left;
        menu.IsOpen = true;
    }

    private void InteractionMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = PetCanInteract;
            if (Equals(item.Tag, "Rest"))
                item.Header = _stateMachine.Current == PetState.Sleeping ? "☀  叫醒她" : "☾  打个盹";
        }
    }

    private void PetActivity_Click(object sender, RoutedEventArgs e)
    {
        if (!PetCanInteract || sender is not MenuItem { Tag: string action }) return;
        if (action == "Feed") { ShowTreat(); return; }
        if (action == "Rest")
        {
            if (_stateMachine.Current == PetState.Sleeping)
                PlayPetActivity(PetActivity.Wake);
            else
            {
                _restingByChoice = true;
                _stateMachine.TransitionTo(PetState.Sleeping);
            }
            return;
        }
        if (Enum.TryParse<PetActivity>(action, out var activity)) PlayPetActivity(activity);
    }

    private void PlayPetActivity(PetActivity activity, bool automatic = false)
    {
        if (!PetCanInteract) return;
        var now = DateTimeOffset.UtcNow;
        if (!automatic && now < _nextManualAction) return;
        if (!automatic)
        {
            _nextManualAction = now.AddMilliseconds(350);
            _lastInteraction = DateTimeOffset.Now;
            _restingByChoice = false;
        }
        _nextAmbientAction = now.AddSeconds(Random.Shared.Next(35, 61));
        _nextBlink = now.AddSeconds(Random.Shared.Next(5, 11));
        var info = PetActivities.Describe(activity);
        var clip = _animationLibrary.Load(activity.ToString());
        // StateChanged is synchronous. Keep the current pose until the clip's
        // first frame is ready instead of flashing the unrelated Happy sprite.
        _preserveCharacterImageForAction = clip is not null;
        try { _stateMachine.TransitionTo(PetState.Happy, restart: true); }
        finally { _preserveCharacterImageForAction = false; }
        _activePetActivity = activity;
        StateText.Text = info.Caption;
        if (clip is not null) _companionAnimator.React(activity);
        if (clip is null)
        {
            _petAnimator.Play(activity, _app.Settings.ReducePetMotion);
            _ = ReturnToIdleAsync(info.DurationMilliseconds);
        }
        else
        {
            var revision = _stateMachine.Revision;
            _animationPlayer.Play(clip, _app.Settings.ReducePetMotion, () =>
            {
                if (_stateMachine.TryFinishFeedback(revision, IsPointerOverCharacter()))
                    _nextBlink = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(5, 11));
            });
            if (activity == PetActivity.Hop && !_app.Settings.ReducePetMotion)
                _petAnimator.PlayHopMotion(_app.Settings.Scale);
            if (_app.Settings.ReducePetMotion) _ = ReturnToIdleAsync(info.DurationMilliseconds);
        }
    }

    private void TickPetInteractions(DateTimeOffset nowUtc)
    {
        _companionAnimator.Tick(nowUtc, IsFocusing);
        var available = IsVisible && !_isFullscreenActive && !_closing && !_settingsOpen
            && !_isBusy && !_mouseDown && !_dragged && !_feeding && !_animationPlayer.IsPlaying
            && _activePetActivity is null && _app.Settings.AmbientPetActionsEnabled && !_app.Settings.ReducePetMotion;
        if (nowUtc >= _nextBlink)
        {
            _nextBlink = nowUtc.AddSeconds(IsFocusing ? Random.Shared.Next(9, 15) : Random.Shared.Next(5, 11));
            if (available && _stateMachine.Current == PetState.Idle)
                PlayQuietClip("Blink");
        }
        if (nowUtc < _nextAmbientAction) return;
        _nextAmbientAction = nowUtc.AddSeconds(Random.Shared.Next(35, 61));
        var panelOpen = _settingsOpen || _feeding || _animationPlayer.IsPlaying || ChatBubble.IsVisible || ProductivityPanel.IsVisible
            || ReminderAlertCard.IsVisible || OnboardingBubble.IsVisible || QuickBar.IsVisible
            || ((ContextMenu)FindResource("PetMenu")).IsOpen
            || ((ContextMenu)FindResource("InteractionMenu")).IsOpen;
        if (!PetActivities.CanPlayAmbient(_stateMachine.Current, _isBusy, _mouseDown || _dragged,
            IsVisible && !_isFullscreenActive && !_closing, IsFocusing, panelOpen,
            _app.Settings.ReducePetMotion, _app.Settings.AmbientPetActionsEnabled)) return;
        if (_ambientIndex++ % 2 == 0) PlayQuietClip("Tail");
        else PlayPetActivity(PetActivity.LookAround, automatic: true);
    }

    private void PlayQuietClip(string id)
    {
        if (_activePetActivity is not null || _animationPlayer.IsPlaying) return;
        var clip = _animationLibrary.Load(id);
        if (clip is null) return;
        var revision = _stateMachine.Revision;
        var restore = CharacterImage.Source;
        _animationPlayer.Play(clip, false, () =>
        {
            if (_stateMachine.Revision == revision) CharacterImage.Source = restore;
        });
    }
}
