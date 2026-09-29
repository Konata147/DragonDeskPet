namespace DragonDeskPet.Core;

public sealed class PetTapCooldown
{
    public const int DelayMilliseconds = 700;
    private DateTimeOffset _nextAllowedAt;

    public bool TryAccept(DateTimeOffset now)
    {
        if (now < _nextAllowedAt) return false;
        _nextAllowedAt = now.AddMilliseconds(DelayMilliseconds);
        return true;
    }
}
