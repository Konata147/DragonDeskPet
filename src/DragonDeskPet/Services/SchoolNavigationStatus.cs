using Microsoft.Web.WebView2.Core;

namespace DragonDeskPet.Services;

// Only enum/status codes enter diagnostics, never URLs, cookies or page contents.
public sealed class SchoolNavigationStatus
{
    private ulong _navigationId;
    private string? _blockedReason;
    public string Message { get; private set; } = "正在启动教务浏览器…";

    public void Begin(ulong id, bool allowed)
    {
        if (_navigationId != id) _blockedReason = null;
        _navigationId = id;
        if (!allowed)
            _blockedReason = "已阻止非学校 HTTPS 页面跳转（SchoolOriginBlocked）。没有放行不受信任的地址。请反馈此提示，或改用 Excel 导入。";
        Message = _blockedReason ?? "正在加载学校页面，请稍候…";
    }

    public void Complete(ulong id, bool success, CoreWebView2WebErrorStatus error, int httpStatus)
    {
        // Canceled older requests must not replace the current page's status.
        if (id != _navigationId) return;
        Message = _blockedReason ?? (success
            ? "页面已加载。请自行登录并打开个人课表，然后点击“读取当前课表”。"
            : Describe(error, httpStatus));
    }

    public static string Describe(CoreWebView2WebErrorStatus error, int httpStatus)
    {
        var reason = error switch
        {
            CoreWebView2WebErrorStatus.OperationCanceled => "本次页面加载被取消，并不代表断网。可点击“重试加载”；若页面已正常显示，可继续登录。",
            CoreWebView2WebErrorStatus.ConnectionAborted => "页面加载中断。可点击“重试加载”；若页面已正常显示，可继续登录。",
            CoreWebView2WebErrorStatus.HostNameNotResolved => "内嵌浏览器无法解析学校域名。请检查 DNS、校园网或代理连接后重试。",
            CoreWebView2WebErrorStatus.Timeout => "连接学校网站超时，请稍后重试或检查校园网、代理连接。",
            CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired => "内嵌浏览器的代理需要身份验证，请检查系统代理设置，或改用 Excel 导入。",
            CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired => "学校服务器要求身份验证，请检查页面中的登录提示，或改用 Excel 导入。",
            CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
            CoreWebView2WebErrorStatus.CertificateExpired or
            CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or
            CoreWebView2WebErrorStatus.CertificateRevoked or
            CoreWebView2WebErrorStatus.CertificateIsInvalid => "教务连接的安全证书校验失败，未绕过安全检查。请检查系统时间、网络代理或联系学校，暂可使用 Excel 导入。",
            CoreWebView2WebErrorStatus.CannotConnect or
            CoreWebView2WebErrorStatus.ConnectionReset or
            CoreWebView2WebErrorStatus.Disconnected => "内嵌浏览器未能维持与学校的连接。请检查校园网或代理连接后重试。",
            _ when httpStatus >= 400 => "学校服务器返回错误，请稍后重试或改用 Excel 导入。",
            _ => "内嵌浏览器未完成页面加载。请重试；若普通浏览器可登录，请反馈下方错误码，暂可使用 Excel 导入。"
        };
        return $"{reason}（错误码：{error}" + (httpStatus > 0 ? $"；HTTP {httpStatus}" : "") + "）";
    }
}
