using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DragonDeskPet.AI;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace DragonDeskPet;

public partial class MainWindow : Window
{
    private const double LogicalSurfaceWidth = 430;
    private const double LogicalSurfaceHeight = 310;
    private const double SurfaceLeftPadding = 150;
    private const double SurfaceTopPadding = 150;
    private const double SurfaceRightPadding = 130;
    private const double SurfaceBottomPadding = 50;
    private const double CharacterHalfWidth = 84;
    private const double CharacterScaleOriginY = 187;
    private readonly App _app;
    private readonly PetStateMachine _stateMachine = new();
    private readonly Dictionary<PetState, BitmapSource> _stateImages = new();
    private readonly IFullscreenDetectionService _fullscreenDetectionService = new FullscreenDetectionService();
    private readonly DispatcherTimer _inactivityTimer;
    private readonly DispatcherTimer _positionSaveTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly Queue<DateTimeOffset> _recentClicks = new();
    private DateTimeOffset _lastInteraction = DateTimeOffset.Now;
    private Point _mouseDownPoint;
    private bool _mouseDown;
    private bool _dragged;
    private int _mouseDownClickCount;
    private bool _loaded;
    private bool _isBusy;
    private bool _hiddenForFullscreen;
    private CapturedScreenshot? _pendingScreenshot;

    public bool AllowClose { get; set; }

    private MenuItem TopmostMenuItem => ((ContextMenu)FindResource("PetMenu"))
        .Items.OfType<MenuItem>()
        .First(item => Equals(item.Tag, "Topmost"));

    private MenuItem ScreenshotMenuItem => ((ContextMenu)FindResource("PetMenu"))
        .Items.OfType<MenuItem>()
        .First(item => Equals(item.Tag, "Screenshot"));

    private MenuItem ClipboardMenuItem => ((ContextMenu)FindResource("PetMenu"))
        .Items.OfType<MenuItem>()
        .First(item => Equals(item.Tag, "Clipboard"));

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
        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _fullscreenTimer.Tick += FullscreenTimer_Tick;

        Loaded += MainWindow_Loaded;
        _inactivityTimer.Start();
        _fullscreenTimer.Start();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Topmost = _app.Settings.AlwaysOnTop;
        UpdateTopmostIndicators();
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
            && left >= SystemParameters.VirtualScreenLeft - LogicalSurfaceWidth + 80
            && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
            && top >= SystemParameters.VirtualScreenTop - LogicalSurfaceHeight + 80
            && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
        {
            Left = left.Value - SurfaceLeftPadding;
            Top = top.Value - SurfaceTopPadding;
            return;
        }

        Left = area.Right - LogicalSurfaceWidth - 24 - SurfaceLeftPadding;
        Top = area.Bottom - LogicalSurfaceHeight - 18 - SurfaceTopPadding;
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
        ToggleQuickBar();
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

    private void CharacterHost_DragEnter(object sender, DragEventArgs e) => UpdateImageDragFeedback(e);

    private void CharacterHost_DragOver(object sender, DragEventArgs e) => UpdateImageDragFeedback(e);

