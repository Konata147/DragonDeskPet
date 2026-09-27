using System.Globalization;
using System.IO;
using DragonDeskPet.Core;
using Ical.Net;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using Microsoft.VisualBasic.FileIO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace DragonDeskPet.Services;

public sealed partial class CourseScheduleImporter : ICourseScheduleImporter
{
    private readonly IProductivityStore _store;

    public CourseScheduleImporter(IProductivityStore store) => _store = store;

    public Task<CourseImportPreview> PreviewAsync(string path, IReadOnlyList<CourseItem> existingCourses, SemesterSettings semester,
        int sheetIndex = 0, CancellationToken cancellationToken = default) => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Path.GetExtension(path).ToLowerInvariant() is ".xls" or ".xlsx"
                ? Classify(ParseExcel(path, sheetIndex, semester, cancellationToken), existingCourses)
                : Preview(path, existingCourses, semester);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }, cancellationToken);

    public static CourseImportPreview Classify(IEnumerable<CourseImportRow> rows, IReadOnlyList<CourseItem> existingCourses)
    {
        var preview = new CourseImportPreview();
        var seen = existingCourses.ToList();
        var batchIds = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row.Course is not { } course || row.Disposition == CourseImportDisposition.Invalid)
            { preview.Rows.Add(row); continue; }
            var existing = FindExisting(seen, course);
            var disposition = existing is null ? CourseImportDisposition.Add
                : IsEquivalent(existing, course) ? CourseImportDisposition.Duplicate : CourseImportDisposition.Update;
            if (existing is not null) course.Id = existing.Id;
            if (disposition == CourseImportDisposition.Update && batchIds.Contains(course.Id))
            {
                preview.Rows.Add(row with { Disposition = CourseImportDisposition.Invalid,
                    ErrorMessage = "同一批导入有相同课程时段但不同内容，请核对后排除其中一条。" });
                continue;
            }
            preview.Rows.Add(row with { Disposition = disposition });
            batchIds.Add(course.Id);
            seen.RemoveAll(c => c.Id == course.Id);
            seen.Add(course);
        }
        return preview;
    }

    public string ScheduleFingerprint()
    {
        var courses = CourseDataCopy.Clone(_store.Data.Courses);
        foreach (var c in courses) c.LastAlertedDate = null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Courses = courses, _store.Data.Semester, _store.Data.CourseAdjustments }))));
    }

    private void SaveOrRestore(ProductivityData before)
    {
        try { _store.Save(); }
        catch
        {
            _store.Data.Courses = before.Courses;
            _store.Data.Semester = before.Semester;
            _store.Data.CourseAdjustments = before.CourseAdjustments;
            _store.Data.PendingAlerts = before.PendingAlerts;
            _store.Data.LastCourseImport = before.LastCourseImport;
            _store.Data.LastCourseSchedulerCheckUtc = before.LastCourseSchedulerCheckUtc;
            _store.Data.NotifiedCourseOccurrences = before.NotifiedCourseOccurrences;
            throw;
        }
    }

    public void Undo()
    {
        var snapshot = _store.Data.LastCourseImport ?? throw new InvalidOperationException("没有可撤销的导入。");
        var before = CourseDataCopy.Clone(_store.Data);
        // Preserve notification history outside the schedule snapshot; never revive old alerts.
        foreach (var course in _store.Data.Courses.Where(c => c.LastAlertedDate is not null))
            _store.Data.NotifiedCourseOccurrences.Add($"{course.Id:N}:{course.LastAlertedDate:yyyy-MM-dd}");
        _store.Data.Courses = CourseDataCopy.Clone(snapshot.Courses);
        _store.Data.Semester = CourseDataCopy.Clone(snapshot.Semester);
        _store.Data.CourseAdjustments = CourseDataCopy.Clone(snapshot.Adjustments);
        _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course);
        _store.Data.LastCourseSchedulerCheckUtc = DateTimeOffset.UtcNow;
        _store.Data.LastCourseImport = null;
        SaveOrRestore(before);
    }

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
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new ArgumentException("课表文件超过20 MiB，请导出较小文件。");
            var imported = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".ics" => ParseIcs(path, semester),
                ".csv" => ParseCsv(path),
                ".xls" or ".xlsx" => ParseExcel(path, 0, semester, CancellationToken.None),
                _ => throw new ArgumentException("不支持此文件格式，请选择 ICS、CSV、XLS 或 XLSX。")
            };
            return Classify(imported, existingCourses);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, Path.GetFileName(path),
                CourseImportErrors.Describe(exception)));
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

        foreach (var course in accepted) CourseScheduleService.Validate(course, _store.Data.Semester);
        var before = CourseDataCopy.Clone(_store.Data);
        var snapshot = new CourseImportSnapshot
        {
            ImportedAtUtc = DateTimeOffset.UtcNow,
            Courses = CourseDataCopy.Clone(_store.Data.Courses),
            Semester = CourseDataCopy.Clone(_store.Data.Semester),
            Adjustments = CourseDataCopy.Clone(_store.Data.CourseAdjustments)
        };

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
        var updatedIds = accepted.Select(c => c.Id).ToHashSet();
        _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course && a.SourceId is { } id && updatedIds.Contains(id));
        _store.Data.CourseAdjustments.RemoveAll(a => !nextCourses.Any(c => c.Id == a.CourseId));
        if (replaceCurrentSemester)
        {
            var retainedIds = nextCourses.Select(course => course.Id).ToHashSet();
            _store.Data.PendingAlerts.RemoveAll(alert =>
                alert.Source == AlertSource.Course
                && alert.SourceId is { } sourceId
                && !retainedIds.Contains(sourceId));
        }

        snapshot.AfterFingerprint = ScheduleFingerprint();
        _store.Data.LastCourseImport = snapshot;
        SaveOrRestore(before);
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
        var byId = courses.FirstOrDefault(c => c.Id == candidate.Id);
        if (byId is not null) return byId;
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
            && course.StartWeek == candidate.StartWeek && course.EndWeek == candidate.EndWeek
            && course.WeekPattern == candidate.WeekPattern && course.Weeks.SetEquals(candidate.Weeks)
            && course.IncludedDates.SetEquals(candidate.IncludedDates)
            && course.StartPeriod == candidate.StartPeriod && course.EndPeriod == candidate.EndPeriod
            && (course.StartPeriod is not null || course.StartTime == candidate.StartTime && course.EndTime == candidate.EndTime));
    }

    private static bool IsEquivalent(CourseItem left, CourseItem right) =>
        left.Name == right.Name
        && left.DayOfWeek == right.DayOfWeek
        && left.StartTime == right.StartTime
        && left.EndTime == right.EndTime
        && left.Location == right.Location
        && TeacherNames(left.Teacher).SetEquals(TeacherNames(right.Teacher))
        && left.IsEnabled == right.IsEnabled && left.ReminderMinutes == right.ReminderMinutes
        && left.StartWeek == right.StartWeek
        && left.EndWeek == right.EndWeek
        && left.WeekPattern == right.WeekPattern
        && left.StartPeriod == right.StartPeriod && left.EndPeriod == right.EndPeriod
        && left.Weeks.SetEquals(right.Weeks)
        && left.IncludedDates.SetEquals(right.IncludedDates);

    // A list of co-teachers is unordered. Normalize only explicit list separators;
    // retain spaces within a name, and leave the original display text untouched.
    private static HashSet<string> TeacherNames(string value) => value
        .Split([',', '，', '、', ';', '；', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .ToHashSet(StringComparer.Ordinal);

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
