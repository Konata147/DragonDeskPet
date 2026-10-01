using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DragonDeskPet.Core;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using Size = System.Windows.Size;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;

namespace DragonDeskPet;

public partial class MainWindow
{
    private readonly PetStrokeRecognizer _strokeRecognizer = new();
    private NativePoint? _lastStrokeCursor;
    private bool _feeding;
    private bool _draggingTreat;
    private PetSnack _selectedSnack = PetSnack.Cookie;
    private enum PetChoiceKind { Snacks, Dances }
    private PetChoiceKind? _choiceKind;
    private readonly DispatcherTimer _propHideTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private bool CanShowPetProps => IsVisible && !_closing && !_isFullscreenActive && !_settingsOpen
        && !_isBusy && !_feeding && !_mouseDown && !_dragged && _activePetActivity is null
        && _stateMachine.Current is PetState.Idle or PetState.Hover
        && QuickBar.Visibility != Visibility.Visible && ChatBubble.Visibility != Visibility.Visible
        && ProductivityPanel.Visibility != Visibility.Visible && ReminderAlertCard.Visibility != Visibility.Visible
        && OnboardingBubble.Visibility != Visibility.Visible
        && !((ContextMenu)FindResource("PetMenu")).IsOpen
        && !((ContextMenu)FindResource("InteractionMenu")).IsOpen;

    private void PositionPetProps()
    {
        if (CharacterHost.RenderSize.Width <= 0 || RootSurface.ActualWidth <= 0 || RootSurface.ActualHeight <= 0) return;
        var bounds = CharacterHost.TransformToAncestor(RootSurface)
            .TransformBounds(new Rect(new Point(0, 0), CharacterHost.RenderSize));
        Canvas.SetLeft(PetPropBar, Math.Clamp(bounds.Left - 28, 4,
            Math.Max(4, RootSurface.ActualWidth - PetPropBar.Width - 4)));
        Canvas.SetTop(PetPropBar, Math.Clamp(bounds.Top + bounds.Height * .45 - PetPropBar.Height / 2,
            4, Math.Max(4, RootSurface.ActualHeight - PetPropBar.Height - 4)));
    }

    private void ShowPetProps()
    {
        if (!CanShowPetProps) { HidePetProps(); return; }
        _propHideTimer.Stop();
        PositionPetProps();
        PetPropLayer.Visibility = Visibility.Visible;
    }

    private void SchedulePetPropsHide()
    {
        if (PetPropLayer.Visibility == Visibility.Visible && !_propHideTimer.IsEnabled)
        {
            _propHideTimer.Interval = TimeSpan.FromMilliseconds(350);
            _propHideTimer.Start();
        }
    }

    private void HidePetProps()
    {
        _propHideTimer.Stop();
        PetChoicePanel.Visibility = Visibility.Collapsed;
        _choiceKind = null;
        PetPropLayer.Visibility = Visibility.Collapsed;
    }

    private void PetPropBar_MouseEnter(object sender, MouseEventArgs e) => _propHideTimer.Stop();

    private void PetPropBar_MouseLeave(object sender, MouseEventArgs e) => SchedulePetPropsHide();

    private void PetPropSnack_MouseEnter(object sender, MouseEventArgs e) => ShowPetChoices(PetChoiceKind.Snacks);
    private void PetPropDance_MouseEnter(object sender, MouseEventArgs e) => ShowPetChoices(PetChoiceKind.Dances);
    private void PetPropHop_MouseEnter(object sender, MouseEventArgs e)
    {
        _propHideTimer.Stop();
        PetChoicePanel.Visibility = Visibility.Collapsed;
        _choiceKind = null;
    }
    private void PetChoicePanel_MouseEnter(object sender, MouseEventArgs e) => _propHideTimer.Stop();
    private void PetChoicePanel_MouseLeave(object sender, MouseEventArgs e) => SchedulePetPropsHide();