    private void UpdateImageDragFeedback(DragEventArgs e)
    {
        if (!_isBusy && HasFileDrop(e))
        {
            e.Effects = DragDropEffects.Copy;
            _stateMachine.TransitionTo(PetState.Hover);
            StateText.Text = TryGetDroppedImagePath(e, out _)
                ? "松开让我看看"
                : "松开检查文件";
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void CharacterHost_DragLeave(object sender, DragEventArgs e)
    {
        if (_stateMachine.Current == PetState.Hover)
        {
            _stateMachine.TransitionTo(PetState.Idle);
        }
    }

    private async void CharacterHost_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_isBusy)
        {
            return;
        }

        if (!TryGetDroppedImagePath(e, out var path))
        {
            ShowChatMessage("请一次拖入一张 PNG、JPG、BMP、GIF 或 TIFF 图片。");
            _stateMachine.TransitionTo(PetState.Angry);
            await ReturnToIdleAsync(1400);
            return;
        }

        if (!EnsureAiConfigured())
        {
            return;
        }

        MarkInteraction();
        SetBusy(true);
        try
        {
            var image = await _app.ImageFileService.LoadAsync(path);
            try
            {
                PresentPendingImage(
                    image,
                    $"拖入图片 · {Path.GetFileName(path)}",
                    "请分析这张图片，告诉我重点内容。",
                    "图片准备好了。确认问题后点击发送，我才会上传它。");
            }
            catch
            {
                image.Dispose();
                throw;
            }

            _stateMachine.TransitionTo(PetState.Happy);
            await ReturnToIdleAsync();
        }
        catch (ScreenshotImageTooLargeException)
        {
            ShowChatMessage("图片转换为 PNG 后超过 8 MB，请选择更小的图片。");
            _stateMachine.TransitionTo(PetState.Angry);
            await ReturnToIdleAsync(1400);
        }
        catch (OperationCanceledException)
        {
            _stateMachine.TransitionTo(PetState.Idle);
        }
        catch (Exception ex)
        {
            ShowChatMessage($"图片无法读取：{ex.Message}");
            _stateMachine.TransitionTo(PetState.Angry);
            await ReturnToIdleAsync(1400);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryGetDroppedImagePath(DragEventArgs e, out string path)
    {
        path = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths)
        {
            return false;
        }

        path = paths[0];
        return _app.ImageFileService.CanLoad(path);
    }

    private static bool HasFileDrop(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop);

    private void ApplyScale(double scale, bool save)
    {
        scale = Math.Round(Math.Clamp(scale, 0.6, 2.0), 1);
        PetUserScaleTransform.ScaleX = scale;
        PetUserScaleTransform.ScaleY = scale;
        UpdateFloatingUiLayout(scale);
        _app.Settings.Scale = scale;
        if (save)
        {
            _app.SettingsService.Save(_app.Settings);
        }
    }

    private void UpdateFloatingUiLayout(double scale)
    {
        var rightMargin = 180 + (CharacterHalfWidth * (scale - 1));
        var quickBarTop = 86 - (CharacterScaleOriginY * (scale - 1)) + (24 * scale);
        var quickBarScale = 0.92 + ((scale - 0.6) * 0.2);

        var outerRightMargin = SurfaceRightPadding + rightMargin;
        QuickBar.Margin = new Thickness(0, SurfaceTopPadding + quickBarTop, outerRightMargin, 0);
        ChatBubble.Margin = new Thickness(0, 18, outerRightMargin, SurfaceBottomPadding + 26);
        OnboardingBubble.Margin = new Thickness(0, 0, outerRightMargin, SurfaceBottomPadding + 26);

        QuickBarScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        QuickBarScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        QuickBarScaleTransform.ScaleX = quickBarScale;
        QuickBarScaleTransform.ScaleY = quickBarScale;
    }

    private void ToggleQuickBar()
    {
        if (QuickBar.Visibility == Visibility.Visible)
        {
            HideQuickBar();
            return;
        }

        ShowQuickBar();
    }

    private void ShowQuickBar()
    {
        QuickMoreButton.IsChecked = false;
        QuickBar.Visibility = Visibility.Visible;
        QuickBar.Opacity = 1;

        QuickBar.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });

