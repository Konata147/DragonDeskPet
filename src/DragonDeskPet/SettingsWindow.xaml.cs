using System.Windows;
using System.Windows.Controls;
using DragonDeskPet.AI;
using DragonDeskPet.Services;

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
        StartWithWindowsBox.IsChecked = current.StartWithWindows;
        UpdateScaleLabel(current.Scale);
        _initializing = false;
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ProviderBox.SelectedItem is not AiProviderDescriptor descriptor)
        {
            return;
        }

        BaseUrlTextBox.Text = descriptor.DefaultBaseUrl;
        ModelTextBox.Text = descriptor.DefaultModel;
        ValidationText.Text = descriptor.UsesOpenAiCompatibleTransport || descriptor.Id == "Offline"
            ? string.Empty
            : "此提供商已预留在统一架构中，V0.2 暂未实现它的原生协议。";
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleValueText is not null)
        {
            UpdateScaleLabel(e.NewValue);
        }
    }

    private void UpdateScaleLabel(double value) => ScaleValueText.Text = $"{value:P0}";

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var descriptor = ProviderBox.SelectedItem as AiProviderDescriptor ?? AiProviderCatalog.All[0];
        var baseUrl = BaseUrlTextBox.Text.Trim();
        var model = ModelTextBox.Text.Trim();

        if (descriptor.UsesOpenAiCompatibleTransport)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)
                || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ValidationText.Text = "请填写有效的 HTTP 或 HTTPS Base URL。";
                BaseUrlTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                ValidationText.Text = "请填写模型名称。";
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
            ValidationText.Text = $"保存失败：{ex.Message}";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
