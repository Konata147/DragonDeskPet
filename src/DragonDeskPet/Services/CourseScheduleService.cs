using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed class CourseScheduleService : ICourseScheduleService
{
    private readonly IProductivityStore _store;
    public event EventHandler? Changed;

    public CourseScheduleService(IProductivityStore store) => _store = store;

    public int GetTeachingWeek(DateOnly date)
    {
        var days = date.DayNumber - _store.Data.Semester.StartDate.DayNumber;
        return days < 0 ? 0 : days / 7 + 1;
    }

    public IReadOnlyList<CourseOccurrence> GetCoursesForDate(DateOnly date)
    {
        return Occurrences(_store.Data.Courses, _store.Data.Semester, _store.Data.CourseAdjustments, date);
    }

    public IReadOnlyList<CourseOccurrence> GetCoursesForRange(DateOnly start, DateOnly end, bool includeCancelled = false)
    {
        var result = new List<CourseOccurrence>();
        if (end.DayNumber - start.DayNumber > 7 * 104) throw new ArgumentException("查询范围不能超过两年。");
        for (var day = start; day <= end; day = day.AddDays(1))
            result.AddRange(Occurrences(_store.Data.Courses, _store.Data.Semester, _store.Data.CourseAdjustments, day, includeCancelled));
        return result;
    }

    public static IReadOnlyList<CourseOccurrence> Occurrences(IEnumerable<CourseItem> courses, SemesterSettings semester,
        IEnumerable<CourseAdjustment> adjustments, DateOnly date, bool includeCancelled = false)
    {
        var week = date < semester.StartDate ? 0 : (date.DayNumber - semester.StartDate.DayNumber) / 7 + 1;
        var result = new List<CourseOccurrence>();
        foreach (var course in courses.Where(c => c.IsEnabled))
        {
            var changes = adjustments.Where(a => a.CourseId == course.Id).ToList();
            var scheduled = IsScheduled(course, date, week);
            var cancelled = course.ExcludedDates.Contains(date);
            if (includeCancelled && cancelled)
            {
                var copy = CourseDataCopy.Clone(course);
                copy.ExcludedDates.Remove(date);
                scheduled = IsScheduled(copy, date, week);
            }
            var moved = changes.Any(a => !a.IsMakeup && a.OriginalDate == date);
            if (scheduled && (!moved || includeCancelled))
            {
                var time = semester.Timetable.Resolve(course, date);
                result.Add(new CourseOccurrence(course, date, week)
                { StartTime = time.Start, EndTime = time.End, IsCancelled = cancelled || moved });
            }
            foreach (var change in changes.Where(a => a.Date == date))
                result.Add(new CourseOccurrence(course, date, week)
                { StartTime = change.Start, EndTime = change.End, Location = change.Location, AdjustmentId = change.Id, IsMakeup = change.IsMakeup });
        }
        return result.OrderBy(o => o.StartTime).ThenBy(o => o.Course.Name).ToList();
    }

    public static IReadOnlyList<CourseConflict> FindConflicts(IReadOnlyList<CourseItem> courses, SemesterSettings semester,
        IReadOnlyList<CourseAdjustment> adjustments)
    {
        var conflicts = new List<CourseConflict>();
        var maxWeek = Math.Max(32, courses.Count == 0 ? 1 : courses.Max(c => c.EndWeek));
        var dates = Enumerable.Range(0, Math.Min(104, maxWeek) * 7).Select(i => semester.StartDate.AddDays(i))
            .Concat(courses.SelectMany(c => c.IncludedDates)).Concat(adjustments.Select(a => a.Date)).Distinct();
        foreach (var date in dates)
        {
            var items = Occurrences(courses, semester, adjustments, date);
            for (var i = 0; i < items.Count; i++)
                for (var j = i + 1; j < items.Count; j++)
                    if (items[i].StartTime < items[j].EndTime && items[j].StartTime < items[i].EndTime)
                        conflicts.Add(new CourseConflict(items[i], items[j]));
        }
        return conflicts;
    }

    public IReadOnlyList<CourseConflict> CheckCourse(CourseItem candidate) => FindConflicts(
        _store.Data.Courses.Where(c => c.Id != candidate.Id).Append(candidate).ToList(), _store.Data.Semester, _store.Data.CourseAdjustments);

    public IReadOnlyList<CourseConflict> CheckAdjustment(CourseAdjustment candidate) => FindConflicts(_store.Data.Courses,
        _store.Data.Semester, _store.Data.CourseAdjustments.Where(a => a.Id != candidate.Id &&
            (candidate.IsMakeup || a.IsMakeup || a.CourseId != candidate.CourseId || a.OriginalDate != candidate.OriginalDate)).Append(candidate).ToList());

    public void SaveAdjustment(CourseAdjustment change)
    {
        if (!_store.Data.Courses.Any(c => c.Id == change.CourseId)) throw new ArgumentException("课程已不存在。");
        if (change.End <= change.Start) throw new ArgumentException("下课须晚于上课，不支持跨午夜。");
        _store.Data.CourseAdjustments.RemoveAll(a => a.Id == change.Id ||
            !change.IsMakeup && !a.IsMakeup && a.CourseId == change.CourseId && a.OriginalDate == change.OriginalDate);
        _store.Data.CourseAdjustments.Add(change);
        _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course &&
            (a.OccurrenceKey == $"{change.CourseId:N}:{change.OriginalDate:yyyy-MM-dd}" && !change.IsMakeup || a.OccurrenceKey == $"adjustment:{change.Id:N}"));
        Persist();
    }

    public void RemoveAdjustment(Guid id)
    {
        _store.Data.CourseAdjustments.RemoveAll(a => a.Id == id);
        _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course && a.OccurrenceKey == $"adjustment:{id:N}");
        Persist();
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
    private void Persist() { _store.Save(); NotifyChanged(); }

    public void AddOrUpdate(CourseItem course)
    {
        Validate(course, _store.Data.Semester);
        var existing = _store.Data.Courses.FirstOrDefault(item => item.Id == course.Id);
        if (existing is null)
        {
            _store.Data.Courses.Add(course);
        }
        else
        {
            var index = _store.Data.Courses.IndexOf(existing);
            _store.Data.Courses[index] = course;
        }

        _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course && a.SourceId == course.Id);
        Persist();
    }

    public void Delete(Guid id)
    {
        _store.Data.Courses.RemoveAll(item => item.Id == id);
        _store.Data.CourseAdjustments.RemoveAll(item => item.CourseId == id);
        _store.Data.PendingAlerts.RemoveAll(item => item.Source == AlertSource.Course && item.SourceId == id);
        Persist();
    }

    public void SetEnabled(Guid id, bool enabled)
    {
        var course = _store.Data.Courses.FirstOrDefault(item => item.Id == id);
        if (course is null)
        {
            return;
        }

        course.IsEnabled = enabled;
        if (!enabled) _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course && a.SourceId == id);
        Persist();
    }

    public void SkipDate(Guid id, DateOnly date)
    {
        var course = _store.Data.Courses.FirstOrDefault(item => item.Id == id);
        if (course is null)
        {
            return;
        }

        course.ExcludedDates.Add(date);
        _store.Data.PendingAlerts.RemoveAll(item => item.Source == AlertSource.Course && item.OccurrenceKey == $"{id:N}:{date:yyyy-MM-dd}");
        Persist();
    }

    public void Tick(DateTimeOffset nowLocal)
    {
        var nowUtc = nowLocal.ToUniversalTime();
        DateTimeOffset? lastCheckUtc = _store.Data.LastCourseSchedulerCheckUtc is { } storedCheck && storedCheck <= nowUtc
            ? storedCheck
            : null;
        var firstDate = lastCheckUtc is { } previous
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(previous, TimeZoneInfo.Local).DateTime)
            : DateOnly.FromDateTime(nowLocal.DateTime);
        var lastDate = DateOnly.FromDateTime(nowLocal.DateTime);
        var changed = false;
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            foreach (var occurrence in GetCoursesForDate(date))
            {
                var course = occurrence.Course;
                if (course.ReminderMinutes is null || _store.Data.NotifiedCourseOccurrences.Contains(occurrence.OccurrenceKey)
                    || occurrence.AdjustmentId is null && course.LastAlertedDate == date)
                {
                    continue;
                }

                var startLocal = date.ToDateTime(occurrence.StartTime);
                var dueLocal = new DateTimeOffset(startLocal, TimeZoneInfo.Local.GetUtcOffset(startLocal))
                    .AddMinutes(-Math.Clamp(course.ReminderMinutes.Value, 0, 24 * 60));
                if (dueLocal > nowLocal || (lastCheckUtc is { } check && dueLocal.ToUniversalTime() < check && date == firstDate))
                {
                    continue;
                }

                var key = occurrence.OccurrenceKey;
                if (!_store.Data.PendingAlerts.Any(alert => alert.OccurrenceKey == key))
                {
                    _store.Data.PendingAlerts.Add(new PendingAlert
                    {
                        Source = AlertSource.Course,
                        SourceId = course.Id,
                        OccurrenceKey = key,
                        Title = $"{course.Name} 快要开始啦",
                        Message = $"{occurrence.StartTime:HH:mm}–{occurrence.EndTime:HH:mm} · {occurrence.Location}",
                        DueUtc = dueLocal.ToUniversalTime()
                    });
                }

                _store.Data.NotifiedCourseOccurrences.Add(occurrence.OccurrenceKey);
                if (occurrence.AdjustmentId is null) course.LastAlertedDate = date;
                changed = true;
            }
        }

        if (lastCheckUtc is null || nowUtc - lastCheckUtc >= TimeSpan.FromMinutes(1))
        {
            _store.Data.LastCourseSchedulerCheckUtc = nowUtc;
            changed = true;
        }

        if (changed)
        {
            _store.Save();
        }
    }

    public static bool IsScheduled(CourseItem course, DateOnly date, int teachingWeek)
    {
        if (!course.IsEnabled || course.ExcludedDates.Contains(date))
        {
            return false;
        }

        if (course.IncludedDates.Count > 0)
        {
            return course.IncludedDates.Contains(date);
        }

        if (teachingWeek < course.StartWeek || teachingWeek > course.EndWeek || date.DayOfWeek != course.DayOfWeek)
        {
            return false;
        }

        if (course.Weeks.Count > 0) return course.Weeks.Contains(teachingWeek);

        return course.WeekPattern switch
        {
            CourseWeekPattern.Odd => teachingWeek % 2 == 1,
            CourseWeekPattern.Even => teachingWeek % 2 == 0,
            _ => true
        };
    }

    public static void Validate(CourseItem course, SemesterSettings? semester = null)
    {
        if (string.IsNullOrWhiteSpace(course.Name))
        {
            throw new ArgumentException("课程名不能为空。", nameof(course));
        }

        if (course.StartPeriod is not null || course.EndPeriod is not null)
        {
            var timetable = (semester ?? new SemesterSettings()).Timetable;
            timetable.Validate();
            _ = SeasonalTimetable.ResolvePeriods(course, timetable.Summer);
            _ = SeasonalTimetable.ResolvePeriods(course, timetable.Winter);
        }
        else if (course.EndTime <= course.StartTime)
        {
            throw new ArgumentException("下课时间必须晚于上课时间。", nameof(course));
        }

        if (course.StartWeek < 1 || course.EndWeek < course.StartWeek || course.EndWeek > 104
            || course.Weeks.Any(w => w < course.StartWeek || w > course.EndWeek))
        {
            throw new ArgumentException("课程周次范围无效。", nameof(course));
        }
    }

    private static string BuildCourseMessage(CourseItem course)
    {
        var location = string.IsNullOrWhiteSpace(course.Location) ? string.Empty : $" · {course.Location}";
        return $"{course.StartTime:HH:mm}–{course.EndTime:HH:mm}{location}";
    }
}
