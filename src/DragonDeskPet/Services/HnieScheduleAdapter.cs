using System.Text.Json;
using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public interface ISchoolScheduleAdapter
{
    bool IsAllowed(Uri uri);
    CourseImportPreview ParseSnapshot(string json, SemesterSettings semester, IReadOnlyList<CourseItem> existing);
}

public sealed class HnieScheduleAdapter : ISchoolScheduleAdapter
{
    public const string EntryUrl = "https://jwcmis.hnie.edu.cn/jsxsd/framework/xsMainV.htmlx";
    public bool IsAllowed(Uri uri) => uri.Scheme == "https" && uri.Host.Equals("jwcmis.hnie.edu.cn", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort && uri.UserInfo.Length == 0;

    public CourseImportPreview ParseSnapshot(string json, SemesterSettings semester, IReadOnlyList<CourseItem> existing)
    {
        if (json.Length > 2_000_000) throw new ArgumentException("课表页面内容过大，请先打开单学期的个人课表。");
        var tables = JsonSerializer.Deserialize<List<string[][]>>(json) ?? [];
        var candidates = new List<CourseImportRow>();
        foreach (var table in tables)
        {
            if (table.Length > 500 || table.Any(r => r.Length > 40)) continue;
            try
            {
                var normalized = table.Select(row => row.Select(cell => string.Join("\n", cell.Split('\n').Select(CourseImageImporter.NormalizeLine))).ToArray()).ToArray();
                var rows = CourseScheduleImporter.ParseTable(normalized, semester);
                if (rows.Any(r => r.Course is not null)) candidates.AddRange(rows);
            }
            catch (ArgumentException) { }
        }
        if (!candidates.Any(r => r.Course is not null))
            throw new ArgumentException("未读到受支持的完整课表。请登录并打开个人学期课表；若页面已改版，请导出 XLS 后使用文件导入。现有课表未改变。");
        return CourseScheduleImporter.Classify(candidates, existing);
    }

    // Only reads table cells from the selected school's same-origin documents.
    // No form fields, cookies, requests, scripts or raw page HTML are returned.
    public const string SnapshotScript = """
        (() => {
          if (location.origin !== 'https://jwcmis.hnie.edu.cn') return [];
          const output = [];
          function read(doc, depth) {
            if (depth > 4 || output.length > 16) return;
            for (const table of doc.querySelectorAll('table')) {
              if (table.rows.length > 500) continue;
              const matrix = [];
              Array.from(table.rows).forEach((row, r) => {
                matrix[r] ||= []; let c = 0;
                for (const cell of row.cells) {
                  while (matrix[r][c] !== undefined) c++;
                  if (c >= 40) break;
                  const text = (cell.innerText || '').trim().slice(0, 8000);
                  const rs = Math.min(cell.rowSpan || 1, 30), cs = Math.min(cell.colSpan || 1, 20);
                  for (let y = 0; y < rs; y++) {
                    matrix[r+y] ||= [];
                    for (let x = 0; x < cs; x++) matrix[r+y][c+x] = y === 0 && x === 0 ? text : '';
                  }
                  c += cs;
                }
              });
              if (matrix.some(row => row.filter(text => /^(星期|周)[一二三四五六日天]$/.test(text)).length >= 5))
                output.push(matrix.map(row => Array.from(row, cell => cell || '')));
            }
            for (const frame of doc.querySelectorAll('iframe,frame')) {
              try { if (frame.contentWindow.location.origin === location.origin) read(frame.contentDocument, depth + 1); } catch {}
            }
          }
          read(document, 0); return output;
        })()
        """;
}
