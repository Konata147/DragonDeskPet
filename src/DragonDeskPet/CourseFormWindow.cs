using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DragonDeskPet;

// Small modal editor reused by import correction, one-off changes and timetable settings.
public sealed class CourseFormWindow : Window
{
    private readonly StackPanel _fields = new() { Margin = new Thickness(4) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed, Margin = new Thickness(4, 8, 4, 8) };
    private readonly Dictionary<string, Func<string>> _values = [];
    public Func<IReadOnlyDictionary<string, string>, bool>? Submit { get; set; }

    public CourseFormWindow(Window owner, string title)
    {
        Owner = owner; Title = title; Width = 520; Height = 610; MinWidth = 440; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(245, 241, 250)); FontFamily = new FontFamily("Microsoft YaHei UI");
        // Use the existing themed controls instead of falling back to Windows defaults.
        var source = owner is CourseManagerWindow ? owner : owner.Owner;
        if (source is not null) Resources.MergedDictionaries.Add(source.Resources);
        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 4, 12) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new StackPanel();
        footer.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton("取消"); cancel.IsCancel = true; cancel.Click += (_, _) => DialogResult = false;
        var save = MakeButton("确认"); save.Click += (_, _) =>
        {
            try
            {
                if (Submit?.Invoke(_values.ToDictionary(p => p.Key, p => p.Value())) == true) DialogResult = true;
            }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException)
            { _error.Text = e.Message; }
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = _fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
    }

    public Button MakeButton(string text) => new() { Content = text, Margin = new Thickness(4), Padding = new Thickness(12, 7, 12, 7),
        Style = TryFindResource("CourseButton") as Style ?? TryFindResource("PreviewButton") as Style };
    public void Note(string text) => _fields.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10), Foreground = Brushes.DimGray });
    public TextBox Field(string key, string title, string value, bool multiline = false)
    {
        _fields.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 7, 0, 4) });
        var box = new TextBox { Text = value, MinHeight = multiline ? 75 : 36, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = multiline, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _fields.Children.Add(box); _values[key] = () => box.Text.Trim();
        return box;
    }
    public void Choice(string key, string title, string[] labels, int selected)
    {
        _fields.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 7, 0, 4) });
        var box = new ComboBox { MinHeight = 36, Style = TryFindResource("CourseCombo") as Style };
        foreach (var label in labels) box.Items.Add(new ComboBoxItem { Content = label, Style = TryFindResource("CourseComboItem") as Style });
        box.SelectedIndex = Math.Clamp(selected, 0, labels.Length - 1);
        _fields.Children.Add(box); _values[key] = () => box.SelectedIndex.ToString(CultureInfo.InvariantCulture);
    }
    public static TimeOnly Time(string text) => TimeOnly.TryParseExact(text.Replace('：', ':'), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
        ? time : throw new FormatException("时间请填写 HH:mm。");

    public static CourseItem? EditCourse(Window owner, CourseItem? original, SemesterSettings semester)
    {
        var c = CourseDataCopy.Clone(original ?? new CourseItem { StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 40), DayOfWeek = DayOfWeek.Monday });
        var dialog = new CourseFormWindow(owner, "核对课程");
        dialog.Note("按节次课程随冬夏作息切换；固定钟点不变。省略号和不完整内容请对照原表补齐。");
        dialog.Field("name", "课程名称", c.Name);
        dialog.Choice("day", "星期", ["星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日"], ((int)c.DayOfWeek + 6) % 7);
        dialog.Choice("mode", "时间模式", ["固定钟点", "按节次（自动冬夏切换）"], c.StartPeriod is null ? 0 : 1);
        dialog.Field("start", "上课钟点 / 起始节次", c.StartPeriod?.ToString() ?? c.StartTime.ToString("HH:mm"));
        dialog.Field("end", "下课钟点 / 结束节次", c.EndPeriod?.ToString() ?? c.EndTime.ToString("HH:mm"));
        dialog.Field("weeks", "明确上课周（如 1-11,14-18；此处填写实际周次）", c.EndWeek < 1 ? "" : c.Weeks.Count > 0 ? string.Join(",", c.Weeks.Order())
            : string.Join(",", Enumerable.Range(c.StartWeek, c.EndWeek - c.StartWeek + 1).Where(w => c.WeekPattern == CourseWeekPattern.All || (w % 2 == 1) == (c.WeekPattern == CourseWeekPattern.Odd))));
        dialog.Field("location", "地点", c.Location); dialog.Field("teacher", "教师", c.Teacher);
        dialog.Field("reminder", "提前提醒分钟（填“关闭”则不提醒）", c.ReminderMinutes?.ToString() ?? "关闭");
        dialog.Choice("enabled", "课程状态", ["启用", "停用"], c.IsEnabled ? 0 : 1);
        if (c.IncludedDates.Count > 0) dialog.Note("此课程来自明确日期日历，修改周次不会替代原来的发生日期；单次日期请在周课表中调整。");
        dialog.Submit = values =>
        {
            c.Name = values["name"]; c.DayOfWeek = (DayOfWeek)((int.Parse(values["day"]) + 1) % 7);
            if (c.Name.Contains('…') || c.Name.Contains("...")) throw new ArgumentException("请补齐被省略的课程名称。");
            c.StartPeriod = values["mode"] == "1" ? int.Parse(values["start"]) : null;
            c.EndPeriod = values["mode"] == "1" ? int.Parse(values["end"]) : null;
            if (c.StartPeriod is null) { c.StartTime = Time(values["start"]); c.EndTime = Time(values["end"]); }
            c.Weeks = CourseScheduleImporter.ParseWeeks(values["weeks"]); c.StartWeek = c.Weeks.Min(); c.EndWeek = c.Weeks.Max();
            c.WeekPattern = CourseWeekPattern.All; c.Location = values["location"]; c.Teacher = values["teacher"];
            c.ReminderMinutes = values["reminder"] == "关闭" ? null : int.Parse(values["reminder"]);
            c.IsEnabled = values["enabled"] == "0";
            if (c.ReminderMinutes is < 0 or > 1440) throw new ArgumentException("提前提醒须在0–1440分钟内。");
            CourseScheduleService.Validate(c, semester);
            var resolved = semester.Timetable.Resolve(c, semester.StartDate); c.StartTime = resolved.Start; c.EndTime = resolved.End;
            return true;
        };
        return dialog.ShowDialog() == true ? c : null;
    }
}
