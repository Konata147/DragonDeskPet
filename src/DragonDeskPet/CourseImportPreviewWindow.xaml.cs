using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DragonDeskPet.Core;

namespace DragonDeskPet;

public partial class CourseImportPreviewWindow : Window
{
    public CourseImportPreviewWindow(CourseImportPreview preview)
    {
        InitializeComponent();
        SummaryText.Text = $"新增 {preview.AddedCount} · 更新 {preview.UpdatedCount} · 重复 {preview.DuplicateCount} · 无效 {preview.InvalidCount}";
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

    public bool ReplaceCurrentSemester { get; private set; }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
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
            return $"{day} {course.StartTime:HH:mm}–{course.EndTime:HH:mm} · 第 {course.StartWeek}–{course.EndWeek} 周 · {pattern}";
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
