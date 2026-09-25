using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed class CourseScheduleService : ICourseScheduleService
{
    private readonly IProductivityStore _store;

    public CourseScheduleService(IProductivityStore store) => _store = store;

    public int GetTeachingWeek(DateOnly date)
    {
        var days = date.DayNumber - _store.Data.Semester.StartDate.DayNumber;
        return days < 0 ? 0 : days / 7 + 1;
    }

    public IReadOnlyList<CourseOccurrence> GetCoursesForDate(DateOnly date)
    {
        var week = GetTeachingWeek(date);
        return _store.Data.Courses
            .Where(course => IsScheduled(course, date, week))
            .OrderBy(course => course.StartTime)
            .Select(course => new CourseOccurrence(course, date, week))
            .ToList();
    }

    public void AddOrUpdate(CourseItem course)
    {
        Validate(course);
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

        _store.Save();
    }

    public void Delete(Guid id)
    {
        _store.Data.Courses.RemoveAll(item => item.Id == id);
        _store.Data.PendingAlerts.RemoveAll(item => item.Source == AlertSource.Course && item.SourceId == id);
        _store.Save();
    }

    public void SetEnabled(Guid id, bool enabled)
    {
        var course = _store.Data.Courses.FirstOrDefault(item => item.Id == id);
        if (course is null)
        {
            return;
        }

        course.IsEnabled = enabled;
        _store.Save();
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
        _store.Save();
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
                if (course.ReminderMinutes is null || course.LastAlertedDate == date)
                {
                    continue;
                }

                var startLocal = date.ToDateTime(course.StartTime);
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
                        Message = BuildCourseMessage(course),
                        DueUtc = dueLocal.ToUniversalTime()
                    });
                }

                course.LastAlertedDate = date;
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

        return course.WeekPattern switch
        {
            CourseWeekPattern.Odd => teachingWeek % 2 == 1,
            CourseWeekPattern.Even => teachingWeek % 2 == 0,
            _ => true
        };
    }

    private static void Validate(CourseItem course)
    {
        if (string.IsNullOrWhiteSpace(course.Name))
        {
            throw new ArgumentException("课程名不能为空。", nameof(course));
        }

        if (course.EndTime <= course.StartTime)
        {
            throw new ArgumentException("下课时间必须晚于上课时间。", nameof(course));
        }

        if (course.StartWeek < 1 || course.EndWeek < course.StartWeek)
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
