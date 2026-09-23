using System.Configuration;
using System.Data;
using System.Windows;
using DragonDeskPet.AI;
using DragonDeskPet.Services;
using Application = System.Windows.Application;

namespace DragonDeskPet;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private TrayIconService? _trayIcon;
    private SingleInstanceService? _singleInstance;
    private CrashLogService? _crashLogs;
    private int _handlingFatalException;

    public AppSettings Settings { get; set; } = new();
    public SettingsService SettingsService { get; } = new();
    public AiProviderFactory AiProviderFactory { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimaryInstance)
        {
            _singleInstance.SignalPrimaryAsync().GetAwaiter().GetResult();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _crashLogs = new CrashLogService(SettingsService.SettingsDirectory);
        _crashLogs.Cleanup();
        RegisterExceptionHandlers();
        _singleInstance.ShowRequested += (_, _) => Dispatcher.BeginInvoke(ShowPet);
        Settings = SettingsService.Load();

        var mainWindow = new MainWindow(this);
        MainWindow = mainWindow;
        _trayIcon = new TrayIconService(
            show: () => Dispatcher.Invoke(ShowPet),
            hide: () => Dispatcher.Invoke(mainWindow.Hide),
            openSettings: () => Dispatcher.Invoke(mainWindow.OpenSettings),
            exit: () => Dispatcher.Invoke(ExitApplication),
            iconPath: AssetService.IconPath);

        mainWindow.Show();
    }

    public void ShowPet()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    public void ExitApplication()
    {
        if (MainWindow is MainWindow window)
        {
            window.AllowClose = true;
            window.Close();
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        UnregisterExceptionHandlers();
        base.OnExit(e);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void UnregisterExceptionHandlers()
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleException(e.Exception, notifyUser: true, shouldExit: true);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            HandleException(exception, notifyUser: false, shouldExit: e.IsTerminating);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        HandleException(e.Exception, notifyUser: false, shouldExit: false);
    }

    private void HandleException(Exception exception, bool notifyUser, bool shouldExit)
    {
        if (Interlocked.Exchange(ref _handlingFatalException, 1) == 1)
        {
            return;
        }

        string? logPath = null;
        try
        {
            logPath = _crashLogs?.Write(exception, Settings.ApiKey);
        }
        catch
        {
            // Failure reporting must not throw another unhandled exception.
        }

        if (!notifyUser)
        {
            if (shouldExit)
            {
                Dispatcher.BeginInvoke(ExitApplication);
            }
            else
            {
                Interlocked.Exchange(ref _handlingFatalException, 0);
            }

            return;
        }

        void NotifyUser()
        {
            var location = string.IsNullOrWhiteSpace(logPath) ? "日志写入失败" : $"日志：{logPath}";
            System.Windows.MessageBox.Show(
                $"龙娘桌宠遇到了一点问题。\n\n{location}",
                "龙娘桌宠",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            if (shouldExit)
            {
                ExitApplication();
            }
            else
            {
                Interlocked.Exchange(ref _handlingFatalException, 0);
            }
        }

        if (Dispatcher.CheckAccess())
        {
            NotifyUser();
        }
        else
        {
            Dispatcher.BeginInvoke(NotifyUser);
        }
    }
}

