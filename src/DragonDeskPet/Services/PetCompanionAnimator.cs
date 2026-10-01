using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DragonDeskPet.Core;
using Image = System.Windows.Controls.Image;

namespace DragonDeskPet.Services;

/// <summary>Independent, finite reactions for the small cloud companion.</summary>
public sealed class PetCompanionAnimator(
    Image image, ScaleTransform scale, RotateTransform rotate, TranslateTransform translate)
{
    private enum Mood { Normal, Blink, Curious, Happy, Thinking, Angry }

    // The six generated sprites share this transparent canvas and cloud anchor.
    private static readonly Int32Rect SpriteCrop = new(365, 435, 555, 437);

    private readonly Dictionary<Mood, BitmapSource> _faces = [];
    private readonly DispatcherTimer _blinkTimer = new(DispatcherPriority.Normal)
    {
        Interval = TimeSpan.FromMilliseconds(170)
    };
    private DispatcherTimer? _tapTimer;
    private DispatcherTimer? _reactionTimer;
    private readonly Stopwatch _reactionWatch = new();
    private (int Milliseconds, Mood Face)[] _reactionFaces = [];
    private (int Milliseconds, double Y)[] _reactionMotion = [];
    private int _nextReactionFace;
    private int _reactionDurationMs;
    private long _revision;
    private PetState _state = PetState.Idle;
    private bool _reducedMotion;
    private DateTimeOffset _nextAmbient = DateTimeOffset.UtcNow.AddSeconds(8);

    public void LoadSources(BitmapSource normal, BitmapSource curious, BitmapSource happy,
        BitmapSource thinking, BitmapSource angry, string? directory = null)
    {
        _blinkTimer.Tick -= EndBlink;
        _blinkTimer.Tick += EndBlink;
        _faces.Clear();
        directory ??= Path.Combine(AssetService.CharacterDirectory, "companion");
        Add(Mood.Normal, directory, "normal.png", normal, new Int32Rect(953, 490, 127, 116));
        Add(Mood.Blink, directory, "blink.png", normal, new Int32Rect(953, 490, 127, 116));
        Add(Mood.Curious, directory, "curious.png", curious, new Int32Rect(935, 455, 125, 145));
        Add(Mood.Happy, directory, "happy.png", happy, new Int32Rect(965, 355, 125, 120));
        Add(Mood.Thinking, directory, "thinking.png", thinking, new Int32Rect(930, 480, 125, 120));
        Add(Mood.Angry, directory, "angry.png", angry, new Int32Rect(940, 500, 125, 120));
        SetFace(Mood.Normal);
    }

    public void SetState(PetState state, bool reducedMotion, bool hasActionArtwork,
        bool settleAfterFeedback = false)
    {
        StopMotion();
        _state = state;
        _reducedMotion = reducedMotion;
        var show = _faces.Count > 0
            && (state is PetState.Idle or PetState.Hover
                || state == PetState.Happy && hasActionArtwork);
        image.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        SetFace(state == PetState.Hover ? Mood.Curious : Mood.Normal);
        _nextAmbient = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(7, 12));
        if (state == PetState.Hover && !reducedMotion && !settleAfterFeedback)
            Animate(translate, TranslateTransform.YProperty,
                (0, 0), (180, -2), (450, 0));
    }

    public void React(PetActivity activity)
    {
        if (image.Visibility != Visibility.Visible) return;
        StopMotion();
        SetFace(activity switch
        {
            PetActivity.Pet => Mood.Blink,
            PetActivity.Feed or PetActivity.Hop or PetActivity.LookAround
                or PetActivity.Land or PetActivity.Wake => Mood.Curious,
            PetActivity.Stretch => Mood.Thinking,
            _ => Mood.Happy
        });
        if (_reducedMotion) return;
        switch (activity)
        {
            case PetActivity.Greet:
                Animate(translate, TranslateTransform.YProperty,
                    (0, 0), (150, -3), (300, 0), (460, -3), (660, 0));
                break;
            case PetActivity.Pet:
                Animate(scale, ScaleTransform.ScaleYProperty,
                    (0, 1), (180, .8), (440, 1));
                break;
            case PetActivity.Feed:
                Animate(translate, TranslateTransform.YProperty,
                    (0, 0), (180, 2), (390, -3), (650, 0));
                break;
            case PetActivity.Cuddle:
                Animate(scale, ScaleTransform.ScaleXProperty,
                    (0, 1), (240, 1.13), (640, 1));
                Animate(scale, ScaleTransform.ScaleYProperty,
                    (0, 1), (240, 1.1), (640, 1));
                break;
            case PetActivity.Hop:
                Animate(translate, TranslateTransform.YProperty,
                    (0, 0), (300, -3), (600, -8), (850, -4), (1250, 0));
                break;
            case PetActivity.Dance:
                Animate(rotate, RotateTransform.AngleProperty,
                    (0, 0), (170, -8), (350, 8), (530, -8), (760, 0));
                break;
            case PetActivity.Stretch:
                Animate(scale, ScaleTransform.ScaleYProperty,
                    (0, 1), (220, 1.14), (610, 1));
                break;
            case PetActivity.LookAround:
                Animate(rotate, RotateTransform.AngleProperty,
                    (0, 0), (230, 7), (590, 0));
                break;
            case PetActivity.Land:
                Animate(scale, ScaleTransform.ScaleYProperty,
                    (0, 1), (130, .78), (390, 1));
                break;
            case PetActivity.Wake:
                Animate(translate, TranslateTransform.YProperty,
                    (0, 0), (280, -4), (610, 0));
                break;
            case PetActivity.Celebrate:
                Animate(translate, TranslateTransform.YProperty,
                    (0, 0), (190, -5), (390, 0), (590, -5), (850, 0));
                break;
        }
    }

    public void ReactSnack(PetSnack snack, int durationMs)
    {
        const int baseDuration = 1630;
        int At(int time) => (int)Math.Round(time * durationMs / (double)baseDuration);
        var (faces, motion) = snack switch
        {
            PetSnack.Cookie => (
                new[] { (0, Mood.Curious), (At(550), Mood.Happy) },
                new[] { (0, 0d), (At(550), -2d), (At(1200), 0d), (durationMs, 0d) }),
            PetSnack.Strawberry => (
                new[] { (0, Mood.Curious), (At(450), Mood.Blink), (At(700), Mood.Happy) },
                new[] { (0, 0d), (At(450), 0d), (At(850), -3d), (At(1350), 0d), (durationMs, 0d) }),
            PetSnack.Cake => (
                new[] { (0, Mood.Happy), (At(900), Mood.Blink), (At(1100), Mood.Happy) },
                new[] { (0, 0d), (At(280), -2d), (At(700), -2d), (At(1500), 0d), (durationMs, 0d) }),
            PetSnack.Candy => (
                new[] { (0, Mood.Curious), (At(350), Mood.Happy) },
                new[] { (0, 0d), (At(850), -2d), (At(1250), 0d), (durationMs, 0d) }),
            PetSnack.CottonCandy => (
                new[] { (0, Mood.Thinking), (At(600), Mood.Blink), (At(950), Mood.Happy) },
                new[] { (0, 0d), (At(600), 0d), (At(1100), -2d), (At(1500), 0d), (durationMs, 0d) }),
            _ => throw new ArgumentOutOfRangeException(nameof(snack))
        };
        StartReaction(durationMs, faces, motion);
    }

    public void ReactDance(PetDance dance, int durationMs)
    {
        const int baseDuration = 8000;
        int At(int time) => (int)Math.Round(time * durationMs / (double)baseDuration);
        var (faces, motion) = dance switch
        {
            PetDance.Step => (
                new[] { (0, Mood.Happy), (At(4000), Mood.Blink), (At(4200), Mood.Happy) },
                new[] { (0, 0d), (At(800), -2d), (At(1200), 0d), (At(3000), -2d),
                    (At(3400), 0d), (At(5400), -2d), (At(5800), 0d), (durationMs, 0d) }),
            PetDance.Guofeng => (
                new[] { (0, Mood.Thinking), (At(4000), Mood.Happy) },
                new[] { (0, 0d), (At(1200), 0d), (At(3000), -2d),
                    (At(5200), -2d), (At(7400), 0d), (durationMs, 0d) }),
            PetDance.WingTail => (
                new[] { (0, Mood.Curious), (At(5860), Mood.Happy) },
                new[] { (0, 0d), (At(5600), 0d), (At(6200), -3d),
                    (At(6800), 0d), (durationMs, 0d) }),
            _ => throw new ArgumentOutOfRangeException(nameof(dance))
        };
        StartReaction(durationMs, faces, motion);
    }

    private void StartReaction(int durationMs, (int Milliseconds, Mood Face)[] faces,
        (int Milliseconds, double Y)[] motion)
    {
        if (image.Visibility != Visibility.Visible || durationMs <= 0) return;
        StopMotion();
        SetFace(faces[0].Face);
        if (_reducedMotion) return;
        _reactionFaces = faces;
        _reactionMotion = motion;
        _nextReactionFace = 1;
        _reactionDurationMs = durationMs;
        var revision = _revision;
        var timer = new DispatcherTimer(DispatcherPriority.Normal);
        timer.Tick += (_, _) => AdvanceReaction(timer, revision, _reactionWatch.ElapsedMilliseconds);
        _reactionTimer = timer;
        _reactionWatch.Restart();
        Animate(translate, TranslateTransform.YProperty,
            motion.Select(pose => (pose.Milliseconds, pose.Y)).ToArray());
        ScheduleReaction(timer, 0);
        timer.Start();
    }

    private void AdvanceReaction(DispatcherTimer timer, long revision, long elapsedMs)
    {
        if (!ReferenceEquals(_reactionTimer, timer) || _revision != revision) return;
        if (elapsedMs >= _reactionDurationMs)
        {
            StopMotion();
            if (image.Visibility == Visibility.Visible)
                SetFace(_state == PetState.Hover ? Mood.Curious : Mood.Normal);
            return;
        }
        while (_nextReactionFace < _reactionFaces.Length
            && elapsedMs >= _reactionFaces[_nextReactionFace].Milliseconds)
            SetFace(_reactionFaces[_nextReactionFace++].Face);
        ScheduleReaction(timer, elapsedMs);
    }

    private void ScheduleReaction(DispatcherTimer timer, long elapsedMs)
    {
        var next = _nextReactionFace < _reactionFaces.Length
            ? _reactionFaces[_nextReactionFace].Milliseconds : _reactionDurationMs;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1,
            next - Math.Max(elapsedMs, _reactionWatch.ElapsedMilliseconds)));
    }

    public void Tap()
    {
        if (image.Visibility != Visibility.Visible
            || _state is not (PetState.Idle or PetState.Hover or PetState.Happy)) return;
        StopMotion();
        var revision = _revision;
        SetFace(Mood.Happy);
        if (!_reducedMotion)
            Animate(translate, TranslateTransform.YProperty,
                (0, 0), (240, -3), (700, 0));
        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        timer.Tick += (_, _) => EndTap(timer, revision);
        _tapTimer = timer;
        timer.Start();
    }

    private void EndTap(DispatcherTimer timer, long revision)
    {
        timer.Stop();
        if (!ReferenceEquals(_tapTimer, timer) || _revision != revision) return;
        StopMotion();
        if (image.Visibility == Visibility.Visible
            && _state is PetState.Idle or PetState.Hover or PetState.Happy)
            SetFace(_state == PetState.Hover ? Mood.Curious : Mood.Normal);
    }

    public void Tick(DateTimeOffset now, bool focusing)
    {
        if (_tapTimer?.IsEnabled == true || _reducedMotion || image.Visibility != Visibility.Visible
            || _state is not (PetState.Idle or PetState.Hover)
            || now < _nextAmbient) return;
        _nextAmbient = now.AddSeconds(focusing ? Random.Shared.Next(13, 19)
            : Random.Shared.Next(7, 12));
        StopMotion();
        SetFace(Mood.Blink);
        _blinkTimer.Start();
        // A real closed-eye frame and one vertical hover; no pet sway.
        Animate(translate, TranslateTransform.YProperty,
            (0, 0), (280, -2), (760, 0));
    }

    public void Suspend()
    {
        StopMotion();
        image.Visibility = Visibility.Collapsed;
    }

    private void Add(Mood mood, string directory, string fileName,
        BitmapSource fallback, Int32Rect fallbackCrop)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(Path.Combine(directory, fileName)));
            bitmap.EndInit();
            if (bitmap.PixelWidth != 1254 || bitmap.PixelHeight != 1254)
                throw new InvalidDataException("The companion sprite canvas changed.");
            bitmap.Freeze();
            var sprite = new CroppedBitmap(bitmap, SpriteCrop);
            sprite.Freeze();
            _faces[mood] = sprite;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or InvalidDataException)
        {
            if (fallback.PixelWidth < fallbackCrop.X + fallbackCrop.Width
                || fallback.PixelHeight < fallbackCrop.Y + fallbackCrop.Height) return;
            var face = new CroppedBitmap(fallback, fallbackCrop);
            face.Freeze();
            _faces[mood] = face;
        }
    }

    private BitmapSource? Face(Mood mood) =>
        _faces.GetValueOrDefault(mood) ?? _faces.GetValueOrDefault(Mood.Normal);

    private void SetFace(Mood mood)
    {
        image.Source = Face(mood);
    }

    private void EndBlink(object? sender, EventArgs e)
    {
        _blinkTimer.Stop();
        if (_tapTimer?.IsEnabled == true) return;
        if (_state is (PetState.Idle or PetState.Hover) && image.Visibility == Visibility.Visible)
            SetFace(_state == PetState.Hover ? Mood.Curious : Mood.Normal);
    }

    private void StopMotion()
    {
        _revision++;
        _blinkTimer.Stop();
        _tapTimer?.Stop();
        _tapTimer = null;
        _reactionTimer?.Stop();
        _reactionTimer = null;
        _reactionWatch.Reset();
        _reactionFaces = [];
        _reactionMotion = [];
        _nextReactionFace = 0;
        _reactionDurationMs = 0;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
        rotate.Angle = translate.X = translate.Y = 0;
    }

    private static void Animate(Animatable target, DependencyProperty property,
        params (int Milliseconds, double Value)[] poses)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(poses[^1].Milliseconds),
            FillBehavior = FillBehavior.Stop
        };
        foreach (var (milliseconds, value) in poses)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        Timeline.SetDesiredFrameRate(animation, 30);
        target.BeginAnimation(property, animation);
    }
}
