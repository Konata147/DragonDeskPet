namespace DragonDeskPet.Core;

internal enum PetCompanionInvitationKind { Snack, Dance }

/// <summary>Limits optional companion invitations to a brief, quiet idle window.</summary>
internal sealed class PetCompanionInvitation(DateTimeOffset startedAt)
{
    internal static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(4);

    private DateTimeOffset _nextAt = startedAt + IdleDelay;
    private DateTimeOffset _endAt;
    private int _shownCount;

    internal bool IsShowing { get; private set; }
    internal PetCompanionInvitationKind CurrentKind { get; private set; }

    internal bool TryShow(DateTimeOffset now, DateTimeOffset lastInteraction, bool canStart,
        out PetCompanionInvitationKind kind)
    {
        kind = CurrentKind;
        if (IsShowing || !canStart || now < _nextAt || now - lastInteraction < IdleDelay)
            return false;
        CurrentKind = _shownCount++ % 2 == 0
            ? PetCompanionInvitationKind.Snack : PetCompanionInvitationKind.Dance;
        kind = CurrentKind;
        IsShowing = true;
        _endAt = now + VisibleDuration;
        _nextAt = now + Cooldown;
        return true;
    }

    internal bool ShouldHide(DateTimeOffset now, bool canContinue) =>
        IsShowing && (!canContinue || now >= _endAt);

    internal void Defer(DateTimeOffset now)
    {
        if (!IsShowing) _nextAt = now + IdleDelay;
    }

    internal void Hide(DateTimeOffset now, bool interrupted)
    {
        IsShowing = false;
        if (interrupted) _nextAt = now + IdleDelay;
    }
}
