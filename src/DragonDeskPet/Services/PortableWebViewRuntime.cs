using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace DragonDeskPet.Services;

// Used only after a user confirms downloading Microsoft's fixed-version x64 runtime.
// Never invokes an installer or modifies machine/user registry settings.
public static class PortableWebViewRuntime
{
    public const string DownloadPage = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
    private const string DownloadHost = "msedge.sf.dl.delivery.mp.microsoft.com";
    public static Uri FindOfficialPackage(string page)
    {
        var decoded = page.Replace(@"\u002F", "/").Replace(@"\/", "/");
        var matches = Regex.Matches(decoded, @"https://msedge\.sf\.dl\.delivery\.mp\.microsoft\.com/filestreamingservice/files/[a-fA-F0-9-]+/Microsoft\.WebView2\.FixedVersionRuntime\.(?<version>\d+\.\d+\.\d+\.\d+)\.x64\.cab");
        return matches.Cast<Match>().OrderByDescending(m => Version.Parse(m.Groups["version"].Value))
            .Select(m => new Uri(m.Value)).FirstOrDefault()
            ?? throw new ArgumentException("微软下载页面已变化，未找到 x64 便携包。请从微软官网选择 Fixed Version，或继续使用 Excel 导入。");
    }

    public static string? FindInstalledFolder(string root)
    {
        if (!Directory.Exists(root)) return null;
        if (File.Exists(Path.Combine(root, "msedgewebview2.exe"))) return root;
        return Directory.EnumerateDirectories(root).FirstOrDefault(d => File.Exists(Path.Combine(d, "msedgewebview2.exe")));
    }

    public static async Task DownloadAsync(IProgress<string> progress, CancellationToken token)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "webview2-runtime"));
        if (Path.GetPathRoot(root)?.Equals("C:\\", StringComparison.OrdinalIgnoreCase) != false)
            throw new ArgumentException("便携运行时须保存在 D 盘或其他非系统盘，请先移动测试包。");
        if (Directory.Exists(root)) throw new ArgumentException("webview2-runtime 目录已存在，请检查其中的运行时，不会自动覆盖。");
        // Unique staging is kept for diagnostics if cleanup is prevented by a locked browser.
        var stage = root + ".download-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(15) };
            progress.Report("正在获取微软官方便携包地址…");
            // The public download page may redirect to localized marketing query parameters.
            using var pageClient = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            var package = FindOfficialPackage(await pageClient.GetStringAsync(DownloadPage, token));
            if (package.Host != DownloadHost || package.Scheme != "https") throw new ArgumentException("运行时下载地址不可信。");
            using var response = await client.GetAsync(package, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            const long limit = 600L * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new ArgumentException("便携运行时超过600 MiB限制，请使用官网手动下载。");
            var cab = Path.Combine(stage, "runtime.cab");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(cab, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long total = 0, reported = -1048576;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    total += read;
                    if (total > limit) throw new ArgumentException("便携运行时超过下载大小限制。");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    if (total - reported >= 1048576)
                    { reported = total; progress.Report($"正在下载便携运行时：{total / 1048576d:F1} MiB（只写入程序目录）"); }
                }
            }
            progress.Report("正在解压微软便携运行时（不执行安装）…");
            var expanded = Path.Combine(stage, "expanded"); Directory.CreateDirectory(expanded);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "expand.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add(cab); start.ArgumentList.Add("-F:*"); start.ArgumentList.Add(expanded);
            using var process = Process.Start(start) ?? throw new IOException("无法启动系统 CAB 解压工具。");
            try { await process.WaitForExitAsync(token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            if (process.ExitCode != 0) throw new IOException("CAB 解压失败。");
            var folder = FindInstalledFolder(expanded) ?? throw new IOException("下载包缺少 WebView2 程序。");
            token.ThrowIfCancellationRequested();
            Directory.Move(folder, root);
            progress.Report("便携运行时已准备好，请关闭并重新打开教务导入。");
        }
        finally
        {
            // Only this freshly generated staging directory, never an existing runtime/profile.
            if (Directory.Exists(stage) && Path.GetFullPath(stage).StartsWith(root + ".download-", StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(stage, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
