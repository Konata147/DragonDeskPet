namespace DragonDeskPet.Core;

public sealed class PetStateMachine
{
    public PetState Current { get; private set; } = PetState.Idle;

    public event EventHandler<PetState>? StateChanged;

    public void TransitionTo(PetState next)
    {
        if (Current == next)
        {
            return;
        }

        Current = next;
        StateChanged?.Invoke(this, next);
    }
}
