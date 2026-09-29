using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DragonDeskPet.Core;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DragonDeskPet;

public partial class MainWindow
{
    private readonly PetStrokeRecognizer _strokeRecognizer = new();
    private NativePoint? _lastStrokeCursor;
    private bool _feeding;
    private bool _draggingTreat;

    private bool TryGetArtworkRect(out Rect rect)
    {
        rect = Rect.Empty;
        if (CharacterImage.Source is not BitmapSource image || CharacterImage.ActualWidth <= 0) return false;
        var fit = Math.Min(CharacterImage.ActualWidth / image.PixelWidth, CharacterImage.ActualHeight / image.PixelHeight);
        rect = new Rect((CharacterImage.ActualWidth - image.PixelWidth * fit) / 2,
            (CharacterImage.ActualHeight - image.PixelHeight * fit) / 2, image.PixelWidth * fit, image.PixelHeight * fit);
        return rect.Width > 0 && rect.Height > 0;
    }

    private void ObserveHeadStroke(MouseEventArgs e)
    {
        if (!GetCursorPos(out var cursor)) return;
        if (_lastStrokeCursor is { } previous && previous.X == cursor.X && previous.Y == cursor.Y) return;
        _lastStrokeCursor = cursor;
        if (!TryGetArtworkRect(out var art)) return;
        var point = e.GetPosition(CharacterImage);
        var x = (point.X - art.X) / art.Width;
        var y = (point.Y - art.Y) / art.Height;
        var eligible = PetCanInteract && !_feeding && !_animationPlayer.IsPlaying
            && _stateMachine.Current is PetState.Idle or PetState.Hover
            && e.LeftButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released;
        var inside = x is >= .28 and <= .72 && y is >= .16 and <= .38 && IsCharacterPixelHit(point);
        if (_strokeRecognizer.Observe(x * 168, inside, eligible, Environment.TickCount64))
            PlayPetActivity(PetActivity.Pet);
    }

    private Point MouthPoint()
    {
        if (!TryGetArtworkRect(out var art)) return new Point(Width - 214, Height - 164);
        return CharacterImage.TranslatePoint(new Point(art.X + art.Width * .50, art.Y + art.Height * .43), TreatLayer);
    }

    private void ShowTreat()
    {
        if (!PetCanInteract) return;
        HideQuickBar();
        _animationPlayer.Stop();
        if (_stateMachine.Current == PetState.Sleeping) _restingByChoice = false;
        if (_stateMachine.Current is not (PetState.Idle or PetState.Hover))
            _stateMachine.TransitionTo(PetState.Idle);
        ApplyStateVisual(_stateMachine.Current);
        _feeding = true;
        TreatLayer.Visibility = Visibility.Visible;
        var mouth = MouthPoint();
        Canvas.SetLeft(TreatTarget, mouth.X - 20); Canvas.SetTop(TreatTarget, mouth.Y - 20);
        SetTreatPosition(new Point(mouth.X - 66, mouth.Y + 38));
        StateText.Text = "把点心拖到嘴边～";
        TreatToken.Focus();
    }

    private void SetTreatPosition(Point point)
    {
        Canvas.SetLeft(TreatToken, Math.Clamp(point.X - 18, 0, Math.Max(0, ActualWidth - 36)));
        Canvas.SetTop(TreatToken, Math.Clamp(point.Y - 18, 0, Math.Max(0, ActualHeight - 36)));
    }

    private void Treat_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_feeding || !PetCanInteract) { CancelTreat(); return; }
        _draggingTreat = TreatToken.CaptureMouse();
    }

    private void Treat_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingTreat) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelTreat(); return; }
        SetTreatPosition(e.GetPosition(TreatLayer));
        e.Handled = true;
    }

    private void Treat_MouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var point = e.GetPosition(TreatLayer);
        var accepted = PetTreatDrop.IsAccepted(_feeding, _draggingTreat, PetCanInteract,
            (point - MouthPoint()).Length, _app.Settings.Scale);
        CancelTreat();
        ApplyStateVisual(_stateMachine.Current);
        if (accepted) PlayPetActivity(PetActivity.Feed);
    }

    private void Treat_LostCapture(object sender, MouseEventArgs e)
    {
        if (_draggingTreat) CancelTreat();
    }

    private void Interaction_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_feeding) return;
        CancelTreat(); ApplyStateVisual(_stateMachine.Current); e.Handled = true;
    }

    private void CancelTreat()
    {
        _feeding = false; _draggingTreat = false;
        if (Mouse.Captured == TreatToken) TreatToken.ReleaseMouseCapture();
        TreatLayer.Visibility = Visibility.Collapsed;
    }
}
