using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

/// <summary>Fallback only. Never substitutes rocking for missing character animation.</summary>
public sealed class PetAnimator(ScaleTransform scale, RotateTransform rotate,
    TranslateTransform translate, TextBlock accent)
{
    public void Stop()
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        accent.BeginAnimation(UIElement.OpacityProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
        rotate.Angle = translate.X = translate.Y = 0;
        accent.Text = string.Empty;
        accent.Opacity = 1;
    }

    public void ShowState(PetState state, bool reduceMotion, bool focusing)
    {
        Stop();
        accent.Text = state switch { PetState.Thinking => "· · ·", PetState.Sleeping => "z Z", _ => "" };
    }

    public void Play(PetActivity activity, bool reduceMotion)
    {
        Stop();
        accent.Text = PetActivities.Describe(activity).Accent;
        if (reduceMotion) return;
        if (activity == PetActivity.Hop)
            Track(translate, TranslateTransform.YProperty, 1100, 0, 0, -32, -38, -5, 0);
        else if (activity == PetActivity.Land)
            Track(scale, ScaleTransform.ScaleYProperty, 550, .96, .98, 1);
    }

    public void PlayHopMotion(double userScale)
    {
        var height = 48 * Math.Clamp(userScale, .6, 2);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(1800),
            FillBehavior = FillBehavior.Stop
        };
        foreach (var (time, value) in new[]
        {
            (0, 0d), (220, 0d), (420, -7d), (600, -height),
            (810, -height), (1060, -height * .62), (1260, -5d),
            (1450, 0d), (1800, 0d)
        })
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(time)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        Timeline.SetDesiredFrameRate(animation, 60);
        translate.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private static void Track(Animatable target, DependencyProperty property, int milliseconds, params double[] values)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(milliseconds), FillBehavior = FillBehavior.Stop
        };
        for (var i = 0; i < values.Length; i++)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(values[i],
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds * i / (double)(values.Length - 1))),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        Timeline.SetDesiredFrameRate(animation, 24);
        target.BeginAnimation(property, animation);
    }
}
