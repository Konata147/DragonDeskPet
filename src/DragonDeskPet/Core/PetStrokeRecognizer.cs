namespace DragonDeskPet.Core;

/// <summary>Consumes physical pointer movement in stable character coordinates.</summary>
public sealed class PetStrokeRecognizer
{
    private long _startedAt;
    private long _cooldownUntil;
    private double? _lastX;
    private double _distance;
    private int _direction;
    private bool _reversed;
    public void Reset() { _lastX = null; _distance = 0; _direction = 0; _reversed = false; }
    public bool Observe(double x, bool insideHead, bool eligible, long milliseconds)
    {
        if (!eligible || !insideHead || milliseconds < _cooldownUntil) { Reset(); return false; }
        if (_lastX is null || milliseconds - _startedAt > 1000)
        {
            Reset(); _lastX = x; _startedAt = milliseconds; return false;
        }
        var movement = x - _lastX.Value;
        if (Math.Abs(movement) < 2) return false;
        _lastX = x;
        var direction = Math.Sign(movement);
        if (_direction != 0 && _direction != direction) _reversed = true;
        _direction = direction; _distance += Math.Abs(movement);
        if (!_reversed || _distance < 24) return false;
        Reset(); _cooldownUntil = milliseconds + 3000; return true;
    }
}