        var targetScale = QuickBarScaleTransform.ScaleX;
        var scaleAnimation = new DoubleAnimation(targetScale * 0.9, targetScale, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        QuickBarScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnimation);
        QuickBarScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnimation);
    }

    private void HideQuickBar()
    {
        QuickBar.BeginAnimation(OpacityProperty, null);
        QuickBarScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        QuickBarScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        QuickMorePopup.IsOpen = false;
        QuickMoreButton.IsChecked = false;
        QuickBar.Opacity = 0;
        QuickBar.Visibility = Visibility.Collapsed;
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
        HideQuickBar();
        if (ChatBubble.Visibility == Visibility.Visible)
        {
            PromptBox.Focus();
        }
    }

    private void CloseChat_Click(object sender, RoutedEventArgs e) => ChatBubble.Visibility = Visibility.Collapsed;

    private async void ScreenshotMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HideQuickBar();
        await CaptureScreenshotAsync();
    }

    private async Task CaptureScreenshotAsync()
    {
        if (_isBusy)
        {
            return;
        }

        MarkInteraction();
        if (!EnsureAiConfigured())
        {
            return;
        }

        SetBusy(true);
        HideQuickBar();
        OnboardingBubble.Visibility = Visibility.Collapsed;
        ScreenshotCaptureResult result;
        var restoreWindow = IsVisible;
        try
        {
            if (restoreWindow)
            {
                Hide();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(120);
            }

            result = await _app.ScreenshotCaptureService.CaptureRegionAsync();
        }
        catch (Exception ex)
        {
            result = ScreenshotCaptureResult.Failed($"截图没有完成：{ex.Message}");
        }
        finally
        {
            if (restoreWindow)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            SetBusy(false);
        }

        if (result.Status == ScreenshotCaptureStatus.Canceled)
        {
            return;
        }

        if (result.Status == ScreenshotCaptureStatus.Failed || result.Screenshot is null)
        {
            ShowChatMessage(result.ErrorMessage ?? "截图没有完成，请重试。");
            _stateMachine.TransitionTo(PetState.Angry);
            await ReturnToIdleAsync(1400);
            return;
        }

        try
        {
            PresentPendingImage(
                result.Screenshot,
                "待发送截图",
                "请分析这张截图，告诉我重点内容。",
                "截图准备好了。确认问题后点击发送，我才会把它交给当前 AI。");
        }
        catch
        {
            result.Screenshot.Dispose();
            ShowChatMessage("截图预览无法读取，请重新选择区域。");
            return;
        }

        _stateMachine.TransitionTo(PetState.Happy);
        await ReturnToIdleAsync();
    }

    private async void ClipboardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HideQuickBar();
        await ReadClipboardAsync();
    }

    private async Task ReadClipboardAsync()
    {
        if (_isBusy || !EnsureAiConfigured())
        {
            return;
        }

        MarkInteraction();
        SetBusy(true);
        ClipboardContentResult result;
        try
        {
            result = await _app.ClipboardContentService.ReadAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = new ClipboardContentResult(
                ClipboardContentKind.Failed,
                ErrorMessage: $"剪贴板读取失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }

        switch (result.Kind)
        {
            case ClipboardContentKind.Text:
                ClearPendingScreenshot();
                ChatBubble.Visibility = Visibility.Visible;
                HideQuickBar();
                ResponseText.Text = "剪贴板文字已放入输入框，确认后点击发送。";
                PromptBox.Text = result.Text ?? string.Empty;
                PromptBox.Focus();
                PromptBox.SelectAll();
                break;

            case ClipboardContentKind.Image when result.Image is not null:
                try
                {
                    PresentPendingImage(
                        result.Image,
                        "剪贴板图片",
                        "请分析这张图片，告诉我重点内容。",
                        "剪贴板图片准备好了。确认问题后点击发送，我才会上传它。");
                    _stateMachine.TransitionTo(PetState.Happy);
                    await ReturnToIdleAsync();
                }
                catch
                {
                    result.Image.Dispose();
                    ShowChatMessage("剪贴板图片预览无法读取，请重新复制后再试。");
                }
                break;

            case ClipboardContentKind.Empty:
                ShowChatMessage("剪贴板里没有可用的文字或图片。请先复制内容后再试。");
                break;

            case ClipboardContentKind.Failed:
                ShowChatMessage(result.ErrorMessage ?? "剪贴板内容暂时无法读取。");
                break;
        }
    }

    private void PresentPendingImage(
        CapturedScreenshot screenshot,
        string title,
        string defaultPrompt,
        string readyMessage)
    {
        var preview = LoadBitmap(screenshot.PngBytes);
        ClearPendingScreenshot();
        _pendingScreenshot = screenshot;
        ScreenshotPreviewImage.Source = preview;
        PendingImageTitleText.Text = title;
        ScreenshotDimensionsText.Text = $"{screenshot.Width} × {screenshot.Height} · {FormatByteCount(screenshot.ByteLength)}";
        ScreenshotPreviewPanel.Visibility = Visibility.Visible;
        ChatBubble.Visibility = Visibility.Visible;
        HideQuickBar();
        ResponseText.Text = readyMessage;
        PromptBox.Text = defaultPrompt;
        PromptBox.Focus();
        PromptBox.SelectAll();
    }

    private static BitmapSource LoadBitmap(ReadOnlyMemory<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string FormatByteCount(int bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024d * 1024d):0.0} MB"
            : $"{Math.Max(1, bytes / 1024d):0.#} KB";

    private void RemoveScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy)
        {
            ClearPendingScreenshot();
            ResponseText.Text = "图片已移除。你仍然可以发送纯文字问题。";
        }
    }

    private void ClearPendingScreenshot()
    {
        _pendingScreenshot?.Dispose();
        _pendingScreenshot = null;
        ScreenshotPreviewImage.Source = null;
        PendingImageTitleText.Text = "待发送图片";
        ScreenshotDimensionsText.Text = string.Empty;
        ScreenshotPreviewPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowChatMessage(string message)
    {
        ChatBubble.Visibility = Visibility.Visible;
        HideQuickBar();
        ResponseText.Text = message;
        PromptBox.Focus();
    }

    private bool EnsureAiConfigured()
    {
        var provider = _app.AiProviderFactory.Create(_app.Settings);
        if (provider.IsConfigured)
        {
            return true;
        }

        ShowChatMessage($"{provider.DisplayName} 尚未配置可用。请先打开设置，选择 OpenAI-compatible 服务并填写模型信息。");
        _stateMachine.TransitionTo(PetState.Angry);
        _ = ReturnToIdleAsync(1400);
        return false;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        SendButton.IsEnabled = !busy;
        PromptBox.IsEnabled = !busy;
        QuickScreenshotButton.IsEnabled = !busy;
        QuickClipboardButton.IsEnabled = !busy;
        ScreenshotMenuItem.IsEnabled = !busy;
        ClipboardMenuItem.IsEnabled = !busy;
        RemoveScreenshotButton.IsEnabled = !busy;
    }

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
        if (prompt.Length == 0 || _isBusy)
        {
            return;
        }

        MarkInteraction();
        var pendingScreenshot = _pendingScreenshot;
        var request = new AiRequest(prompt, pendingScreenshot?.CreateAttachment());
        SetBusy(true);
        ResponseText.Text = "让我想想……";
        _stateMachine.TransitionTo(PetState.Thinking);

        try
        {
            var provider = _app.AiProviderFactory.Create(_app.Settings);
            ResponseText.Text = await provider.SendAsync(request);
            PromptBox.Clear();
            if (ReferenceEquals(_pendingScreenshot, pendingScreenshot))
            {
                ClearPendingScreenshot();
            }

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
            SetBusy(false);
        }
    }

    public void OpenSettings()
    {
        MarkInteraction();
        var window = new SettingsWindow(_app.Settings, saved =>
        {
            _app.Settings = saved;
            Topmost = saved.AlwaysOnTop;
            UpdateTopmostIndicators();
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

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HideQuickBar();
        OpenSettings();
    }

    private void HideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HideFromUserRequest();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => _app.ExitApplication();

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdateTopmostIndicators();
        _app.Settings.AlwaysOnTop = Topmost;
        _app.SettingsService.Save(_app.Settings);
        HideQuickBar();
    }

    private void UpdateTopmostIndicators()
    {
        TopmostMenuItem.IsChecked = Topmost;
        QuickTopmostButton.Content = Topmost ? "✓  始终置顶" : "○  始终置顶";
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
        _app.Settings.Left = Left + SurfaceLeftPadding;
        _app.Settings.Top = Top + SurfaceTopPadding;
        _app.SettingsService.Save(_app.Settings);
    }

    private void Window_Deactivated(object? sender, EventArgs e) => HideQuickBar();

    private void FullscreenTimer_Tick(object? sender, EventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        var shouldHide = _app.Settings.AutoHideInFullscreen
            && _fullscreenDetectionService.IsForegroundWindowFullscreen();

        if (shouldHide)
        {
            if (IsVisible)
            {
                _hiddenForFullscreen = true;
                HideQuickBar();
                Hide();
            }

            return;
        }

        if (!_hiddenForFullscreen)
        {
            return;
        }

        _hiddenForFullscreen = false;
        var showActivated = ShowActivated;
        try
        {
            ShowActivated = false;
            Show();
            WindowState = WindowState.Normal;
        }
        finally
        {
            ShowActivated = showActivated;
        }
    }

    public void ShowFromUserRequest()
    {
        _hiddenForFullscreen = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void HideFromUserRequest()
    {
        _hiddenForFullscreen = false;
        HideQuickBar();
        Hide();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SavePosition();
        if (AllowClose)
        {
            _fullscreenTimer.Stop();
            ClearPendingScreenshot();
            return;
        }

        e.Cancel = true;
        HideFromUserRequest();
    }
}
