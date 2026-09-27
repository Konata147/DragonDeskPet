using DragonDeskPet.Services;
using Microsoft.Web.WebView2.Core;

internal static class SchoolNavigationTests
{
    public static void Recovery()
    {
        var gate = new SchoolRedirectRecovery();
        var schoolHttp = new Uri("http://jwcmis.hnie.edu.cn/landing?synthetic-token=not-forwarded");
        if (gate.Observe(1, false, true, schoolHttp)) throw new Exception("untrusted chain recovered");
        gate.Observe(2, true, false, new Uri(HnieScheduleAdapter.EntryUrl));
        if (gate.Observe(3, false, true, schoolHttp) || gate.Observe(2, false, false, schoolHttp))
            throw new Exception("unrelated or non-redirect request recovered");
        foreach (var address in new[] { "http://jwcmis.hnie.edu.cn.evil.example/", "http://evil.example/",
            "http://jwcmis.hnie.edu.cn:8080/", "http://user:secret@jwcmis.hnie.edu.cn/", "file:///D:/test", "about:blank" })
            if (gate.Observe(2, false, true, new Uri(address))) throw new Exception("unsafe recovery accepted");
        if (!gate.Observe(2, false, true, schoolHttp)) throw new Exception("school downgrade not recovered");
        gate.Observe(4, true, false, new Uri(HnieScheduleAdapter.EntryUrl));
        if (gate.Observe(4, false, true, schoolHttp)) throw new Exception("recovery loop allowed");
        gate.ResetForManualRetry(); gate.Observe(5, true, false, new Uri(HnieScheduleAdapter.EntryUrl));
        if (!gate.Observe(5, false, true, schoolHttp)) throw new Exception("manual retry budget not reset");
        if (new HnieScheduleAdapter().IsAllowed(schoolHttp)) throw new Exception("HTTP was allowed");
    }

    public static void Run()
    {
        var state = new SchoolNavigationStatus();
        state.Begin(1, true);
        state.Begin(1, false); // Redirect shares its navigation id.
        state.Complete(1, false, CoreWebView2WebErrorStatus.OperationCanceled, 0);
        if (!state.Message.Contains("SchoolOriginBlocked")) throw new Exception("blocked reason overwritten");
        state.Begin(2, true);
        state.Complete(1, false, CoreWebView2WebErrorStatus.Timeout, 0);
        if (!state.Message.Contains("正在加载")) throw new Exception("stale failure overwrote current navigation");
        state.Complete(2, true, CoreWebView2WebErrorStatus.Unknown, 200);
        if (!state.Message.Contains("页面已加载")) throw new Exception("successful retry did not clear failure");
        var canceled = SchoolNavigationStatus.Describe(CoreWebView2WebErrorStatus.OperationCanceled, 0);
        if (!canceled.Contains("不代表断网") || !canceled.Contains("OperationCanceled")) throw new Exception("cancellation misdiagnosed");
        foreach (var error in Enum.GetValues<CoreWebView2WebErrorStatus>())
        {
            var message = SchoolNavigationStatus.Describe(error, 403);
            if (!message.Contains(error.ToString()) || !message.Contains("HTTP 403") || message.Contains("https://"))
                throw new Exception("missing safe diagnostic code");
        }
        if (!SchoolNavigationStatus.Describe(CoreWebView2WebErrorStatus.CertificateIsInvalid, 0).Contains("未绕过"))
            throw new Exception("certificate failure guidance missing");
    }
}
