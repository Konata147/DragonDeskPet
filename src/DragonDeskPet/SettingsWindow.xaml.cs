using System.Windows;
using System.Windows.Controls;
using DragonDeskPet.AI;
using DragonDeskPet.Services;
using TextBox = System.Windows.Controls.TextBox;

namespace DragonDeskPet;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _current;
    private readonly Action<AppSettings> _save;
    private bool _initializing = true;

    public SettingsWindow(AppSettings current, Action<AppSettings> save)
    {
        _current = current;
        _save = save;
        InitializeComponent();

        ProviderBox.ItemsSource = AiProviderCatalog.All;
        ProviderBox.SelectedValue = current.Provider;
        if (ProviderBox.SelectedItem is null)
        {
            ProviderBox.SelectedValue = "Offline";
        }

        var selectedDescriptor = ProviderBox.SelectedItem as AiProviderDescriptor ?? AiProviderCatalog.All[0];
        ModelTextBox.Text = string.IsNullOrWhiteSpace(current.Model) ? selectedDescriptor.DefaultModel : current.Model;
        BaseUrlTextBox.Text = string.IsNullOrWhiteSpace(current.BaseUrl) ? selectedDescriptor.DefaultBaseUrl : current.BaseUrl;
        ApiKeyBox.Password = current.ApiKey;
        ScaleSlider.Value = current.Scale;
        AlwaysOnTopBox.IsChecked = current.AlwaysOnTop;
        AutoHideInFullscreenBox.IsChecked = current.AutoHideInFullscreen;
        StartWithWindowsBox.IsChecked = current.StartWithWindows;
        ReminderSoundBox.IsChecked = current.ReminderSoundEnabled;
        FocusMinutesBox.Text = current.PomodoroFocusMinutes.ToString();
        ShortBreakMinutesBox.Text = current.PomodoroShortBreakMinutes.ToString();
        LongBreakMinutesBox.Text = current.PomodoroLongBreakMinutes.ToString();
        RoundsBeforeLongBreakBox.Text = current.PomodoroRoundsBeforeLongBreak.ToString();
        UpdateScaleLabel(current.Scale);
        _initializing = false;
        UpdateProviderHint(selectedDescriptor);
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ProviderBox.SelectedItem is not AiProviderDescriptor descriptor)
        {
            return;
        }

        BaseUrlTextBox.Text = descriptor.DefaultBaseUrl;
        ModelTextBox.Text = descriptor.DefaultModel;
        UpdateProviderHint(descriptor);
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleValueText is not null)
        {
            UpdateScaleLabel(e.NewValue);
        }
    }

    private void UpdateScaleLabel(double value) => ScaleValueText.Text = $"{value:P0}";

    private void UpdateProviderHint(AiProviderDescriptor descriptor)
    {
        ApiKeyBox.IsEnabled = descriptor.RequiresApiKey;
        ApiKeyHintText.Text = descriptor.Id == "Ollama"
            ? "无需 API Key。需先在电脑上安装 Ollama 并下载所选本地模型；聊天和图片都在本机处理。"
            : "只保存在本机，并使用当前 Windows 用户身份加密。";
        ProviderHintText.Text = descriptor.Id == "Ollama"
            ? "推荐 qwen2.5vl:3b（约 3.2 GB，支持文字与图片）。首次使用前需由用户自行安装模型。"
            : descriptor.UsesOpenAiCompatibleTransport || descriptor.Id == "Offline"
                ? string.Empty
                : "此提供商已预留在统一架构中，当前版本暂未实现它的原生协议。";
        ProviderHintPanel.Visibility = string.IsNullOrWhiteSpace(ProviderHintText.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var descriptor = ProviderBox.SelectedItem as AiProviderDescriptor ?? AiProviderCatalog.All[0];
        var baseUrl = BaseUrlTextBox.Text.Trim();
        var model = ModelTextBox.Text.Trim();

        if (!TryReadNumber(FocusMinutesBox, 1, 240, out var focusMinutes)
            || !TryReadNumber(ShortBreakMinutesBox, 1, 120, out var shortBreakMinutes)
            || !TryReadNumber(LongBreakMinutesBox, 1, 180, out var longBreakMinutes)
            || !TryReadNumber(RoundsBeforeLongBreakBox, 1, 12, out var roundsBeforeLongBreak))
        {
            return;
        }

        if (descriptor.UsesOpenAiCompatibleTransport)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)
                || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ShowValidation("请填写有效的 HTTP 或 HTTPS Base URL。");
                BaseUrlTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                ShowValidation("请填写模型名称。");
                ModelTextBox.Focus();
                return;
            }
        }

        var updated = new AppSettings
        {
            Left = _current.Left,
            Top = _current.Top,
            Scale = Math.Round(ScaleSlider.Value, 1),
            AlwaysOnTop = AlwaysOnTopBox.IsChecked == true,
            AutoHideInFullscreen = AutoHideInFullscreenBox.IsChecked == true,
            ReminderSoundEnabled = ReminderSoundBox.IsChecked == true,
            PomodoroFocusMinutes = focusMinutes,
            PomodoroShortBreakMinutes = shortBreakMinutes,
            PomodoroLongBreakMinutes = longBreakMinutes,
            PomodoroRoundsBeforeLongBreak = roundsBeforeLongBreak,
            StartWithWindows = StartWithWindowsBox.IsChecked == true,
            HasCompletedOnboarding = _current.HasCompletedOnboarding,
            Provider = descriptor.Id,
            Model = model,
            BaseUrl = baseUrl,
            ApiKey = ApiKeyBox.Password
        };

        try
        {
            _save(updated);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowValidation($"保存失败：{ex.Message}");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private bool TryReadNumber(TextBox box, int minimum, int maximum, out int value)
    {
        if (int.TryParse(box.Text.Trim(), out value) && value >= minimum && value <= maximum)
        {
            return true;
        }

        ShowValidation($"请输入 {minimum} 到 {maximum} 之间的整数。");
        box.Focus();
        return false;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationPanel.Visibility = Visibility.Visible;
    }
}
