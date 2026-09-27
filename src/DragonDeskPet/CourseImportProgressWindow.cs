using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DragonDeskPet.Core;
using Button = System.Windows.Controls.Button;

namespace DragonDeskPet;

public sealed class CourseImportProgressWindow : Window
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), FontSize = 14 };
    private bool _finished;
    public string Stage { get; set; } = "正在读取和解析课表";
    public CourseImportProgressWindow(Window owner)
    {
        Owner = owner; Title = "课表导入"; Width = 400; Height = 190; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = owner.Background;
        var panel = new StackPanel { Margin = new Thickness(16) }; panel.Children.Add(_status);
        var cancel = new Button { Content = "取消", Style = owner.TryFindResource("CourseButton") as Style, Margin = new Thickness(8) };
        cancel.Click += (_, _) => { _cancel.Cancel(); Stage = "正在取消并释放资源"; };
        panel.Children.Add(cancel); Content = panel;
        Closing += (_, e) => { if (!_finished) { _cancel.Cancel(); e.Cancel = true; Stage = "正在取消并释放资源"; } };
    }
    public async Task<T?> RunAsync<T>(Func<CancellationToken, Task<T>> action) where T : class
    {
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => _status.Text = $"{Stage}\n已用时 {watch.Elapsed.TotalSeconds:F0} 秒；尚未修改课表。";
        Owner.IsEnabled = false; Show(); timer.Start();
        try { return await action(_cancel.Token); }
        catch (OperationCanceledException) { return null; }
        finally { timer.Stop(); _finished = true; Owner.IsEnabled = true; Close(); _cancel.Dispose(); }
    }
}
