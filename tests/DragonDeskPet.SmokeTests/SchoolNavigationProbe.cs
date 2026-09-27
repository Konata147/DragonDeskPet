using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DragonDeskPet;
using DragonDeskPet.Services;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System.Text;

// Explicit opt-in online diagnostic. No login, page extraction or shared profile.
internal static class SchoolNavigationProbe
{
    public static int Run(bool syntheticRedirect = false)
    {
        var result = 0;
        var thread = new Thread(() =>
        {
            try
            {
                // Do not run DragonDeskPet.App startup/single-instance behavior in a probe.
                var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var owner = new Window { Width = 1, Height = 1, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                owner.Show();
                var root = Path.Combine(AppContext.BaseDirectory, "test-data", "school-probe-" + Guid.NewGuid().ToString("N"));
                var window = new SchoolImportWindow(owner, new ProductivityStore(root)) { Opacity = 0, ShowActivated = false };
                var browser = ((DockPanel)window.Content).Children.OfType<WebView2>().Single();
                var entryRequests = 0;
                var unsafeRequests = 0;
                var recovered = false;
                var bodies = new List<MemoryStream>();
                browser.CoreWebView2InitializationCompleted += (_, args) =>
                {
                    Console.WriteLine("Initialized=" + args.IsSuccess);
                    if (!args.IsSuccess) return;
                    if (syntheticRedirect)
                    {
                        var core = browser.CoreWebView2;
                        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                        core.WebResourceRequested += (_, request) =>
                        {
                            // Intercept every request: this mode never contacts the school.
                            if (request.Request.Uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
                                || request.Request.Method != "GET") unsafeRequests++;
                            var entry = request.Request.Uri == HnieScheduleAdapter.EntryUrl;
                            if (entry) entryRequests++;
                            var redirect = entry && entryRequests == 1;
                            var body = new MemoryStream(Encoding.UTF8.GetBytes("<html><body>Synthetic test only</body></html>"));
                            bodies.Add(body);
                            request.Response = core.Environment.CreateWebResourceResponse(body, redirect ? 302 : 200,
                                redirect ? "Found" : "OK", redirect
                                    ? "Location: http://jwcmis.hnie.edu.cn/synthetic-landing?never-forward=this\r\nCache-Control: no-store"
                                    : "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
                        };
                    }
                    browser.CoreWebView2.NavigationStarting += (_, navigation) =>
                    {
                        // Scheme + authority only, never paths, query strings or credentials.
                        var target = Uri.TryCreate(navigation.Uri, UriKind.Absolute, out var uri)
                            ? (uri.Scheme is "http" or "https" ? uri.Scheme + "://" + uri.IdnHost + ":" + uri.Port : uri.Scheme)
                            : "invalid";
                        Console.WriteLine($"Navigate: {navigation.NavigationId}; origin={target}; redirect={navigation.IsRedirected}");
                    };
                    browser.CoreWebView2.NavigationCompleted += (_, navigation) =>
                    {
                        Console.WriteLine($"Complete: {navigation.NavigationId}; success={navigation.IsSuccess}; error={navigation.WebErrorStatus}; HTTP={navigation.HttpStatusCode}");
                        if (syntheticRedirect && entryRequests == 2 && navigation.IsSuccess) recovered = true;
                    };
                };
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
                window.Closed += (_, _) =>
                {
                    Console.WriteLine("CleanupNotice=" + (window.CleanupNotice is not null));
                    if (syntheticRedirect)
                    {
                        Console.WriteLine($"Recovery={recovered}; EntryRequests={entryRequests}; UnsafeRequests={unsafeRequests}");
                        if (!recovered || entryRequests != 2 || unsafeRequests != 0 || window.CleanupNotice is not null) result = 1;
                    }
                    foreach (var body in bodies) body.Dispose();
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                    {
                        owner.Close();
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }));
                };
                window.Show(); timer.Start(); Dispatcher.Run();
            }
            catch (Exception ex) { Console.WriteLine($"Probe failure: {ex.GetType().Name}; 0x{ex.HResult:X8}"); result = 1; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        return result;
    }
}
