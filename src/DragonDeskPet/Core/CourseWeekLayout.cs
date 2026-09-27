namespace DragonDeskPet.Core;

// Presentation only: occurrence clocks, reminder times and persisted data are never changed.
public sealed record CourseWeekRow(string Label, IReadOnlyList<CourseOccurrence> Occurrences);

public static class CourseWeekLayout
{
    public static IReadOnlyList<CourseWeekRow> Build(IEnumerable<CourseOccurrence> occurrences, SeasonalTimetable timetable)
    {
        var entries = occurrences.Select(occurrence =>
        {
            var periods = timetable.IsSummer(occurrence.Date) ? timetable.Summer : timetable.Winter;
            var first = periods.FirstOrDefault(p => p.Start == occurrence.StartTime);
            var last = periods.FirstOrDefault(p => p.End == occurrence.EndTime && p.Number >= first?.Number);
            // Match actual clocks, not the source course's period: a makeup/reschedule
            // may have been explicitly moved from afternoon to morning or custom clocks.
            var matched = first is not null && last is not null
                && Enumerable.Range(first.Number, last.Number - first.Number + 1).All(n => periods.Any(p => p.Number == n));
            var period = matched ? first!.Number : (int?)null;
            var key = period is { } number ? $"period:{number}" : $"clock:{occurrence.StartTime:HH:mm:ss}";
            var order = period is { } slot
                ? (timetable.Summer.FirstOrDefault(p => p.Number == slot) ?? first!).Start
                : occurrence.StartTime;
            return new { Occurrence = occurrence, Key = key, Period = period, Last = matched ? last!.Number : (int?)null, Order = order };
        });
        return entries.GroupBy(e => e.Key).OrderBy(g => g.Min(e => e.Order)).ThenBy(g => g.Key)
            .Select(group =>
            {
                var first = group.First();
                var ends = group.Select(e => e.Last).Distinct().ToList();
                var label = first.Period is not { } period ? $"自定义\n{first.Occurrence.StartTime:HH:mm}"
                    : ends.Count > 1 ? $"第{period}节起"
                    : ends[0] == period ? $"第{period}节" : $"第{period}–{ends[0]}节";
                return new CourseWeekRow(label, group.Select(e => e.Occurrence).ToList());
            }).ToList();
    }
}
