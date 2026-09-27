using DragonDeskPet.Core;
using DragonDeskPet.Services;

internal static class CourseWeekLayoutTests
{
    public static void Run()
    {
        var semester = new SemesterSettings { StartDate = new DateOnly(2026, 9, 6) };
        var course = new CourseItem { Name = "脱敏课程", DayOfWeek = DayOfWeek.Wednesday, StartPeriod = 5, EndPeriod = 6 };
        var makeup = new CourseAdjustment { CourseId = course.Id, OriginalDate = new DateOnly(2026, 9, 30), Date = new DateOnly(2026, 10, 3),
            Start = new TimeOnly(14, 0), End = new TimeOnly(15, 40), IsMakeup = true };
        var original = CourseScheduleService.Occurrences([course], semester, [makeup], makeup.OriginalDate).Single();
        var extra = CourseScheduleService.Occurrences([course], semester, [makeup], makeup.Date).Single();
        var rows = CourseWeekLayout.Build([original, extra], semester.Timetable);
        if (rows.Count != 1 || rows[0].Label != "第5–6节" || rows[0].Occurrences.Count != 2)
            throw new Exception("Seasonal makeup must share the original period row.");
        if (original.StartTime != new TimeOnly(14, 30) || extra.StartTime != new TimeOnly(14, 0) || !extra.IsMakeup || original.IsCancelled)
            throw new Exception("Layout must not change real scheduling.");
        var custom = extra with { AdjustmentId = Guid.NewGuid(), StartTime = new TimeOnly(14, 10), EndTime = new TimeOnly(15, 20) };
        rows = CourseWeekLayout.Build([original, extra, custom], semester.Timetable);
        if (rows.Count != 2 || rows.Single(r => r.Label.StartsWith("自定义")).Occurrences.Single() != custom)
            throw new Exception("Custom clocks must not be guessed into a school period.");
        var morning = extra with { AdjustmentId = Guid.NewGuid(), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 40) };
        rows = CourseWeekLayout.Build([original, morning], semester.Timetable);
        if (rows.Count != 2 || rows[0].Label != "第1–2节") throw new Exception("Moved morning class incorrectly retained its afternoon source period.");
        var overlapping = extra with { AdjustmentId = Guid.NewGuid() };
        if (CourseWeekLayout.Build([original, extra, overlapping], semester.Timetable).Single().Occurrences.Count != 3)
            throw new Exception("Simultaneous records must not be hidden.");
    }
}
