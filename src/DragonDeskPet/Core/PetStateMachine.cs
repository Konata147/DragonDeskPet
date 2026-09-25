namespace DragonDeskPet.Core;

public sealed class PetStateMachine
{
    public PetState Current { get; private set; } = PetState.Idle;
    public long Revision { get; private set; }

    public event EventHandler<PetState>? StateChanged;

    public void TransitionTo(PetState next)
    {
        if (Current == next)
        {
            return;
        }

        Current = next;
        Revision++;
        StateChanged?.Invoke(this, next);
    }

    public bool TryFinishFeedback(long expectedRevision, bool pointerOverCharacter = false)
    {
        if (Revision != expectedRevision || Current is not (PetState.Happy or PetState.Angry))
        {
            return false;
        }

        TransitionTo(pointerOverCharacter ? PetState.Hover : PetState.Idle);
        return true;
    }
}
