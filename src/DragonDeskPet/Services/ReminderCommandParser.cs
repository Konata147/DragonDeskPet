using System.Globalization;
using System.Text.RegularExpressions;
using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public static partial class ReminderCommandParser
{
    public static bool LooksLikeReminderCommand(string text) => text.Contains("提醒我", StringComparison.Ordinal);

    public static bool TryParse(string text, DateTimeOffset now, out ReminderDraft? draft, out string? error)
    {
        draft = null;
        error = null;
        text = text.Trim();

        var relative = RelativePattern().Match(text);
        if (relative.Success && int.TryParse(relative.Groups["amount"].Value, out var amount) && amount > 0)
        {
            var unit = relative.Groups["unit"].Value;
            var delay = unit == "小时" ? TimeSpan.FromHours(amount) : TimeSpan.FromMinutes(amount);
            draft = new ReminderDraft(relative.Groups["message"].Value.Trim(), ReminderRepeat.None, now.Add(delay).ToUniversalTime());
            return Validate(draft, out error);
        }

        var daily = DailyPattern().Match(text);
        if (daily.Success && TryReadTime(daily.Groups["time"].Value, out var dailyTime))
        {
            var due = ReminderService.GetNextDailyOccurrence(dailyTime, now.ToUniversalTime());
            draft = new ReminderDraft(daily.Groups["message"].Value.Trim(), ReminderRepeat.Daily, due, dailyTime);
            return Validate(draft, out error);
        }

        var dated = DatedPattern().Match(text);
        if (dated.Success && TryReadTime(dated.Groups["time"].Value, out var localTime))
        {
            var localNow = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
            var date = DateOnly.FromDateTime(localNow.DateTime).AddDays(dated.Groups["day"].Value == "明天" ? 1 : 0);
            var localDateTime = date.ToDateTime(localTime);
            var due = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
            if (due <= localNow)
            {
                error = "这个时间已经过去了，请换一个时间。";
                return false;
            }

            draft = new ReminderDraft(dated.Groups["message"].Value.Trim(), ReminderRepeat.None, due.ToUniversalTime());
            return Validate(draft, out error);
        }

        error = "我没能确定提醒时间。可以试试“20分钟后提醒我休息”“明天 8:30 提醒我上课”或“每天 22:00 提醒我吃药”。";
        return false;
    }

    private static bool Validate(ReminderDraft draft, out string? error)
    {
        if (string.IsNullOrWhiteSpace(draft.Message))
        {
            error = "请补充要提醒的内容。";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value.Replace('：', ':'), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    [GeneratedRegex(@"^(?<amount>\d+)\s*(?<unit>分钟|小时)后\s*提醒我\s*(?<message>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RelativePattern();

    [GeneratedRegex(@"^每天\s*(?<time>\d{1,2}[:：]\d{2})\s*提醒我\s*(?<message>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DailyPattern();

    [GeneratedRegex(@"^(?<day>今天|明天)\s*(?<time>\d{1,2}[:：]\d{2})\s*提醒我\s*(?<message>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DatedPattern();
}
