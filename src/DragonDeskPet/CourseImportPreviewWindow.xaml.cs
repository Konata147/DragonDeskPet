using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DragonDeskPet.Core;
using DragonDeskPet.Services;

namespace DragonDeskPet;

public partial class CourseImportPreviewWindow : Window
{
    private readonly CourseImportPreview _preview;
    private readonly IProductivityStore? _store;
    public CourseImportPreviewWindow(CourseImportPreview preview, IProductivityStore? store = null)
    {
        _preview = preview; _store = store;
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        var preview = _preview;
        SummaryText.Text = $"新增 {preview.AddedCount} · 更新 {preview.UpdatedCount} · 重复 {preview.DuplicateCount} · 无效 {preview.InvalidCount}";
        if (_store is not null)
        {
            var incoming = preview.Rows.Where(r => r.Course is not null && r.Disposition != CourseImportDisposition.Invalid)
                .Select(r => r.Course!).GroupBy(c => c.Id).Select(g => g.Last()).ToList();
            var courses = _store.Data.Courses.Where(c => incoming.All(i => i.Id != c.Id)).Concat(incoming).ToList();
            var conflicts = CourseScheduleService.FindConflicts(courses, _store.Data.Semester, _store.Data.CourseAdjustments);
            SummaryText.Text += $"\n按合并方案：{conflicts.Count}处实际时间冲突（导入前确认）";
        }
        RowsList.ItemsSource = preview.Rows.Select(row => new CourseImportDisplayRow(row)).ToList();
        var hasMergeChanges = preview.AddedCount + preview.UpdatedCount > 0;
        var hasReplacementRecords = hasMergeChanges || preview.DuplicateCount > 0;
        MergeButton.IsEnabled = hasMergeChanges;
        ReplaceButton.IsEnabled = hasReplacementRecords;
        EmptyNotice.Text = hasReplacementRecords
            ? "全部为重复课程；合并不会更改课表。"
            : "没有可导入的有效课程。";
        EmptyNotice.Visibility = hasMergeChanges ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Reclassify()
    {
        var rows = CourseScheduleImporter.Classify(_preview.Rows, _store?.Data.Courses ?? []);
        _preview.Rows.Clear(); _preview.Rows.AddRange(rows.Rows); Refresh();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (RowsList.SelectedItem is not CourseImportDisplayRow selected) return;
        var result = CourseFormWindow.EditCourse(this, selected.Row.Course, _store?.Data.Semester ?? new SemesterSettings());
        if (result is null) return;
        var index = _preview.Rows.IndexOf(selected.Row);
        _preview.Rows[index] = new CourseImportRow(CourseImportDisposition.Add, result, selected.SourceDescription);
        Reclassify();
    }

    private void Exclude_Click(object sender, RoutedEventArgs e)
    {
        if (RowsList.SelectedItem is not CourseImportDisplayRow selected) return;
        _preview.Rows.Remove(selected.Row); Reclassify();
    }

    private bool ConfirmConflicts(bool replace)
    {
        if (_store is null) return true;
        var incoming = _preview.Rows.Where(r => r.Course is not null && r.Disposition != CourseImportDisposition.Invalid).Select(r => r.Course!).GroupBy(c => c.Id).Select(g => g.Last()).ToList();
        var items = replace ? incoming : _store.Data.Courses.Where(c => incoming.All(i => i.Id != c.Id)).Concat(incoming).ToList();
        var conflicts = CourseScheduleService.FindConflicts(items, _store.Data.Semester, _store.Data.CourseAdjustments);
        return conflicts.Count == 0 || new ConfirmActionWindow("发现课程冲突", string.Join("\n", conflicts.Take(6).Select(c => c.Description)) + $"\n共{conflicts.Count}处。仍然保留这些安排？", "仍然导入") { Owner = this }.ShowDialog() == true;
    }

    public bool ReplaceCurrentSemester { get; private set; }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmConflicts(false)) return;
        ReplaceCurrentSemester = false;
        DialogResult = true;
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmActionWindow(
            "替换当前学期？",
            "这会删除当前课程后再导入预览中的有效记录。普通提醒和今日清单不受影响。",
            "确认替换") { Owner = this };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        ReplaceCurrentSemester = true;
        if (!ConfirmConflicts(true)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed record CourseImportDisplayRow(CourseImportRow Row)
{
    public string SourceDescription => Row.SourceDescription;
    public CourseImportDisposition Disposition => Row.Disposition;
    public string ErrorMessage => Row.ErrorMessage ?? string.Empty;
    public bool HasError => !string.IsNullOrWhiteSpace(Row.ErrorMessage);
    public bool HasCourse => Row.Course is not null;

    public string ScheduleText
    {
        get
        {
            if (Row.Course is not { } course)
            {
                return string.Empty;
            }

            var day = course.DayOfWeek switch
            {
                DayOfWeek.Monday => "星期一",
                DayOfWeek.Tuesday => "星期二",
                DayOfWeek.Wednesday => "星期三",
                DayOfWeek.Thursday => "星期四",
                DayOfWeek.Friday => "星期五",
                DayOfWeek.Saturday => "星期六",
                DayOfWeek.Sunday => "星期日",
                _ => "未知星期"
            };
            var pattern = course.WeekPattern switch
            {
                CourseWeekPattern.Odd => "单周",
                CourseWeekPattern.Even => "双周",
                _ => "全部周"
            };
            var time = course.StartPeriod is { } period ? $"第{period}–{course.EndPeriod}节（冬夏自动）" : $"{course.StartTime:HH:mm}–{course.EndTime:HH:mm}";
            var weeks = course.Weeks.Count > 0 ? string.Join(",", course.Weeks.Order()) : $"{course.StartWeek}–{course.EndWeek}";
            return $"{day} {time} · 第 {weeks} 周 · {pattern}";
        }
    }

    public string LocationTeacherText => Row.Course is { } course
        ? $"地点：{DisplayOrUnknown(course.Location)}    教师：{DisplayOrUnknown(course.Teacher)}"
        : string.Empty;

    public string ReminderText => Row.Course is { } course
        ? $"{(course.ReminderMinutes is { } minutes ? minutes == 0 ? "到点提醒" : $"提前 {minutes} 分钟提醒" : "不提醒")} · {(course.IsEnabled ? "已启用" : "未启用")}"
        : string.Empty;

    private static string DisplayOrUnknown(string value) => string.IsNullOrWhiteSpace(value) ? "未填写" : value;
}

public sealed class CourseImportStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CourseImportDisposition.Add => "新增",
        CourseImportDisposition.Update => "更新",
        CourseImportDisposition.Duplicate => "重复",
        CourseImportDisposition.Invalid => "无效",
        _ => "未知"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
