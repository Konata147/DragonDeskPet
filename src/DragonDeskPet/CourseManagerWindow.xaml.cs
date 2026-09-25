using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using Microsoft.Win32;
using ComboBox = System.Windows.Controls.ComboBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DragonDeskPet;

public partial class CourseManagerWindow : Window
{
    private readonly IProductivityStore _store;
    private readonly CourseScheduleService _courses;
    private readonly CourseScheduleImporter _importer;
    private Guid? _editingId;

    public CourseManagerWindow(IProductivityStore store, CourseScheduleService courses, CourseScheduleImporter importer)
    {
        _store = store;
        _courses = courses;
        _importer = importer;
        InitializeComponent();
        SemesterStartPicker.SelectedDate = store.Data.Semester.StartDate.ToDateTime(TimeOnly.MinValue);
        RefreshList();
    }

    private void RefreshList()
    {
        CourseList.ItemsSource = null;
        CourseList.ItemsSource = _store.Data.Courses.OrderBy(course => course.DayOfWeek).ThenBy(course => course.StartTime).ToList();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        CourseList.SelectedItem = null;
        NameBox.Clear();
        LocationBox.Clear();
        TeacherBox.Clear();
        WeekdayBox.SelectedIndex = 0;
        PatternBox.SelectedIndex = 0;
        StartBox.Text = "08:00";
        EndBox.Text = "09:40";
        StartWeekBox.Text = "1";
        EndWeekBox.Text = "16";
        ReminderMinutesBox.Text = "30";
        ReminderEnabledBox.IsChecked = true;
        CourseEnabledBox.IsChecked = true;
        ValidationText.Text = string.Empty;
    }

    private void CourseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CourseList.SelectedItem is not CourseItem course)
        {
            return;
        }

        _editingId = course.Id;
        NameBox.Text = course.Name;
        SelectTag(WeekdayBox, course.DayOfWeek.ToString());
        StartBox.Text = course.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndBox.Text = course.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        LocationBox.Text = course.Location;
        TeacherBox.Text = course.Teacher;
        StartWeekBox.Text = course.StartWeek.ToString(CultureInfo.InvariantCulture);
        EndWeekBox.Text = course.EndWeek.ToString(CultureInfo.InvariantCulture);
        SelectTag(PatternBox, course.WeekPattern.ToString());
        ReminderEnabledBox.IsChecked = course.ReminderMinutes is not null;
        ReminderMinutesBox.Text = (course.ReminderMinutes ?? 30).ToString(CultureInfo.InvariantCulture);
        CourseEnabledBox.IsChecked = course.IsEnabled;
        ValidationText.Text = string.Empty;
    }

    private void SaveCourse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var existing = _editingId is { } id ? _store.Data.Courses.FirstOrDefault(course => course.Id == id) : null;
            var course = new CourseItem
            {
                Id = _editingId ?? Guid.NewGuid(),
                ExternalId = existing?.ExternalId ?? string.Empty,
                Name = NameBox.Text.Trim(),
                DayOfWeek = Enum.Parse<DayOfWeek>(((ComboBoxItem)WeekdayBox.SelectedItem).Tag!.ToString()!),
                StartTime = ReadTime(StartBox.Text),
                EndTime = ReadTime(EndBox.Text),
                Location = LocationBox.Text.Trim(),
                Teacher = TeacherBox.Text.Trim(),
                StartWeek = ReadInt(StartWeekBox.Text),
                EndWeek = ReadInt(EndWeekBox.Text),
                WeekPattern = Enum.Parse<CourseWeekPattern>(((ComboBoxItem)PatternBox.SelectedItem).Tag!.ToString()!),
                ReminderMinutes = ReminderEnabledBox.IsChecked == true ? Math.Clamp(ReadInt(ReminderMinutesBox.Text), 0, 1440) : null,
                IsEnabled = CourseEnabledBox.IsChecked == true,
                ExcludedDates = existing?.ExcludedDates ?? [],
                IncludedDates = existing?.IncludedDates ?? [],
                LastAlertedDate = existing?.LastAlertedDate
            };
            _courses.AddOrUpdate(course);
            _editingId = course.Id;
            ValidationText.Text = "已保存。";
            RefreshList();
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException)
        {
            ValidationText.Text = exception.Message;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (CourseList.SelectedItem is not CourseItem course)
        {
            return;
        }

        var confirmation = new ConfirmActionWindow(
            "删除课程？",
            $"“{course.Name}”将从课表和后续课程提醒中移除。普通提醒和今日清单不受影响。",
            "删除课程") { Owner = this };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        _courses.Delete(course.Id);
        New_Click(sender, e);
        RefreshList();
    }

    private void SkipToday_Click(object sender, RoutedEventArgs e)
    {
        if (CourseList.SelectedItem is CourseItem course)
        {
            _courses.SkipDate(course.Id, DateOnly.FromDateTime(DateTime.Today));
            ValidationText.Text = $"已将“{course.Name}”标记为今天停课。";
        }
    }

    private void SemesterStartPicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SemesterStartPicker.SelectedDate is not { } date)
        {
            return;
        }

        _store.Data.Semester.StartDate = DateOnly.FromDateTime(date);
        _store.Save();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "课程表 (*.ics;*.csv)|*.ics;*.csv|iCalendar (*.ics)|*.ics|CSV (*.csv)|*.csv" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var preview = _importer.Preview(dialog.FileName, _store.Data.Courses, _store.Data.Semester);
        var previewWindow = new CourseImportPreviewWindow(preview) { Owner = this };
        if (previewWindow.ShowDialog() != true)
        {
            return;
        }

        _importer.Apply(preview, previewWindow.ReplaceCurrentSemester);
        RefreshList();
        ValidationText.Text = "课程导入完成。";
    }

    private static int ReadInt(string text) => int.TryParse(text.Trim(), out var value) ? value : throw new FormatException("周次和提醒分钟必须是数字。");
    private static TimeOnly ReadTime(string text) => TimeOnly.TryParseExact(text.Trim().Replace('：', ':'), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : throw new FormatException("时间格式应为 HH:mm。");

    private static void SelectTag(ComboBox box, string tag)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().First(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class CourseDayDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        DayOfWeek.Sunday => "星期日",
        _ => string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
