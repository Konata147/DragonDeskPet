using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DragonDeskPet;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly PetStateMachine _stateMachine = new();
    private readonly Dictionary<PetState, BitmapSource> _stateImages = new();
    private readonly DispatcherTimer _inactivityTimer;
    private readonly DispatcherTimer _positionSaveTimer;
    private readonly Queue<DateTimeOffset> _recentClicks = new();
    private DateTimeOffset _lastInteraction = DateTimeOffset.Now;
    private Point _mouseDownPoint;
    private bool _mouseDown;
    private bool _dragged;
    private int _mouseDownClickCount;
    private bool _loaded;

    public bool AllowClose { get; set; }

    private MenuItem TopmostMenuItem => ((ContextMenu)FindResource("PetMenu"))
        .Items.OfType<MenuItem>()
        .First(item => Equals(item.Tag, "Topmost"));

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();

        if (File.Exists(AssetService.IconPath))
        {
            try
            {
                Icon = BitmapFrame.Create(new Uri(AssetService.IconPath, UriKind.Absolute));
            }
            catch
            {
                // The application remains usable if a custom icon is damaged.
            }
        }

        _stateMachine.StateChanged += (_, state) => ApplyStateVisual(state);
        _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _inactivityTimer.Tick += (_, _) =>
        {
            if (DateTimeOffset.Now - _lastInteraction >= TimeSpan.FromMinutes(5)
                && _stateMachine.Current == PetState.Idle)
            {
                _stateMachine.TransitionTo(PetState.Sleeping);
            }
        };
        _positionSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _positionSaveTimer.Tick += (_, _) =>
        {
            _positionSaveTimer.Stop();
            SavePosition();
        };

        Loaded += MainWindow_Loaded;
        _inactivityTimer.Start();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Topmost = _app.Settings.AlwaysOnTop;
        TopmostMenuItem.IsChecked = Topmost;
        ApplyScale(_app.Settings.Scale, save: false);
        LoadCharacterAssets();
        RestorePosition();
        _loaded = true;
        if (!_app.Settings.HasCompletedOnboarding)
        {
            OnboardingBubble.Visibility = Visibility.Visible;
        }
    }

    private void LoadCharacterAssets()
    {
        if (!File.Exists(AssetService.CharacterPath))
        {
            return;
        }

        try
        {
            var idle = LoadBitmap(AssetService.CharacterPath);
            _stateImages[PetState.Idle] = idle;
            foreach (var state in Enum.GetValues<PetState>())
            {
                if (state != PetState.Idle)
                {
                    _stateImages[state] = AssetService.LoadCharacterAsset(state, LoadBitmap);
                }
            }

            CharacterImage.Source = idle;
            CharacterImage.Visibility = Visibility.Visible;
            PlaceholderCharacter.Visibility = Visibility.Collapsed;
        }
        catch
        {
            CharacterImage.Visibility = Visibility.Collapsed;
            PlaceholderCharacter.Visibility = Visibility.Visible;
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void RestorePosition()
    {
        var area = SystemParameters.WorkArea;
        var left = _app.Settings.Left;
        var top = _app.Settings.Top;
        if (left is not null && top is not null
            && left >= SystemParameters.VirtualScreenLeft - Width + 80
            && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
            && top >= SystemParameters.VirtualScreenTop - Height + 80
            && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
        {
            Left = left.Value;
            Top = top.Value;
            return;
        }

        Left = area.Right - Width - 24;
        Top = area.Bottom - Height - 18;
    }

    private void MarkInteraction()
    {
        _lastInteraction = DateTimeOffset.Now;
        if (_stateMachine.Current == PetState.Sleeping)
        {
            _stateMachine.TransitionTo(PetState.Idle);
        }
    }

    private void CharacterHost_MouseEnter(object sender, MouseEventArgs e)
    {
        MarkInteraction();
        if (_stateMachine.Current is PetState.Idle or PetState.Sleeping)
        {
            _stateMachine.TransitionTo(PetState.Hover);
        }
    }

    private async void CharacterHost_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            && (_dragged || _stateMachine.Current == PetState.Dragged))
        {
            await CompleteDragAsync();
            return;
        }

        if (!_mouseDown && _stateMachine.Current == PetState.Hover)
        {
            _stateMachine.TransitionTo(PetState.Idle);
        }
    }

    private async void CharacterHost_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            return;
        }

        if (_dragged || _stateMachine.Current == PetState.Dragged)
        {
            await CompleteDragAsync();
            return;
        }

        _mouseDown = false;
        _dragged = false;
        if (!CharacterHost.IsMouseOver && _stateMachine.Current == PetState.Hover)
        {
            _stateMachine.TransitionTo(PetState.Idle);
        }
    }

    private void CharacterHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        MarkInteraction();
        _mouseDown = true;
        _dragged = false;
        _mouseDownPoint = e.GetPosition(this);
        _mouseDownClickCount = e.ClickCount;
        CharacterHost.CaptureMouse();
        e.Handled = true;
    }

    private async void CharacterHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || e.LeftButton != MouseButtonState.Pressed || _dragged)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _mouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - _mouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragged = true;
        CharacterHost.ReleaseMouseCapture();
        _stateMachine.TransitionTo(PetState.Dragged);
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The pointer may have been released between move messages.
        }
        finally
        {
            await CompleteDragAsync();
        }
    }

    private async void CharacterHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown && !_dragged)
        {
            e.Handled = true;
            return;
        }

        MarkInteraction();

        if (_dragged)
        {
            await CompleteDragAsync();
            e.Handled = true;
            return;
        }

        CharacterHost.ReleaseMouseCapture();
        _mouseDown = false;

        if (_mouseDownClickCount >= 2)
        {
            ToggleChat();
            _stateMachine.TransitionTo(PetState.Happy);
            await ReturnToIdleAsync();
            return;
        }

        RegisterClick();
        QuickBar.Visibility = QuickBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task CompleteDragAsync()
    {
        var wasDragging = _dragged || _stateMachine.Current == PetState.Dragged;
        _mouseDown = false;
        _dragged = false;
        if (Mouse.Captured == CharacterHost)
        {
            CharacterHost.ReleaseMouseCapture();
        }

        if (!wasDragging)
        {
            return;
        }

        MarkInteraction();
        SavePosition();
        _stateMachine.TransitionTo(PetState.Happy);
        await ReturnToIdleAsync();
    }

    private void RegisterClick()
    {
        var now = DateTimeOffset.Now;
        _recentClicks.Enqueue(now);
        while (_recentClicks.Count > 0 && now - _recentClicks.Peek() > TimeSpan.FromSeconds(2.2))
        {
            _recentClicks.Dequeue();
        }

        if (_recentClicks.Count >= 5)
        {
            _recentClicks.Clear();
            _stateMachine.TransitionTo(PetState.Angry);
            _ = ReturnToIdleAsync(1400);
        }
    }

    private void CharacterHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        MarkInteraction();
        var delta = e.Delta > 0 ? 0.1 : -0.1;
        ApplyScale(_app.Settings.Scale + delta, save: true);
        e.Handled = true;
    }

    private void CharacterHost_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        MarkInteraction();
        _stateMachine.TransitionTo(PetState.Hover);
    }

    private void ApplyScale(double scale, bool save)
    {
        scale = Math.Round(Math.Clamp(scale, 0.6, 2.0), 1);
        PetUserScaleTransform.ScaleX = scale;
        PetUserScaleTransform.ScaleY = scale;
        _app.Settings.Scale = scale;
        if (save)
        {
            _app.SettingsService.Save(_app.Settings);
        }
    }

    private void ApplyStateVisual(PetState state)
    {
        StopStateAnimations();
        if (_stateImages.TryGetValue(state, out var stateImage))
        {
            CharacterImage.Source = stateImage;
        }
        StateText.Text = state switch
        {
            PetState.Idle => "待机",
            PetState.Hover => "嗯？",
            PetState.Dragged => "被拎起来了…",
            PetState.Thinking => "思考中…",
            PetState.Happy => "开心",
            PetState.Angry => "不要一直戳啦",
            PetState.Sleeping => "Zzz…",
            _ => state.ToString()
        };

        CharacterHost.Opacity = state == PetState.Sleeping ? 0.78 : 1.0;
        switch (state)
        {
            case PetState.Idle:
                PetTranslateTransform.BeginAnimation(
                    System.Windows.Media.TranslateTransform.YProperty,
                    new DoubleAnimation(0, -2.5, TimeSpan.FromSeconds(1.6))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase()
                    });
                break;
            case PetState.Hover:
                AnimateScale(1.04, 180);
                break;
            case PetState.Dragged:
                PetRotateTransform.Angle = -5;
                PetTranslateTransform.Y = 7;
                AnimateScale(0.96, 120);
                break;
            case PetState.Thinking:
                PetRotateTransform.BeginAnimation(
                    System.Windows.Media.RotateTransform.AngleProperty,
                    new DoubleAnimation(-2.5, 2.5, TimeSpan.FromMilliseconds(260))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    });
                break;
            case PetState.Happy:
                PetTranslateTransform.BeginAnimation(
                    System.Windows.Media.TranslateTransform.YProperty,
                    new DoubleAnimation(0, -11, TimeSpan.FromMilliseconds(180))
                    {
                        AutoReverse = true,
                        RepeatBehavior = new RepeatBehavior(2)
                    });
                break;
            case PetState.Angry:
                PetTranslateTransform.BeginAnimation(
                    System.Windows.Media.TranslateTransform.XProperty,
                    new DoubleAnimation(-5, 5, TimeSpan.FromMilliseconds(70))
                    {
                        AutoReverse = true,
                        RepeatBehavior = new RepeatBehavior(5)
                    });
                break;
            case PetState.Sleeping:
                PetRotateTransform.Angle = 4;
                PetTranslateTransform.Y = 7;
                AnimateScale(0.96, 300);
                break;
        }
    }

    private void StopStateAnimations()
    {
        PetStateScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        PetStateScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        PetRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        PetTranslateTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        PetTranslateTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        PetStateScaleTransform.ScaleX = 1;
        PetStateScaleTransform.ScaleY = 1;
        PetRotateTransform.Angle = 0;
        PetTranslateTransform.X = 0;
        PetTranslateTransform.Y = 0;
    }

    private void AnimateScale(double target, int milliseconds)
    {
        var animation = new DoubleAnimation(1, target, TimeSpan.FromMilliseconds(milliseconds))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        PetStateScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animation);
        PetStateScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, animation);
    }

    private async Task ReturnToIdleAsync(int delayMilliseconds = 900)
    {
        await Task.Delay(delayMilliseconds);
        if (_stateMachine.Current is PetState.Happy or PetState.Angry or PetState.Dragged)
        {
            _stateMachine.TransitionTo(PetState.Idle);
        }
    }

    private void ToggleChat()
    {
        ChatBubble.Visibility = ChatBubble.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        QuickBar.Visibility = Visibility.Collapsed;
        if (ChatBubble.Visibility == Visibility.Visible)
        {
            PromptBox.Focus();
        }
    }

    private void CloseChat_Click(object sender, RoutedEventArgs e) => ChatBubble.Visibility = Visibility.Collapsed;

    private void OnboardingDone_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.HasCompletedOnboarding = true;
        _app.SettingsService.Save(_app.Settings);
        OnboardingBubble.Visibility = Visibility.Collapsed;
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendPromptAsync();

    private async void PromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendPromptAsync();
        }
    }

    private async Task SendPromptAsync()
    {
        var prompt = PromptBox.Text.Trim();
        if (prompt.Length == 0 || !SendButton.IsEnabled)
        {
            return;
        }

        MarkInteraction();
        PromptBox.Clear();
        SendButton.IsEnabled = false;
        ResponseText.Text = "让我想想……";
        _stateMachine.TransitionTo(PetState.Thinking);

        try
        {
            var provider = _app.AiProviderFactory.Create(_app.Settings);
            ResponseText.Text = await provider.SendAsync(prompt);
            _stateMachine.TransitionTo(PetState.Happy);
            await ReturnToIdleAsync(1300);
        }
        catch (OperationCanceledException)
        {
            ResponseText.Text = "这次请求已取消。";
            _stateMachine.TransitionTo(PetState.Idle);
        }
        catch (Exception ex)
        {
            ResponseText.Text = $"连接没有成功：{ex.Message}";
            _stateMachine.TransitionTo(PetState.Angry);
            await ReturnToIdleAsync(1600);
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    public void OpenSettings()
    {
        MarkInteraction();
        var window = new SettingsWindow(_app.Settings, saved =>
        {
            _app.Settings = saved;
            Topmost = saved.AlwaysOnTop;
            TopmostMenuItem.IsChecked = Topmost;
            ApplyScale(saved.Scale, save: false);
            _app.SettingsService.Save(saved);
            StartupService.SetEnabled(saved.StartWithWindows);
        })
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void ChatMenuItem_Click(object sender, RoutedEventArgs e) => ToggleChat();

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void HideMenuItem_Click(object sender, RoutedEventArgs e) => Hide();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => _app.ExitApplication();

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        TopmostMenuItem.IsChecked = Topmost;
        _app.Settings.AlwaysOnTop = Topmost;
        _app.SettingsService.Save(_app.Settings);
    }

    private void PetMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (_stateMachine.Current == PetState.Hover)
        {
            _stateMachine.TransitionTo(PetState.Idle);
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_loaded)
        {
            _positionSaveTimer.Stop();
            _positionSaveTimer.Start();
        }
    }

    private void SavePosition()
    {
        _app.Settings.Left = Left;
        _app.Settings.Top = Top;
        _app.SettingsService.Save(_app.Settings);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SavePosition();
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
