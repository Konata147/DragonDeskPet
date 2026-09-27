using System.Globalization;
using System.Text.Json;

namespace DragonDeskPet.Core;

public sealed class PeriodTime
{
    public int Number { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}

public sealed class SeasonalTimetable
{
    // MM-dd, independent of the academic year. Inclusive summer boundaries.
    public string SummerFrom { get; set; } = "05-01";
    public string SummerThrough { get; set; } = "09-30";
    public List<PeriodTime> Summer { get; set; } = [];
    public List<PeriodTime> Winter { get; set; } = [];

    public bool IsSummer(DateOnly date)
    {
        var from = ParseBoundary(SummerFrom);
        var through = ParseBoundary(SummerThrough);
        var day = new DateOnly(2000, date.Month, date.Day);
        return from <= through ? day >= from && day <= through : day >= from || day <= through;
    }

    public (TimeOnly Start, TimeOnly End) Resolve(CourseItem course, DateOnly date)
        => ResolvePeriods(course, IsSummer(date) ? Summer : Winter);

    public static (TimeOnly Start, TimeOnly End) ResolvePeriods(CourseItem course, IReadOnlyList<PeriodTime> periods)
    {
        if (course.StartPeriod is null && course.EndPeriod is null)
            return (course.StartTime, course.EndTime);
        if (course.StartPeriod is not { } first || course.EndPeriod is not { } last || first > last)
            throw new ArgumentException("课程起止节次无效。");
        for (var number = first; number <= last; number++)
            if (!periods.Any(p => p.Number == number))
                throw new ArgumentException($"第 {number} 节尚未配置，请先补齐冬夏作息。");
        return (periods.Single(p => p.Number == first).Start, periods.Single(p => p.Number == last).End);
    }

    public void Validate()
    {
        _ = ParseBoundary(SummerFrom);
        _ = ParseBoundary(SummerThrough);
        foreach (var list in new[] { Summer, Winter })
        {
            if (list.Count == 0 || list.Select(p => p.Number).Distinct().Count() != list.Count)
                throw new ArgumentException("节次表不能为空或有重复编号。");
            PeriodTime? previous = null;
            foreach (var p in list.OrderBy(p => p.Number))
            {
                if (p.Number < 1 || p.Number > 30 || p.End <= p.Start || previous is not null && p.Start < previous.End)
                    throw new ArgumentException("节次须为1–30，结束须晚于开始，相邻节次不得重叠。");
                previous = p;
            }
        }
    }

    private static DateOnly ParseBoundary(string text) =>
        DateOnly.TryParseExact("2000-" + text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new ArgumentException("冬夏切换日期格式应为 MM-dd。");

    public static SeasonalTimetable CreateHnie()
    {
        string[] starts = ["08:00", "08:55", "10:10", "11:05", "14:30", "15:25", "16:40", "17:35", "19:30", "20:25"];
        var result = new SeasonalTimetable();
        for (var i = 0; i < starts.Length; i++)
        {
            var time = TimeOnly.ParseExact(starts[i], "HH:mm", CultureInfo.InvariantCulture);
            result.Summer.Add(new PeriodTime { Number = i + 1, Start = time, End = time.AddMinutes(45) });
            var winter = i < 4 ? time : time.AddMinutes(-30);
            result.Winter.Add(new PeriodTime { Number = i + 1, Start = winter, End = winter.AddMinutes(45) });
        }
        return result;
    }
}

public sealed class CourseAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public DateOnly OriginalDate { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool IsMakeup { get; set; }
}

public sealed class CourseImportSnapshot
{
    public DateTimeOffset ImportedAtUtc { get; set; }
    public List<CourseItem> Courses { get; set; } = [];
    public SemesterSettings Semester { get; set; } = new();
    public List<CourseAdjustment> Adjustments { get; set; } = [];
    public string AfterFingerprint { get; set; } = string.Empty;
}

public sealed record CourseConflict(CourseOccurrence First, CourseOccurrence Second)
{
    public string Description => $"{First.Date:yyyy-MM-dd}：{First.Course.Name} {First.StartTime:HH:mm}–{First.EndTime:HH:mm} / {Second.Course.Name} {Second.StartTime:HH:mm}–{Second.EndTime:HH:mm}";
}

public static class CourseDataCopy
{
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
