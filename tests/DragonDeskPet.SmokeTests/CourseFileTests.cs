using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DragonDeskPet.Core;
using DragonDeskPet.Services;

internal static class CourseFileTests
{
    public static void LockedFile()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "locked.xls");
            using var held = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try { CourseScheduleImporter.GetExcelSheets(path); throw new Exception("Locked file unexpectedly read."); }
            catch (IOException e)
            {
                var message = CourseImportErrors.Describe(e);
                if (!message.Contains("WPS/Excel") || !message.Contains("现有课表未改变") || message.Contains(path))
                    throw new Exception("Locked-file error lacks safe, actionable feedback.");
            }
            var store = new ProductivityStore(directory);
            var preview = new CourseScheduleImporter(store).Preview(path, [], store.Data.Semester);
            if (preview.InvalidCount != 1 || !preview.Rows[0].ErrorMessage!.Contains("占用") || File.Exists(store.DataPath))
                throw new Exception("Locked import must preserve data and explain the failure.");
        }
        finally { Directory.Delete(directory, true); }
    }
    public static async Task ExcelAsync()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "synthetic.xlsx");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Add(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8); writer.Write(content); }
                Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/></Types>");
                Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Add("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"周课表\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"标准字段\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
                string Sheet(string[][] values, bool merged)
                {
                    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    var root = new XElement(ns + "worksheet", new XElement(ns + "sheetData", values.Select((row, r) => new XElement(ns + "row", new XAttribute("r", r + 1),
                        row.Select((text, c) => new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + c)}{r + 1}"), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", text))))))));
                    if (merged) root.Add(new XElement(ns + "mergeCells", new XElement(ns + "mergeCell", new XAttribute("ref", "A1:F1"))));
                    return root.ToString();
                }
                Add("xl/worksheets/sheet1.xml", Sheet([
                    ["脱敏合并表头"], ["节次", "星期一", "星期二", "星期三", "星期四", "星期五"],
                    ["第1大节", "测试课\n甲教师,乙教师\n1-3,7-9([单周])[01-02节]\nA101", "", "", "", ""]], true));
                Add("xl/worksheets/sheet2.xml", Sheet([
                    ["课程名", "星期", "上课时间", "下课时间", "地点", "教师", "开始周", "结束周", "周次规则"],
                    ["测试行式", "三", "14:00", "15:40", "B,203", "测试教师", "1", "16", "双周"]], false));
            }
            var store = new ProductivityStore(directory);
            var importer = new CourseScheduleImporter(store);
            if (CourseScheduleImporter.GetExcelSheets(path).Count != 2) throw new Exception("multi-sheet listing");
            var first = await importer.PreviewAsync(path, [], store.Data.Semester);
            if (first.AddedCount != 1 || !first.Rows[0].Course!.Weeks.SetEquals([1,3,7,9]) || !first.Rows[0].Course!.Teacher.Contains(',')) throw new Exception("xlsx grid merge or multiple teachers");
            var second = await importer.PreviewAsync(path, [], store.Data.Semester, 1);
            if (second.AddedCount != 1 || second.Rows[0].Course!.StartPeriod is not null || second.Rows[0].Course!.Location != "B,203") throw new Exception("row xlsx selection");
            if (File.Exists(store.DataPath)) throw new Exception("preview persisted data");
            importer.Apply(first, false);
            if ((await importer.PreviewAsync(path, store.Data.Courses, store.Data.Semester)).DuplicateCount != 1) throw new Exception("repeat xlsx import");
            var unknown = Path.Combine(directory, "unknown.dat"); File.WriteAllText(unknown, "课程名,星期");
            if (importer.Preview(unknown, [], store.Data.Semester).InvalidCount != 1) throw new Exception("unknown extension fallback");
            var bad = Path.Combine(directory, "bad.xls"); File.WriteAllText(bad, "not an excel workbook");
            if (importer.Preview(bad, [], store.Data.Semester).InvalidCount != 1) throw new Exception("damaged xls accepted");
            var a = first.Rows[0].Course!;
            var b = CourseDataCopy.Clone(a); b.Id = Guid.NewGuid(); b.StartWeek = 10; b.EndWeek = 12; b.Weeks = [11]; b.Location = "另一教室";
            var batch = CourseScheduleImporter.Classify([new(CourseImportDisposition.Add, a, "a"), new(CourseImportDisposition.Add, b, "b")], []);
            if (batch.AddedCount != 2) throw new Exception("different week arrangements collapsed");
        }
        finally { Directory.Delete(directory, true); }
    }

    public static async Task OcrAndRuntimeAsync()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await new CourseImageImporter().PreviewAsync("not-opened.png", new SemesterSettings(), cancellationToken: cancelled.Token); throw new Exception("OCR cancel ignored"); }
        catch (OperationCanceledException) { }
        var lines = new List<CourseOcrLine>();
        var names = new[] { "星期一", "星期二", "星期三", "星期四", "星期五" };
        for (var i = 0; i < 5; i++) lines.Add(new(names[i], 100 + i * 100, 10, 160 + i * 100, 30, .99));
        lines.Add(new("第一大节", 0, 100, 60, 120, .99)); lines.Add(new("第二大节", 0, 250, 60, 270, .99));
        lines.Add(new("课程…", 100, 60, 160, 80, .7)); lines.Add(new("教师：测试", 100, 85, 160, 105, .99));
        lines.Add(new("01~02小节 第1-3周(全部)", 100, 120, 160, 140, .99));
        var preview = CourseImageImporter.ParseLines(lines, new SemesterSettings());
        if (preview.Rows.Count != 1 || preview.InvalidCount != 1 || preview.Rows[0].Course?.StartPeriod != 1 || !preview.Rows[0].ErrorMessage!.Contains("低置信度")) throw new Exception("uncertain OCR promoted or lost");
        var url = PortableWebViewRuntime.FindOfficialPackage(@"https:\u002F\u002Fmsedge.sf.dl.delivery.mp.microsoft.com\u002Ffilestreamingservice\u002Ffiles\u002Faaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\u002FMicrosoft.WebView2.FixedVersionRuntime.1.2.3.4.x64.cab");
        if (url.Host != "msedge.sf.dl.delivery.mp.microsoft.com") throw new Exception("untrusted runtime package");
        try { PortableWebViewRuntime.FindOfficialPackage("https://evil.example/runtime.cab"); throw new Exception("untrusted runtime accepted"); }
        catch (ArgumentException) { }
    }
}
