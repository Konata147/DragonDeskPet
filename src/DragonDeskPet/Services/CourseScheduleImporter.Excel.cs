using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DragonDeskPet.Core;
using ExcelDataReader;

namespace DragonDeskPet.Services;

public sealed partial class CourseScheduleImporter
{
    private static IExcelDataReader OpenExcel(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration { LeaveOpen = true });
    }

    public static IReadOnlyList<string> GetExcelSheets(string path)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new ArgumentException("课表文件超过20 MiB，请导出较小文件。");
        using var stream = File.OpenRead(path);
        using var reader = OpenExcel(stream);
        var names = new List<string>();
        do { names.Add(reader.Name); if (names.Count > 100) throw new ArgumentException("工作表超过100个，请导出单独课表。"); } while (reader.NextResult());
        return names;
    }

    private static List<CourseImportRow> ParseExcel(string path, int sheetIndex, SemesterSettings semester, CancellationToken token)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new ArgumentException("课表文件超过20 MiB，请导出较小文件。");
        using var stream = File.OpenRead(path);
        using var reader = OpenExcel(stream);
        for (var i = 0; i < sheetIndex; i++)
            if (!reader.NextResult()) throw new ArgumentException("找不到选中的工作表。");
        var cells = new List<string[]>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (cells.Count >= 5000 || reader.FieldCount > 100) throw new ArgumentException("工作表过大，仅支持5000行、100列以内的课表。");
            cells.Add(Enumerable.Range(0, reader.FieldCount).Select(c =>
                reader.GetValue(c) is DateTime dt ? dt.ToString("HH:mm", CultureInfo.InvariantCulture)
                : Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty).ToArray());
        }
        return ParseTable(cells, semester, token);
    }

    // Also shared by the explicitly requested, origin-scoped academic-page snapshot.
    public static List<CourseImportRow> ParseTable(IReadOnlyList<string[]> cells, SemesterSettings semester, CancellationToken token = default)
    {
        var rows = new List<CourseImportRow>();
        var header = -1;
        var columns = new Dictionary<int, DayOfWeek>();
        for (var r = 0; r < Math.Min(cells.Count, 20); r++)
        {
            if (cells[r].Contains("课程名") && cells[r].Contains("上课时间")) return ParseStandardTable(cells, r);
            for (var c = 0; c < cells[r].Length; c++)
                if (Regex.IsMatch(cells[r][c], "^(星期|周)[一二三四五六日天]$")) columns[c] = ParseWeekday(cells[r][c]);
            if (columns.Count >= 5) { header = r; break; }
            columns.Clear();
        }
        if (header < 0) throw new ArgumentException("未识别到课表星期表头；请选择湖南工程学院周课表或项目标准字段表。");
        for (var r = header + 1; r < cells.Count; r++)
        {
            token.ThrowIfCancellationRequested();
            foreach (var (column, day) in columns)
            {
                if (column >= cells[r].Length || string.IsNullOrWhiteSpace(cells[r][column])) continue;
                rows.AddRange(ParseCourseCell(cells[r][column], day, semester, $"第{r + 1}行 第{column + 1}列"));
            }
        }
        return rows;
    }

    public static List<CourseImportRow> ParseCourseCell(string text, DayOfWeek day, SemesterSettings semester, string source)
    {
        var result = new List<CourseImportRow>();
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var consumed = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], @"(?<weeks>\d[\d,，、\-－~～\s]*)\s*(?:\(|（)\[?(?<parity>双周|单周|周|全部)\]?(?:\)|）)\s*\[(?<start>\d+)\s*[-~～－]\s*(?<end>\d+)节\]");
            if (!match.Success) continue;
            CourseItem? course = null;
            try
            {
                if (i - consumed < 2) throw new FormatException("课程名称或教师行不完整，请核对补齐。");
                var weeks = ParseWeeks(match.Groups["weeks"].Value);
                var parity = ParsePattern(match.Groups["parity"].Value);
                weeks.RemoveWhere(w => parity == CourseWeekPattern.Odd && w % 2 == 0 || parity == CourseWeekPattern.Even && w % 2 != 0);
                if (weeks.Count == 0) throw new FormatException("周次规则未包含任何上课周。");
                course = new CourseItem { Name = string.Join(" ", lines[consumed..(i - 1)]), Teacher = lines[i - 1],
                    DayOfWeek = day, Weeks = weeks, StartWeek = weeks.Min(), EndWeek = weeks.Max(), WeekPattern = parity,
                    StartPeriod = int.Parse(match.Groups["start"].Value), EndPeriod = int.Parse(match.Groups["end"].Value),
                    Location = i + 1 < lines.Length ? lines[i + 1] : string.Empty };
                if (course.Name.Contains('…') || course.Name.Contains("...")) throw new FormatException("课程名被截断，请补齐。");
                CourseScheduleService.Validate(course, semester);
                var time = semester.Timetable.Resolve(course, semester.StartDate);
                course.StartTime = time.Start; course.EndTime = time.End;
                result.Add(new CourseImportRow(CourseImportDisposition.Add, course, source + "：" + course.Name));
            }
            catch (Exception e) when (e is ArgumentException or FormatException)
            { result.Add(new CourseImportRow(CourseImportDisposition.Invalid, course, source, e.Message)); }
            consumed = Math.Min(lines.Length, i + 2);
            i++;
        }
        if (result.Count == 0 || consumed < lines.Length)
            result.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, source,
                "备注或不完整记录，需手动补齐课程、星期、时间和周次：" + string.Join(" · ", lines.Skip(consumed))));
        return result;
    }

    public static HashSet<int> ParseWeeks(string text)
    {
        var result = new HashSet<int>();
        foreach (var part in text.Replace('，', ',').Replace('、', ',').Replace('－', '-').Replace('~', '-').Replace('～', '-').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length > 2 || !int.TryParse(range[0], out var first)) throw new FormatException("周次格式应为 1-11,14-18。");
            var last = range.Length == 1 ? first : int.TryParse(range[1], out var end) ? end : 0;
            if (first < 1 || last < first || last > 104) throw new FormatException("周次须在1–104内，结束不早于开始。");
            for (var w = first; w <= last; w++) result.Add(w);
        }
        if (result.Count == 0) throw new FormatException("请填写上课周次。");
        return result;
    }

    private static List<CourseImportRow> ParseStandardTable(IReadOnlyList<string[]> cells, int header)
    {
        var result = new List<CourseImportRow>();
        for (var r = header + 1; r < cells.Count; r++)
        {
            if (cells[r].All(string.IsNullOrWhiteSpace)) continue;
            string Read(string name)
            {
                var index = Array.IndexOf(cells[header], name);
                return index >= 0 && index < cells[r].Length ? cells[r][index] : string.Empty;
            }
            try
            {
                var course = new CourseItem { Name = Read("课程名"), DayOfWeek = ParseWeekday(Read("星期")),
                    StartTime = ExcelTime(Read("上课时间")), EndTime = ExcelTime(Read("下课时间")),
                    Teacher = Read("教师"), Location = Read("地点"), StartWeek = ParseInt(Read("开始周"), "开始周"),
                    EndWeek = ParseInt(Read("结束周"), "结束周"), WeekPattern = ParsePattern(Read("周次规则")),
                    IsEnabled = Read("是否启用") != "否", ReminderMinutes = Read("提前提醒分钟") == "关闭" ? null
                        : string.IsNullOrWhiteSpace(Read("提前提醒分钟")) ? 30 : ParseInt(Read("提前提醒分钟"), "提醒分钟") };
                CourseScheduleService.Validate(course);
                result.Add(new CourseImportRow(CourseImportDisposition.Add, course, $"第{r + 1}行：{course.Name}"));
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            { result.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, $"第{r + 1}行", e.Message)); }
        }
        return result;
    }

    private static TimeOnly ExcelTime(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0 && number < 1
        ? TimeOnly.FromTimeSpan(TimeSpan.FromDays(number)) : ParseTime(value);
}