    private void ShowPetChoicesFromMenu(PetChoiceKind kind)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!CanShowPetProps) return;
            ShowPetProps();
            ShowPetChoices(kind);
            // The pointer starts on the context menu, not on the pet props.
            _propHideTimer.Interval = TimeSpan.FromMilliseconds(2200);
            _propHideTimer.Start();
        }, DispatcherPriority.ContextIdle);
    }

    private void ShowPetChoices(PetChoiceKind kind)
    {
        if (!CanShowPetProps) return;
        _propHideTimer.Stop();
        _propHideTimer.Interval = TimeSpan.FromMilliseconds(350);
        ConfigurePetChoices(kind);
        PetChoicePanel.Visibility = Visibility.Visible;
        PositionPetChoices(kind);
    }

    private void ConfigurePetChoices(PetChoiceKind kind)
    {
        if (_choiceKind != kind)
        {
            PetChoiceItems.Children.Clear();
            _choiceKind = kind;
            PetChoicePanel.Width = kind == PetChoiceKind.Snacks ? 194 : 140;
            PetChoicePanel.Height = kind == PetChoiceKind.Snacks ? 64 : 130;
            PetChoiceTitle.Text = kind == PetChoiceKind.Snacks ? "选一种点心" : "选一支舞";
            PetChoiceItems.Orientation = kind == PetChoiceKind.Snacks ? Orientation.Horizontal : Orientation.Vertical;
            if (kind == PetChoiceKind.Snacks)
            {
                foreach (var snack in PetInteractionVariants.Snacks)
                {
                    var button = ChoiceButton(PetChoiceIcons.Snack(snack, 23), PetInteractionVariants.Name(snack), 34);
                    button.Tag = snack;
                    button.PreviewMouseLeftButtonDown += PetSnackChoice_PreviewMouseLeftButtonDown;
                    PetChoiceItems.Children.Add(button);
                }
            }
            else
            {
                foreach (var dance in PetInteractionVariants.Dances)
                {
                    var content = new StackPanel { Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                    content.Children.Add(PetChoiceIcons.Dance(dance, 19));
                    content.Children.Add(new TextBlock { Text = PetInteractionVariants.Name(dance),
                        Margin = new Thickness(6, 0, 0, 0), FontFamily = new FontFamily("Microsoft YaHei UI"),
                        FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(98, 76, 130)),
                        VerticalAlignment = VerticalAlignment.Center });
                    var button = ChoiceButton(content, PetInteractionVariants.Name(dance), 124);
                    button.Tag = dance;
                    button.Click += PetDanceChoice_Click;
                    PetChoiceItems.Children.Add(button);
                }
            }
        }
    }

    private void PositionPetChoices(PetChoiceKind kind)
    {
        PetChoicePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = PetChoicePanel.Width;
        var height = PetChoicePanel.Height;
        var propsLeft = Canvas.GetLeft(PetPropBar);
        var propsTop = Canvas.GetTop(PetPropBar);
        Canvas.SetLeft(PetChoicePanel, Math.Clamp(propsLeft - width - 5, 4, Math.Max(4, RootSurface.ActualWidth - width - 4)));
        Canvas.SetTop(PetChoicePanel, Math.Clamp(propsTop + (kind == PetChoiceKind.Snacks ? 0 : 20),
            4, Math.Max(4, RootSurface.ActualHeight - height - 4)));
    }

    private Button ChoiceButton(FrameworkElement content, string tooltip, double width) => new()
    {
        Width = width, Height = 31, Margin = new Thickness(1),
        Content = content, ToolTip = tooltip,
        Style = (Style)FindResource("PetChoiceButtonStyle")
    };

    private void PetSnackChoice_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: PetSnack snack }) BeginTreatDrag(snack, e.GetPosition(TreatLayer));
    }

    private void PetDanceChoice_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: PetDance dance } || !CanShowPetProps) return;
        HidePetProps();
        PlayPetActivity(PetActivity.Dance, dance: dance);
    }

    private void BeginTreatDrag(PetSnack snack, Point position)
    {
        if (!CanShowPetProps) { HidePetProps(); return; }
        HidePetProps();
        ShowTreat(snack);
        if (!_feeding) return;
        SetTreatPosition(position);
        _draggingTreat = TreatToken.CaptureMouse();
        if (!_draggingTreat)
        {
            CancelTreat();
            ApplyStateVisual(_stateMachine.Current);
        }
    }

    private void PetPropSnack_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        BeginTreatDrag(PetSnack.Cookie, e.GetPosition(TreatLayer));
    }

    private void PetPropDance_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!CanShowPetProps) { HidePetProps(); return; }
        HidePetProps();
        PlayPetActivity(PetActivity.Dance, dance: PetDance.Step);
    }

    private void PetPropHop_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!CanShowPetProps) { HidePetProps(); return; }
        HidePetProps();
        PlayPetActivity(PetActivity.Hop);
    }

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

    private void ShowTreat(PetSnack snack = PetSnack.Cookie)
    {
        if (!PetCanInteract) return;
        HidePetProps();
        HideQuickBar();
        _animationPlayer.Stop();
        if (_stateMachine.Current == PetState.Sleeping) _restingByChoice = false;
        if (_stateMachine.Current is not (PetState.Idle or PetState.Hover))
            _stateMachine.TransitionTo(PetState.Idle);
        ApplyStateVisual(_stateMachine.Current);
        _selectedSnack = snack;
        _feeding = true;
        SetTreatVisual(snack);
        TreatLayer.Visibility = Visibility.Visible;
        var mouth = MouthPoint();
        Canvas.SetLeft(TreatTarget, mouth.X - 20); Canvas.SetTop(TreatTarget, mouth.Y - 20);
        SetTreatPosition(new Point(mouth.X - 66, mouth.Y + 38));
        StateText.Text = $"把{PetInteractionVariants.Name(snack)}拖到嘴边～";
        TreatToken.Focus();
    }

    private void SetTreatVisual(PetSnack snack)
    {
        TreatToken.Background = new SolidColorBrush(snack switch
        {
            PetSnack.Strawberry => Color.FromRgb(255, 223, 229),
            PetSnack.Cake => Color.FromRgb(255, 239, 219),
            PetSnack.Candy => Color.FromRgb(239, 224, 255),
            PetSnack.CottonCandy => Color.FromRgb(255, 226, 244),
            _ => Color.FromRgb(255, 248, 236)
        });
        TreatToken.Child = PetChoiceIcons.Snack(snack, 25);
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
        var snack = _selectedSnack;
        CancelTreat();
        ApplyStateVisual(_stateMachine.Current);
        if (accepted) PlayPetActivity(PetActivity.Feed, snack: snack);
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
        _selectedSnack = PetSnack.Cookie;
        if (Mouse.Captured == TreatToken) TreatToken.ReleaseMouseCapture();
        TreatLayer.Visibility = Visibility.Collapsed;
    }
}
