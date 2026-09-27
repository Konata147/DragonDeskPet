using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DragonDeskPet.Core;
using RapidOcrNet;
using SkiaSharp;

namespace DragonDeskPet.Services;

public sealed record CourseOcrLine(string Text, float Left, float Top, float Right, float Bottom, double Confidence)
{
    public float X => (Left + Right) / 2;
    public float Y => (Top + Bottom) / 2;
}

public interface ICourseImageImporter
{
    Task<CourseImportPreview> PreviewAsync(string path, SemesterSettings semester, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class CourseImageImporter : ICourseImageImporter
{
    public Task<CourseImportPreview> PreviewAsync(string path, SemesterSettings semester, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new ArgumentException("图片超过20 MiB，请裁剪后导入。");
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream) ?? throw new ArgumentException("图片无法解码，请选择 PNG 或 JPG。");
            if (codec.Info.Width * (long)codec.Info.Height > 32_000_000) throw new ArgumentException("图片超过3200万像素，请裁剪后导入。");
            using var bitmap = SKBitmap.Decode(codec) ?? throw new ArgumentException("图片内容无效。");
            var models = Path.Combine(AppContext.BaseDirectory, "models", "v5");
            var chinese = Path.Combine(AppContext.BaseDirectory, "assets", "ocr");
            progress?.Report("正在校验并加载本地中文识别模型");
            Verify(Path.Combine(chinese, "ch_PP-OCRv5_rec_mobile.onnx"), "5825FC7EBF84AE7A412BE049820B4D86D77620F204A041697B0494669B1742C5");
            Verify(Path.Combine(chinese, "ppocrv5_dict.txt"), "D1979E9F794C464C0D2E0B70A7FE14DD978E9DC644C0E71F14158CDF8342AF1B");
            using var engine = new RapidOcr();
            engine.InitModels(detPath: Path.Combine(models, "ch_PP-OCRv5_mobile_det.onnx"),
                clsPath: Path.Combine(models, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                recPath: Path.Combine(chinese, "ch_PP-OCRv5_rec_mobile.onnx"), keysPath: Path.Combine(chinese, "ppocrv5_dict.txt"));
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("正在本地识别文字与位置，不会上传图片");
            var result = await engine.DetectAsync(bitmap, RapidOcrOptions.Default with { ImgResize = 2048, TextScore = 0.3f },
                new Progress<(int Completed, int Total)>(p => progress?.Report($"正在识别文字 {p.Completed}/{p.Total}")), cancellationToken);
            var lines = result.TextBlocks.Select(b => new CourseOcrLine(b.Text, b.BoxPoints.Min(p => p.X), b.BoxPoints.Min(p => p.Y),
                b.BoxPoints.Max(p => p.X), b.BoxPoints.Max(p => p.Y), b.CharScores?.Average() ?? 0)).ToList();
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("正在整理课表，随后请核对识别结果");
            return ParseLines(lines, semester);
        }, cancellationToken);

    private static void Verify(string path, string expected)
    {
        if (!File.Exists(path)) throw new ArgumentException("中文识别模型缺失，请重新解压完整测试包（无需 API）。");
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("中文模型校验失败，请重新解压完整测试包。");
    }

    public static CourseImportPreview ParseLines(IReadOnlyList<CourseOcrLine> lines, SemesterSettings semester)
    {
        var preview = new CourseImportPreview();
        var days = lines.Where(l => Regex.IsMatch(l.Text.Trim(), "^(星期|周)[一二三四五六日天]$")).OrderBy(l => l.X).ToList();
        var periods = lines.Where(l => Regex.IsMatch(l.Text, "第[一二三四五六]大节")).OrderBy(l => l.Y).ToList();
        if (days.Count < 5 || periods.Count < 2)
        {
            preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, "图片布局未识别",
                "请提供包含星期表头和完整节次的正向周课表截图，或使用 Excel 文件导入。"));
            return preview;
        }
        var dayMap = new Dictionary<char, DayOfWeek> { ['一'] = DayOfWeek.Monday, ['二'] = DayOfWeek.Tuesday,
            ['三'] = DayOfWeek.Wednesday, ['四'] = DayOfWeek.Thursday, ['五'] = DayOfWeek.Friday, ['六'] = DayOfWeek.Saturday, ['日'] = DayOfWeek.Sunday, ['天'] = DayOfWeek.Sunday };
        for (var d = 0; d < days.Count; d++)
        for (var r = 0; r < periods.Count; r++)
        {
            var left = d == 0 ? (periods[0].X + days[d].X) / 2 : (days[d - 1].X + days[d].X) / 2;
            var right = d + 1 == days.Count ? days[d].X + (days[d].X - days[d - 1].X) / 2 : (days[d].X + days[d + 1].X) / 2;
            var top = r == 0 ? Math.Max(days[0].Bottom, periods[0].Y - (periods[1].Y - periods[0].Y) / 2) : (periods[r - 1].Y + periods[r].Y) / 2;
            var bottom = r + 1 == periods.Count ? periods[r].Y + (periods[r].Y - periods[r - 1].Y) / 2 : (periods[r].Y + periods[r + 1].Y) / 2;
            var group = lines.Where(l => l.X >= left && l.X < right && l.Y >= top && l.Y < bottom).OrderBy(l => l.Y).ToList();
            if (group.Count == 0) continue;
            var normalized = group.Select(l => NormalizeLine(l.Text)).ToArray();
            var day = dayMap[days[d].Text.Trim()[^1]];
            var parsed = CourseScheduleImporter.ParseCourseCell(string.Join("\n", normalized), day, semester, $"图片 {days[d].Text} {periods[r].Text}");
            foreach (var row in parsed)
            {
                var draft = row.Course;
                if (draft is null)
                {
                    draft = new CourseItem { Name = normalized[0], DayOfWeek = day, StartWeek = 0, EndWeek = 0,
                        Teacher = normalized.FirstOrDefault(x => group.Any(l => l.Text.Contains("教师") && NormalizeLine(l.Text) == x)) ?? string.Empty };
                    var period = Regex.Match(string.Join(" ", group.Select(l => l.Text)), @"(?<first>\d+)\s*[~～－-]\s*(?<last>\d+)(?:小)?节");
                    if (period.Success)
                    {
                        draft.StartPeriod = int.Parse(period.Groups["first"].Value);
                        draft.EndPeriod = int.Parse(period.Groups["last"].Value);
                    }
                }
                // OCR guesses never become silent, valid schedules: each candidate must be reviewed.
                preview.Rows.Add(row with { Course = draft, Disposition = CourseImportDisposition.Invalid,
                    ErrorMessage = (row.ErrorMessage ?? "识图结果待核对，请点击“编辑所选”确认周次与时间。")
                        + (group.Any(l => l.Confidence < 0.85) ? " 存在低置信度文字。" : "") });
            }
        }
        return preview;
    }

    public static string NormalizeLine(string text)
    {
        text = text.Trim().Replace("教师：", "").Replace("教师:", "").Replace("地点：", "").Replace("教室：", "");
        var m = Regex.Match(text, @"(?<first>\d+)\s*[~～－-]\s*(?<last>\d+)(?:小)?节\s*第(?<weeks>[\d,，、\s~～－-]+)周[（(](?<parity>全部|单周|双周)[）)]");
        return m.Success ? $"{m.Groups["weeks"].Value}([{(m.Groups["parity"].Value == "全部" ? "周" : m.Groups["parity"].Value)}])[{m.Groups["first"].Value}-{m.Groups["last"].Value}节]" : text;
    }
}
