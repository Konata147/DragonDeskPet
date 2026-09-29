namespace DragonDeskPet.Core;

/// <summary>One-shot long press; pointer distance is evaluated by the UI in logical pixels.</summary>
public sealed class PetHoldRecognizer
{
    public const int DelayMilliseconds = 550;
    private long _startedAt;
    public bool IsArmed { get; private set; }

    public void Begin(long now, bool eligible)
    {
        _startedAt = now;
        IsArmed = eligible;
    }

    public void Cancel() => IsArmed = false;

    public bool TryTrigger(long now, bool buttonPressed, bool withinDragThreshold, bool eligible)
    {
        if (!IsArmed) return false;
        if (!buttonPressed || !withinDragThreshold || !eligible)
        {
            Cancel();
            return false;
        }
        if (now - _startedAt < DelayMilliseconds) return false;
        Cancel();
        return true;
    }
}
