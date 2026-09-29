using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DragonDeskPet.Services;

/// <summary>One finite clip at a time. No ticking timer when stopped or showing a static poster.</summary>
public sealed class PetAnimationPlayer : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _watch = new();
    private LoadedPetAnimation? _animation;
    private Action? _completed;
    private int _frame = -1;
    public bool IsPlaying => _timer.IsEnabled;
    public int CurrentFrameIndex => _frame;
    public event Action<BitmapSource>? FrameChanged;
    public PetAnimationPlayer() => _timer.Tick += Tick;
    public void Play(LoadedPetAnimation animation, bool reducedMotion, Action? completed = null)
    {
        Stop(); _animation = animation;
        if (reducedMotion) { FrameChanged?.Invoke(animation.Frames[animation.Clip.PosterFrame]); return; }
        _completed = completed; _watch.Restart(); Advance(0); _timer.Start();
    }
    public void Stop() { _timer.Stop(); _watch.Reset(); _animation = null; _completed = null; _frame = -1; }
    private void Tick(object? sender, EventArgs e) => Advance(_watch.ElapsedMilliseconds);
    internal void Advance(long elapsedMilliseconds)
    {
        if (_animation is not { } animation) return;
        if (elapsedMilliseconds >= animation.Clip.DurationMs)
        { var completed = _completed; Stop(); completed?.Invoke(); return; }
        var index = animation.Clip.FrameAt(elapsedMilliseconds);
        if (index == _frame) return;
        _frame = index; FrameChanged?.Invoke(animation.Frames[index]);
    }
    public void Dispose() { Stop(); _timer.Tick -= Tick; }
}
