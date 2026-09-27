using System.IO;
using System.Windows;
using System.Windows.Controls;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace DragonDeskPet;

public sealed class SchoolImportWindow : Window
{
    private readonly WebView2 _browser = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly Button _download = new() { Content = "下载便携 WebView2", Visibility = Visibility.Collapsed };
    private readonly HnieScheduleAdapter _adapter = new();
    private readonly SchoolNavigationStatus _navigation = new();
    private readonly SchoolRedirectRecovery _redirectRecovery = new();
    private ulong _latestNavigationId;
    private readonly IProductivityStore _store;
    private readonly string _sessionRoot;
    private readonly string _sessionPath;
    private bool _closing;
    private bool _cleaned;
    private bool _reading;
    private Task _initializing = Task.CompletedTask;
    private CoreWebView2Environment? _environment;
    private readonly TaskCompletionSource _browserExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CourseImportPreview? Preview { get; private set; }
    public string? CleanupNotice { get; private set; }

    public SchoolImportWindow(Window owner, IProductivityStore store)
    {
        _store = store;
        Owner = owner; Title = "湖南工程学院 · 教务课表导入"; Width = 1050; Height = 740;
        MinWidth = 760; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Background = owner.Background;
        _sessionRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(store.DataPath)!, "school-sessions"));
        _sessionPath = Path.Combine(_sessionRoot, Guid.NewGuid().ToString("N"));
        var panel = new DockPanel { Margin = new Thickness(10) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "请自行登录、输入验证码并打开个人学期课表，然后点击读取。不会保存密码或登录状态。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var read = new Button { Content = "读取当前课表", Style = owner.TryFindResource("CourseButton") as Style };
        read.Click += Read_Click; buttons.Children.Add(read);
        var retry = new Button { Content = "重试加载", Style = owner.TryFindResource("CourseButton") as Style };
        retry.Click += (_, _) =>
        {
            if (_closing || _reading || _browser.CoreWebView2 is not { } core) return;
            // Reopen the public entry point, never replay a login POST submission.
            _redirectRecovery.ResetForManualRetry();
            core.Navigate(HnieScheduleAdapter.EntryUrl);
        };
        buttons.Children.Add(retry);
        var close = new Button { Content = "关闭并清理登录", Style = owner.TryFindResource("CourseButton") as Style };
        close.Click += (_, _) => Close(); buttons.Children.Add(close);
        _download.Style = owner.TryFindResource("CourseButton") as Style;
        _download.Click += DownloadRuntime_Click; buttons.Children.Add(_download);
        header.Children.Add(buttons); header.Children.Add(_status); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        panel.Children.Add(_browser); Content = panel;
        Loaded += (_, _) => _initializing = InitializeAsync();
        Closing += async (_, e) =>
        {
            if (_cleaned) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true; _status.Text = "正在关闭浏览器并清理本次登录…";
            await CleanupAsync(); _cleaned = true; Close();
        };
    }

    private async Task InitializeAsync()
    {
        _status.Text = _navigation.Message;
        try
        {
            string? portable = null;
            try { _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (WebView2RuntimeNotFoundException)
            { portable = PortableWebViewRuntime.FindInstalledFolder(Path.Combine(AppContext.BaseDirectory, "webview2-runtime")); }
            _environment = await CoreWebView2Environment.CreateAsync(portable, _sessionPath);
            _environment.BrowserProcessExited += (_, _) => _browserExited.TrySetResult();
            _browser.CreationProperties = new CoreWebView2CreationProperties { IsInPrivateModeEnabled = true };
            await _browser.EnsureCoreWebView2Async(_environment);
            if (_closing) { _browser.Dispose(); return; }
            var core = _browser.CoreWebView2;
            core.Profile.IsPasswordAutosaveEnabled = false;
            core.Profile.IsGeneralAutofillEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => { args.Cancel = true; _status.Text = "本窗口仅用于读取课表。如需文件，请在日常浏览器导出后使用文件导入。"; };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && _adapter.IsAllowed(uri)) core.Navigate(uri.AbsoluteUri);
                else _status.Text = "已阻止非学校 HTTPS 弹出页面（SchoolPopupBlocked）。请反馈学校登录地址变化，或使用 Excel 导入。";
            };
            core.NavigationStarting += (_, args) =>
            {
                _latestNavigationId = args.NavigationId;
                var allowed = Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && _adapter.IsAllowed(uri);
                _navigation.Begin(args.NavigationId, allowed);
                args.Cancel = !allowed;
                if (!_closing) _status.Text = _navigation.Message;
                if (!_closing && _redirectRecovery.Observe(args.NavigationId, allowed, args.IsRedirected, uri))
                {
                    var canceledId = args.NavigationId;
                    _status.Text = "已阻止学校 HTTP 跳转，正在返回安全的 HTTPS 入口…";
                    // Leave the canceling callback before starting a new navigation.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_closing || _latestNavigationId != canceledId) return;
                        try { core.Navigate(HnieScheduleAdapter.EntryUrl); }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        { _status.Text = $"返回安全入口失败（0x{ex.HResult:X8}），请点击“重试加载”或使用 Excel 导入。"; }
                    }));
                }
            };
            core.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            core.NavigationCompleted += (_, args) =>
            {
                _navigation.Complete(args.NavigationId, args.IsSuccess, args.WebErrorStatus, args.HttpStatusCode);
                if (!_closing && !_reading) _status.Text = _navigation.Message;
            };
            core.Navigate(HnieScheduleAdapter.EntryUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text = "教务浏览器无法启动。可确认后下载微软便携 WebView2 到程序目录；不会自动安装到 C 盘，也可继续使用 Excel 导入。";
            _download.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _status.Text = $"教务浏览器初始化失败（错误码：0x{ex.HResult:X8}）。请关闭后重开教务导入，仍失败时反馈此代码；也可使用 Excel 导入。";
        }
    }

    private async void DownloadRuntime_Click(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        if (new ConfirmActionWindow("下载便携运行时？", "将从微软官网下载 Fixed Version x64 运行时，下载量可能数百 MiB，解压后需要约1 GiB。只存放在程序旁 webview2-runtime，不安装到系统。使用遵循微软 WebView2 运行时许可；取消可继续使用 Excel 导入。", "确认下载") { Owner = this }.ShowDialog() != true) return;
        try
        {
            var progress = new CourseImportProgressWindow(this);
            var report = new Progress<string>(message => progress.Stage = message);
            var result = await progress.RunAsync(async token => { await PortableWebViewRuntime.DownloadAsync(report, token); return "ok"; });
            if (result is not null) { _download.Visibility = Visibility.Collapsed; _status.Text = "便携运行时下载完成，请关闭并重新打开教务导入。"; }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { _status.Text = ex is ArgumentException ? ex.Message : "便携包下载或解压失败，请检查网络及磁盘空间，或改用 Excel 导入。没有运行系统安装程序。"; }
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (_reading || _closing || _browser.CoreWebView2 is not { } core) return;
        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) || !_adapter.IsAllowed(uri)) return;
        _reading = true;
        try
        {
            _status.Text = "正在读取当前页面的课表区域，不读取密码或 Cookie…";
            var json = await core.ExecuteScriptAsync(HnieScheduleAdapter.SnapshotScript);
            if (_closing) return;
            Preview = _adapter.ParseSnapshot(json, CourseDataCopy.Clone(_store.Data.Semester), _store.Data.Courses);
            Close();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { _status.Text = ex is ArgumentException ? ex.Message : "课表读取失败，请重新打开个人学期课表或使用 Excel 导入。现有课表未改变。"; }
        finally { _reading = false; }
    }

    private async Task CleanupAsync()
    {
        try { await _initializing.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch { CleanupNotice = "教务浏览器关闭超时；本次使用临时隐私会话，残留缓存待浏览器退出后清理。"; }
        try
        {
            if (_browser.CoreWebView2 is { } core)
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { /* InPrivate mode does not retain a reusable login even if the renderer has exited. */ }
        _browser.Dispose();
        if (_environment is not null)
            await Task.WhenAny(_browserExited.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        // Delete only this randomly named session directory, never a shared browser profile.
        var full = Path.GetFullPath(_sessionPath);
        if (!full.StartsWith(_sessionRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        if (!Directory.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return;
        try { Directory.Delete(full, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { CleanupNotice = "浏览器已关闭，临时文件仍被占用；本次使用隐私会话，临时目录为：" + _sessionPath; }
    }
}
