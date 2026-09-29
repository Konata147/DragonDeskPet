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

    public void SetState(PetState state, bool reducedMotion, bool hasActionArtwork)
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
        if (state == PetState.Hover && !reducedMotion)
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

    public void Tap()
    {
        if (image.Visibility != Visibility.Visible || _state is not (PetState.Idle or PetState.Hover)) return;
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
        if (image.Visibility == Visibility.Visible && _state is PetState.Idle or PetState.Hover)
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
