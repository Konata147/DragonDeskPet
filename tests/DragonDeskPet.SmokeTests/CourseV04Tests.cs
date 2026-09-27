using DragonDeskPet.Core;
using DragonDeskPet.Services;

internal static class CourseV04Tests
{
    public static void BoundariesAndAlerts()
    {
        var store = new MemoryStore();
        store.Data.Semester.StartDate = new DateOnly(2026, 9, 6);
        var service = new CourseScheduleService(store);
        var course = new CourseItem { Name = "测试", DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0), StartWeek = 1, EndWeek = 20 };
        service.AddOrUpdate(course);
        if (service.GetTeachingWeek(new DateOnly(2026, 9, 13)) != 2) throw new Exception("Sunday week boundary");
        var change = new CourseAdjustment { CourseId = course.Id, OriginalDate = new DateOnly(2026, 9, 13), Date = new DateOnly(2026, 9, 14),
            Start = new TimeOnly(10, 0), End = new TimeOnly(11, 0) };
        service.SaveAdjustment(change);
        service.Tick(new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 13))));
        if (store.Data.PendingAlerts.Count != 0) throw new Exception("moved course alerted on original day");
        var due = new DateTimeOffset(2026, 9, 14, 9, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 14)));
        service.Tick(due); service.Tick(due.AddMinutes(1));
        if (store.Data.PendingAlerts.Count != 1) throw new Exception("adjustment reminder duplicate");
        store.Data.PendingAlerts.Clear(); service.Tick(due.AddMinutes(2));
        if (store.Data.PendingAlerts.Count != 0) throw new Exception("completed alert repeated");
        var invalid = CourseScheduleImporter.ParseCourseCell("测试\n教师\n1-2([周])[09-11节]\n地点", DayOfWeek.Monday, store.Data.Semester, "test");
        if (invalid.Single().Disposition != CourseImportDisposition.Invalid) throw new Exception("unknown period accepted");
        if (CourseScheduleImporter.Classify(invalid, []).AddedCount != 0) throw new Exception("invalid promoted to valid");
        var adapter = new HnieScheduleAdapter();
        if (!adapter.IsAllowed(new Uri(HnieScheduleAdapter.EntryUrl)) || adapter.IsAllowed(new Uri("https://jwcmis.hnie.edu.cn.evil.example/"))
            || adapter.IsAllowed(new Uri("http://jwcmis.hnie.edu.cn/"))) throw new Exception("school origin boundary");
        try { adapter.ParseSnapshot("[]", store.Data.Semester, []); throw new Exception("empty page accepted"); }
        catch (ArgumentException) { }
    }

    public static void SaveFailureRollsBack()
    {
        var store = new MemoryStore();
        store.Data.Courses.Add(new CourseItem { Name = "旧课", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0) });
        var preview = new CourseImportPreview();
        preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Add, new CourseItem { Name = "新课", StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0) }, "test"));
        store.ThrowOnSave = true;
        try { new CourseScheduleImporter(store).Apply(preview, true); throw new Exception("write failure swallowed"); }
        catch (IOException) { }
        if (store.Data.Courses.Single().Name != "旧课" || store.Data.LastCourseImport is not null) throw new Exception("memory changed after failed write");
    }

    private sealed class MemoryStore : IProductivityStore
    {
        public ProductivityData Data { get; } = new();
        public string DataPath => "memory-only";
        public string? RecoveryNotice => null;
        public bool ThrowOnSave { get; set; }
        public void Save() { if (ThrowOnSave) throw new IOException("simulated disk failure"); }
    }
    public static void ImportAndUndo()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProductivityStore(directory);
            store.Data.Semester.StartDate = new DateOnly(2026, 9, 6);
            store.Data.Todos.Add(new TodoItem { Text = "保留待办" });
            var rows = CourseScheduleImporter.ParseTable([
                ["节次", "星期一", "星期二", "星期三", "星期四", "星期五"],
                ["第1大节", "测试课程\n测试教师\n1-11,14-18([双周])[01-02节]\n测试楼101", "", "", "", ""],
                ["备注", "实训12-13周", "", "", "", ""]], store.Data.Semester);
            if (rows.Count(r => r.Course is not null) != 1) throw new Exception("grid parsing");
            var course = rows.Single(r => r.Course is not null).Course!;
            if (!course.Weeks.SetEquals([2,4,6,8,10,14,16,18])) throw new Exception("parity and gaps");
            var importer = new CourseScheduleImporter(store);
            var preview = CourseScheduleImporter.Classify(rows, []);
            if (File.Exists(store.DataPath)) throw new Exception("preview wrote data");
            importer.Apply(preview, false);
            var reloaded = new ProductivityStore(directory);
            if (reloaded.Data.LastCourseImport is null) throw new Exception("undo not persisted");
            new CourseScheduleImporter(reloaded).Undo();
            if (reloaded.Data.Courses.Count != 0 || reloaded.Data.Todos.Count != 1) throw new Exception("undo changed unrelated data");
            File.WriteAllText(store.DataPath, "{\"SchemaVersion\":1,\"Courses\":[]}");
            var migrated = new ProductivityStore(directory);
            if (File.Exists(store.DataPath + ".pre-v0.4.bak")) throw new Exception("read-only load wrote migration backup");
            migrated.Save();
            new ProductivityStore(directory).Save();
            if (!File.Exists(store.DataPath + ".pre-v0.4.bak")) throw new Exception("migration backup missing");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    public static void Scheduling()
    {
        var semester = new SemesterSettings { StartDate = new DateOnly(2026, 9, 6) };
        var course = new CourseItem { Name = "测试课程", DayOfWeek = DayOfWeek.Friday, StartPeriod = 5, EndPeriod = 6,
            StartWeek = 1, EndWeek = 18, Weeks = [1, 2, 3, 4, 5, 14, 16, 18] };
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
        var table = semester.Timetable;
        Check(table.Resolve(course, new DateOnly(2026, 9, 30)).Start == new TimeOnly(14, 30), "summer last day");
        Check(table.Resolve(course, new DateOnly(2026, 10, 1)).Start == new TimeOnly(14, 0), "winter first day");
        Check(!table.IsSummer(new DateOnly(2027, 4, 30)) && table.IsSummer(new DateOnly(2027, 5, 1)), "May boundary");
        Check(table.Winter.Single(p => p.Number == 10).End == new TimeOnly(20, 40), "period ten is 45 minutes");
        Check(CourseScheduleService.Occurrences([course], semester, [], new DateOnly(2026, 9, 11)).Count == 1, "week one");
        Check(!CourseScheduleService.IsScheduled(course, new DateOnly(2026, 11, 27), 12), "week gaps");
        var fixedCourse = new CourseItem { Name = "fixed", StartTime = new TimeOnly(14, 30), EndTime = new TimeOnly(15, 15) };
        Check(table.Resolve(fixedCourse, new DateOnly(2026, 10, 1)).Start == new TimeOnly(14, 30), "fixed time unchanged");
        var unknown = CourseDataCopy.Clone(course); unknown.EndPeriod = 11;
        try { CourseScheduleService.Validate(unknown, semester); throw new Exception("missing period accepted"); }
        catch (ArgumentException) { }
        var change = new CourseAdjustment { CourseId = course.Id, OriginalDate = new DateOnly(2026, 9, 11),
            Date = new DateOnly(2026, 9, 12), Start = new TimeOnly(8, 0), End = new TimeOnly(9, 0), Location = "临时教室" };
        Check(CourseScheduleService.Occurrences([course], semester, [change], change.OriginalDate).Count == 0, "moved original removed");
        var moved = CourseScheduleService.Occurrences([course], semester, [change], change.Date).Single();
        Check(moved.Location == "临时教室" && moved.StartTime == change.Start, "override time and location");
        change.IsMakeup = true;
        Check(CourseScheduleService.Occurrences([course], semester, [change], change.OriginalDate).Count == 1, "makeup retains original");
        var other = CourseDataCopy.Clone(course); other.Id = Guid.NewGuid();
        Check(CourseScheduleService.FindConflicts([course, other], semester, []).Count > 0, "actual overlap");
        other.StartPeriod = null; other.EndPeriod = null; other.StartTime = new TimeOnly(16, 10); other.EndTime = new TimeOnly(16, 40);
        Check(CourseScheduleService.FindConflicts([course, other], semester, []).Count == 0, "touching boundary is not overlap");
    }
}
