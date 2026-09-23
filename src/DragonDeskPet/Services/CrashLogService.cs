using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DragonDeskPet.Services;

public sealed class CrashLogService
{
    private static readonly Regex SensitiveHeaderPattern = new(
        @"(?i)(api[_ -]?key|authorization|bearer)\s*[:=]?\s*[^\s,;]+",
        RegexOptions.Compiled);
    private readonly object _sync = new();

    public CrashLogService(string dataDirectory)
    {
        LogDirectory = Path.Combine(dataDirectory, "logs");
    }

    public string LogDirectory { get; }

    public string Write(Exception exception, params string[] secrets)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(LogDirectory);
            var timestamp = DateTimeOffset.Now;
            var fileName = $"crash-{timestamp:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.txt";
            var path = Path.Combine(LogDirectory, fileName);
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            var details = new StringBuilder()
                .AppendLine($"Time: {timestamp:O}")
                .AppendLine($"Version: {version}")
                .AppendLine($"Exception: {exception.GetType().FullName}")
                .AppendLine($"Message: {exception.Message}")
                .AppendLine("Stack trace:")
                .AppendLine(exception.StackTrace ?? "(not available)")
                .ToString();

            File.WriteAllText(path, Redact(details, secrets), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Cleanup(10);
            return path;
        }
    }

    public void Cleanup(int keepCount = 10)
    {
        if (!Directory.Exists(LogDirectory))
        {
            return;
        }

        var staleLogs = new DirectoryInfo(LogDirectory)
            .GetFiles("crash-*.txt")
            .OrderByDescending(file => file.CreationTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(Math.Max(0, keepCount))
            .ToArray();

        foreach (var staleLog in staleLogs)
        {
            try
            {
                staleLog.Delete();
            }
            catch (IOException)
            {
                // A diagnostic file may be open in another process; retry next startup.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never cause a second application failure.
            }
        }
    }

    private static string Redact(string value, IEnumerable<string> secrets)
    {
        var redacted = SensitiveHeaderPattern.Replace(value, "$1=[REDACTED]");
        foreach (var secret in secrets.Where(secret => !string.IsNullOrWhiteSpace(secret)))
        {
            redacted = redacted.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }

        return redacted;
    }
}
