using System.Globalization;
using System.IO;
using DragonDeskPet.Core;
using Ical.Net;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using Microsoft.VisualBasic.FileIO;

namespace DragonDeskPet.Services;

public sealed class CourseScheduleImporter : ICourseScheduleImporter
{
    private readonly IProductivityStore _store;

    public CourseScheduleImporter(IProductivityStore store) => _store = store;

    public CourseImportPreview Preview(string path, IReadOnlyList<CourseItem> existingCourses, SemesterSettings semester)
    {
        var preview = new CourseImportPreview();
        if (!File.Exists(path))
        {
            preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, path, "找不到导入文件。"));
            return preview;
        }

        try
        {
            var imported = Path.GetExtension(path).Equals(".ics", StringComparison.OrdinalIgnoreCase)
                ? ParseIcs(path, semester)
                : ParseCsv(path);
            foreach (var result in imported)
            {
                if (result.Course is null)
                {
                    preview.Rows.Add(result);
                    continue;
                }

                var existing = FindExisting(existingCourses, result.Course);
                var disposition = existing is null
                    ? CourseImportDisposition.Add
                    : IsEquivalent(existing, result.Course)
                        ? CourseImportDisposition.Duplicate
                        : CourseImportDisposition.Update;
                if (existing is not null)
                {
                    result.Course.Id = existing.Id;
                }

                preview.Rows.Add(result with { Disposition = disposition });
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, Path.GetFileName(path), exception.Message));
        }

        return preview;
    }

    public void Apply(CourseImportPreview preview, bool replaceCurrentSemester)
    {
        var accepted = preview.Rows
            .Where(row => row.Course is not null
                && (row.Disposition is CourseImportDisposition.Add or CourseImportDisposition.Update
                    || replaceCurrentSemester && row.Disposition == CourseImportDisposition.Duplicate))
            .Select(row => row.Course!)
            .ToList();
        if (accepted.Count == 0)
        {
            return;
        }

        var nextCourses = replaceCurrentSemester
            ? new List<CourseItem>()
            : _store.Data.Courses.ToList();

        foreach (var course in accepted)
        {
            var existing = FindExisting(nextCourses, course);
            if (existing is null)
            {
                nextCourses.Add(course);
            }
            else
            {
                course.Id = existing.Id;
                if (!replaceCurrentSemester)
                {
                    course.ExcludedDates = new HashSet<DateOnly>(existing.ExcludedDates);
                    course.LastAlertedDate = existing.LastAlertedDate;
                }

                nextCourses[nextCourses.IndexOf(existing)] = course;
            }
        }

        _store.Data.Courses = nextCourses;
        if (replaceCurrentSemester)
        {
            var retainedIds = nextCourses.Select(course => course.Id).ToHashSet();
            _store.Data.PendingAlerts.RemoveAll(alert =>
                alert.Source == AlertSource.Course
                && alert.SourceId is { } sourceId
                && !retainedIds.Contains(sourceId));
        }

        _store.Save();
    }

    private static List<CourseImportRow> ParseIcs(string path, SemesterSettings semester)
    {
        var calendar = Ical.Net.Calendar.Load(File.ReadAllText(path))
            ?? throw new InvalidDataException("ICS 文件内容无效。");
        var rows = new List<CourseImportRow>();
        var rangeStart = semester.StartDate.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = rangeStart.AddDays(7 * 32);
        foreach (var calendarEvent in calendar.Events)
        {
            try
            {
                var occurrences = calendarEvent
                    .GetOccurrences(new CalDateTime(rangeStart, false), new EvaluationOptions { MaxUnmatchedIncrementsLimit = 500 })
                    .Take(1000)
                    .Select(occurrence => new
                    {
                        Start = ToLocal(occurrence.Period.StartTime!),
                        End = ToLocal(occurrence.Period.EffectiveEndTime ?? occurrence.Period.StartTime!)
                    })
                    .Where(occurrence => occurrence.Start < rangeEnd && occurrence.End > rangeStart)
                    .ToList();
                if (occurrences.Count == 0)
                {
                    continue;
                }

                var first = occurrences[0];
                if (first.End <= first.Start || calendarEvent.IsAllDay)
                {
                    rows.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, calendarEvent.Summary ?? "未命名日历事件", "全天事件或时间范围无效，未作为课程导入。"));
                    continue;
                }

                var dates = occurrences.Select(item => DateOnly.FromDateTime(item.Start)).ToHashSet();
                var firstDate = dates.Min();
                var lastDate = dates.Max();
                var course = new CourseItem
                {
                    ExternalId = calendarEvent.Uid ?? string.Empty,
                    Name = string.IsNullOrWhiteSpace(calendarEvent.Summary) ? "未命名课程" : calendarEvent.Summary.Trim(),
                    DayOfWeek = first.Start.DayOfWeek,
                    StartTime = TimeOnly.FromDateTime(first.Start),
                    EndTime = TimeOnly.FromDateTime(first.End),
                    Location = calendarEvent.Location?.Trim() ?? string.Empty,
                    StartWeek = Math.Max(1, (firstDate.DayNumber - semester.StartDate.DayNumber) / 7 + 1),
                    EndWeek = Math.Max(1, (lastDate.DayNumber - semester.StartDate.DayNumber) / 7 + 1),
                    IncludedDates = dates,
                    ReminderMinutes = 30
                };
                rows.Add(new CourseImportRow(CourseImportDisposition.Add, course, course.Name));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                rows.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, calendarEvent.Summary ?? "未命名日历事件", exception.Message));
            }
        }

        return rows;
    }

    private static List<CourseImportRow> ParseCsv(string path)
    {
        using var parser = new TextFieldParser(path, System.Text.Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("CSV 文件缺少表头。");
        var indexes = headers.Select((header, index) => (header: header.Trim(), index))
            .ToDictionary(item => item.header, item => item.index, StringComparer.OrdinalIgnoreCase);
        var rows = new List<CourseImportRow>();
        var line = 1;
        while (!parser.EndOfData)
        {
            line++;
            try
            {
                var fields = parser.ReadFields() ?? [];
                string Read(string name, bool required = false)
                {
                    if (!indexes.TryGetValue(name, out var index) || index >= fields.Length)
                    {
                        if (required)
                        {
                            throw new FormatException($"缺少必填列“{name}”。");
                        }

                        return string.Empty;
                    }

                    return fields[index].Trim();
                }

                var name = Read("课程名", true);
                var weekday = ParseWeekday(Read("星期", true));
                var start = ParseTime(Read("上课时间", true));
                var end = ParseTime(Read("下课时间", true));
                var startWeek = ParseInt(Read("开始周", true), "开始周");
                var endWeek = ParseInt(Read("结束周", true), "结束周");
                if (end <= start || startWeek < 1 || endWeek < startWeek)
                {
                    throw new FormatException("时间或周次范围无效。");
                }

                var reminderText = Read("提前提醒分钟");
                var course = new CourseItem
                {
                    Name = name,
                    DayOfWeek = weekday,
                    StartTime = start,
                    EndTime = end,
                    Location = Read("地点"),
                    Teacher = Read("教师"),
                    StartWeek = startWeek,
                    EndWeek = endWeek,
                    WeekPattern = ParsePattern(Read("周次规则")),
                    IsEnabled = !Read("是否启用").Equals("否", StringComparison.OrdinalIgnoreCase),
                    ReminderMinutes = reminderText.Equals("关闭", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : string.IsNullOrWhiteSpace(reminderText) ? 30 : ParseInt(reminderText, "提前提醒分钟")
                };
                rows.Add(new CourseImportRow(CourseImportDisposition.Add, course, $"第 {line} 行：{name}"));
            }
            catch (Exception exception) when (exception is FormatException or MalformedLineException)
            {
                rows.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, $"第 {line} 行", exception.Message));
            }
        }

        return rows;
    }

    private static DateTime ToLocal(CalDateTime value)
    {
        if (value.IsFloating)
        {
            return DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        }

        var utc = DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
    }

    private static CourseItem? FindExisting(IEnumerable<CourseItem> courses, CourseItem candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ExternalId))
        {
            var byExternalId = courses.FirstOrDefault(course => course.ExternalId == candidate.ExternalId);
            if (byExternalId is not null)
            {
                return byExternalId;
            }
        }

        return courses.FirstOrDefault(course =>
            course.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)
            && course.DayOfWeek == candidate.DayOfWeek
            && course.StartTime == candidate.StartTime
            && course.EndTime == candidate.EndTime);
    }

    private static bool IsEquivalent(CourseItem left, CourseItem right) =>
        left.Name == right.Name
        && left.DayOfWeek == right.DayOfWeek
        && left.StartTime == right.StartTime
        && left.EndTime == right.EndTime
        && left.Location == right.Location
        && left.Teacher == right.Teacher
        && left.StartWeek == right.StartWeek
        && left.EndWeek == right.EndWeek
        && left.WeekPattern == right.WeekPattern
        && left.IncludedDates.SetEquals(right.IncludedDates);

    private static int ParseInt(string value, string field) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"{field}不是有效数字。");

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.TryParseExact(value.Replace('：', ':'), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new FormatException("课程时间格式应为 HH:mm。 ");

    private static DayOfWeek ParseWeekday(string value) => value.Trim().Replace("星期", string.Empty).Replace("周", string.Empty) switch
    {
        "一" or "1" => DayOfWeek.Monday,
        "二" or "2" => DayOfWeek.Tuesday,
        "三" or "3" => DayOfWeek.Wednesday,
        "四" or "4" => DayOfWeek.Thursday,
        "五" or "5" => DayOfWeek.Friday,
        "六" or "6" => DayOfWeek.Saturday,
        "日" or "天" or "7" => DayOfWeek.Sunday,
        _ => throw new FormatException("星期应为一至日或 1 至 7。")
    };

    private static CourseWeekPattern ParsePattern(string value) => value.Trim() switch
    {
        "单周" or "单" => CourseWeekPattern.Odd,
        "双周" or "双" => CourseWeekPattern.Even,
        _ => CourseWeekPattern.All
    };
}
