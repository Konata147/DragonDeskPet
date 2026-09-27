using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using Microsoft.Win32;
using ComboBox = System.Windows.Controls.ComboBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DragonDeskPet;

public partial class CourseManagerWindow : Window
{
    private readonly IProductivityStore _store;
    private readonly CourseScheduleService _courses;
    private readonly CourseScheduleImporter _importer;
    private Guid? _editingId;
    private DateOnly _weekStart;
    private bool _weekVisible;

    public CourseManagerWindow(IProductivityStore store, CourseScheduleService courses, CourseScheduleImporter importer)
    {
        _store = store;
        _courses = courses;
        _importer = importer;
        InitializeComponent();
        SemesterStartPicker.SelectedDate = store.Data.Semester.StartDate.ToDateTime(TimeOnly.MinValue);
        _weekStart = WeekStart(DateOnly.FromDateTime(DateTime.Today));
        RefreshList();
    }

    private void RefreshList()
    {
        CourseList.ItemsSource = null;
        CourseList.ItemsSource = _store.Data.Courses.OrderBy(course => course.DayOfWeek).ThenBy(course => course.StartTime).ToList();
        RefreshWeek();
        _courses.NotifyChanged();
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
            if (existing?.StartPeriod is not null || existing?.Weeks.Count > 0)
            {
                ValidationText.Text = "这门课程使用精确周次或节次，请点击“完整编辑”以保留其排课规则。";
                return;
            }
            if (!ConfirmConflicts(_courses.CheckCourse(course))) return;
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

    private void SemesterStartPicker_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SemesterStartPicker.SelectedDate is not { } date)
        {
            return;
        }

        var changedSemester = CourseDataCopy.Clone(_store.Data.Semester);
        changedSemester.StartDate = DateOnly.FromDateTime(date);
        if (!ConfirmConflicts(CourseScheduleService.FindConflicts(_store.Data.Courses, changedSemester, _store.Data.CourseAdjustments)))
        {
            SemesterStartPicker.SelectedDateChanged -= SemesterStartPicker_Changed;
            SemesterStartPicker.SelectedDate = _store.Data.Semester.StartDate.ToDateTime(TimeOnly.MinValue);
            SemesterStartPicker.SelectedDateChanged += SemesterStartPicker_Changed;
            return;
        }
        _store.Data.Semester.StartDate = changedSemester.StartDate;
        _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course);
        _store.Save();
        _weekStart = WeekStart(DateOnly.FromDateTime(DateTime.Today));
        RefreshList();
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "课程表 (*.ics;*.csv;*.xls;*.xlsx)|*.ics;*.csv;*.xls;*.xlsx" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
        ValidationText.Text = string.Empty;
        var sheet = 0;
        if (System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant() is ".xls" or ".xlsx")
        {
            var sheetsProgress = new CourseImportProgressWindow(this) { Stage = "正在读取工作表列表" };
            var names = await sheetsProgress.RunAsync(token => Task.Run(() => CourseScheduleImporter.GetExcelSheets(dialog.FileName), token));
            if (names is null) return;
            if (names.Count > 1)
            {
                var chooser = new CourseFormWindow(this, "选择工作表");
                chooser.Choice("sheet", "工作表", names.ToArray(), 0);
                chooser.Submit = values => { sheet = int.Parse(values["sheet"]); return true; };
                if (chooser.ShowDialog() != true) return;
            }
        }
        var progress = new CourseImportProgressWindow(this);
        var preview = await progress.RunAsync(token => _importer.PreviewAsync(dialog.FileName,
            CourseDataCopy.Clone(_store.Data.Courses), CourseDataCopy.Clone(_store.Data.Semester), sheet, token));
        if (preview is null) return;
        var previewWindow = new CourseImportPreviewWindow(preview, _store) { Owner = this };
        if (previewWindow.ShowDialog() != true)
        {
            return;
        }

        _importer.Apply(preview, previewWindow.ReplaceCurrentSemester);
        RefreshList();
        ValidationText.Text = "课程导入完成。";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { ValidationText.Text = CourseImportErrors.Describe(exception); }
    }

    private bool ConfirmConflicts(IReadOnlyList<CourseConflict> conflicts) => conflicts.Count == 0 ||
        new ConfirmActionWindow("发现课程冲突", string.Join("\n", conflicts.Take(6).Select(c => c.Description)) + $"\n共{conflicts.Count}处，是否仍然保留？", "仍然保存") { Owner = this }.ShowDialog() == true;

    private DateOnly WeekStart(DateOnly date)
    {
        var delta = (date.DayNumber - _store.Data.Semester.StartDate.DayNumber) % 7;
        return date.AddDays(-(delta + 7) % 7);
    }
    private void ToggleWeek_Click(object sender, RoutedEventArgs e) { _weekVisible = !_weekVisible; RefreshWeek(); }
    private void PreviousWeek_Click(object sender, RoutedEventArgs e) { _weekStart = _weekStart.AddDays(-7); _weekVisible = true; RefreshWeek(); }
    private void NextWeek_Click(object sender, RoutedEventArgs e) { _weekStart = _weekStart.AddDays(7); _weekVisible = true; RefreshWeek(); }
    private void ThisWeek_Click(object sender, RoutedEventArgs e) { _weekStart = WeekStart(DateOnly.FromDateTime(DateTime.Today)); _weekVisible = true; RefreshWeek(); }

    private void RefreshWeek()
    {
        ListSurface.Visibility = EditorSurface.Visibility = _weekVisible ? Visibility.Collapsed : Visibility.Visible;
        ListRow.Height = new GridLength(_weekVisible ? 0 : 182);
        WeekSurface.Visibility = _weekVisible ? Visibility.Visible : Visibility.Collapsed;
        SaveCourseButton.Visibility = _weekVisible ? Visibility.Collapsed : Visibility.Visible;
        WeekHeading.Text = $"第{_courses.GetTeachingWeek(_weekStart)}教学周 · {_weekStart:MM-dd}—{_weekStart.AddDays(6):MM-dd}";
        WeekColumns.Children.Clear();
        WeekColumns.RowDefinitions.Clear();
        WeekColumns.ColumnDefinitions.Clear();
        if (!_weekVisible) return;
        var occurrences = _courses.GetCoursesForRange(_weekStart, _weekStart.AddDays(6), true);
        // Seasonal clock changes share a school-period row, without changing actual times.
        var rows = CourseWeekLayout.Build(occurrences, _store.Data.Semester.Timetable);
        WeekColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        for (var column = 0; column < 7; column++)
            WeekColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        WeekColumns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var row = 0; row < rows.Count; row++)
        {
            WeekColumns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[row].Label, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray, Margin = new Thickness(4, 16, 4, 0), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(label, row + 1); WeekColumns.Children.Add(label);
        }
        for (var i = 0; i < 7; i++)
        {
            var day = _weekStart.AddDays(i);
            var heading = new TextBlock { Text = $"{day:MM-dd ddd}" + (day == DateOnly.FromDateTime(DateTime.Today) ? " · 今天" : ""),
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 4, 8, 14) };
            Grid.SetColumn(heading, i + 1); WeekColumns.Children.Add(heading);
            var daily = occurrences.Where(o => o.Date == day).ToList();
            for (var row = 0; row < rows.Count; row++)
            {
                var cell = new StackPanel { Tag = "WeekTimeCell", Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Top };
                foreach (var occurrence in rows[row].Occurrences.Where(o => o.Date == day))
                {
                    var conflict = !occurrence.IsCancelled && daily.Any(o => o.OccurrenceKey != occurrence.OccurrenceKey && !o.IsCancelled
                        && o.StartTime < occurrence.EndTime && occurrence.StartTime < o.EndTime);
                    var button = CreateWeekCard(occurrence, conflict);
                    button.Click += (_, _) => EditOccurrence(occurrence);
                    cell.Children.Add(button);
                }
                Grid.SetRow(cell, row + 1); Grid.SetColumn(cell, i + 1); WeekColumns.Children.Add(cell);
            }
        }
    }

    private Button CreateWeekCard(CourseOccurrence occurrence, bool conflict)
    {
        // Separate fields keep short and wrapped content on the same left edge.
        // Minimum row heights align ordinary cards without clipping longer names.
        var content = new StackPanel { Tag = "WeekCardContent" };
        content.Children.Add(new TextBlock
        {
            Text = $"{occurrence.StartTime:HH:mm}–{occurrence.EndTime:HH:mm}",
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(117, 86, 175)),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(new TextBlock
        {
            Text = occurrence.Course.Name, Tag = "WeekCardName", FontSize = 13,
            FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(64, 54, 83)),
            TextWrapping = TextWrapping.Wrap, LineHeight = 20, MinHeight = 40,
            Margin = new Thickness(0, 0, 0, 10)
        });
        void AddField(string label, string value)
        {
            var row = new Grid { MinHeight = 36, Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.Gray, LineHeight = 18 });
            var text = new TextBlock { Text = string.IsNullOrWhiteSpace(value) ? "未填写" : value,
                Tag = "WeekCard" + label, FontSize = 11, LineHeight = 18, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(98, 85, 111)) };
            Grid.SetColumn(text, 1); row.Children.Add(text); content.Children.Add(row);
        }
        AddField("地点", occurrence.Location);
        AddField("教师", occurrence.Course.Teacher);
        var status = occurrence.IsCancelled ? "已停课 / 已调走"
            : occurrence.AdjustmentId is null ? "" : occurrence.IsMakeup ? "补课" : "调课";
        if (conflict) status += (status.Length > 0 ? " · " : "") + "时间冲突";
        if (status.Length > 0)
            content.Children.Add(new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, FontSize = 11,
                Foreground = conflict ? Brushes.DarkRed : new SolidColorBrush(Color.FromRgb(117, 86, 175)),
                Margin = new Thickness(0, 5, 0, 0) });
        return new Button { Content = content, Tag = "WeekCourseCard", Style = (Style)FindResource("WeekCourseCard"),
            Opacity = occurrence.IsCancelled ? 0.65 : 1, ToolTip = "查看完整课程详情、单次调课或补课" };
    }

    private void EditOccurrence(CourseOccurrence occurrence)
    {
        var dialog = new CourseFormWindow(this, "单次课程安排");
        dialog.Note($"{occurrence.Course.Name}\n教师：{occurrence.Course.Teacher}\n原安排：{occurrence.Date:yyyy-MM-dd} {occurrence.StartTime:HH:mm}–{occurrence.EndTime:HH:mm}\n{occurrence.Location}");
        dialog.Choice("action", "操作", ["仅调整这一次", "新增一次补课", "本次停课", "恢复原安排 / 撤销本次调整"], 0);
        var dateBox = dialog.Field("date", "目标日期 yyyy-MM-dd", occurrence.Date.ToString("yyyy-MM-dd"));
        var startBox = dialog.Field("start", "上课时间", occurrence.StartTime.ToString("HH:mm"));
        var endBox = dialog.Field("end", "下课时间", occurrence.EndTime.ToString("HH:mm"));
        var timesEdited = occurrence.AdjustmentId is not null;
        var updatingTimes = false;
        startBox.TextChanged += (_, _) => { if (!updatingTimes) timesEdited = true; };
        endBox.TextChanged += (_, _) => { if (!updatingTimes) timesEdited = true; };
        dateBox.TextChanged += (_, _) =>
        {
            if (timesEdited || !DateOnly.TryParseExact(dateBox.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var target)) return;
            var times = _store.Data.Semester.Timetable.Resolve(occurrence.Course, target);
            updatingTimes = true;
            startBox.Text = times.Start.ToString("HH:mm"); endBox.Text = times.End.ToString("HH:mm");
            updatingTimes = false;
        };
        dialog.Field("location", "地点", occurrence.Location);
        dialog.Submit = values =>
        {
            var old = _store.Data.CourseAdjustments.FirstOrDefault(a => a.Id == occurrence.AdjustmentId
                || !a.IsMakeup && a.CourseId == occurrence.Course.Id && a.OriginalDate == occurrence.Date);
            if (values["action"] == "3")
            {
                if (old is not null) _courses.RemoveAdjustment(old.Id);
                if (old?.IsMakeup != true) occurrence.Course.ExcludedDates.Remove(old?.OriginalDate ?? occurrence.Date);
                _store.Save(); return true;
            }
            if (values["action"] == "2")
            {
                if (old is not null) _courses.RemoveAdjustment(old.Id);
                if (old?.IsMakeup == true) return true;
                _courses.SkipDate(occurrence.Course.Id, old?.OriginalDate ?? occurrence.Date); return true;
            }
            var change = new CourseAdjustment { CourseId = occurrence.Course.Id, OriginalDate = old?.OriginalDate ?? occurrence.Date,
                Date = DateOnly.ParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Start = CourseFormWindow.Time(values["start"]), End = CourseFormWindow.Time(values["end"]), Location = values["location"], IsMakeup = values["action"] == "1" || old?.IsMakeup == true };
            if (values["action"] == "0" && old is not null) change.Id = old.Id;
            if (change.End <= change.Start) throw new ArgumentException("下课须晚于上课，不支持跨午夜。");
            if (!ConfirmConflicts(_courses.CheckAdjustment(change))) return false;
            _courses.SaveAdjustment(change); return true;
        };
        if (dialog.ShowDialog() == true) RefreshList();
    }

    private void AdvancedEdit_Click(object sender, RoutedEventArgs e)
    {
        var course = CourseFormWindow.EditCourse(this, CourseList.SelectedItem as CourseItem, _store.Data.Semester);
        if (course is null || !ConfirmConflicts(_courses.CheckCourse(course))) return;
        _courses.AddOrUpdate(course); RefreshList();
    }

    private void Timetable_Click(object sender, RoutedEventArgs e)
    {
        var table = CourseDataCopy.Clone(_store.Data.Semester.Timetable);
        var dialog = new CourseFormWindow(this, "冬夏作息");
        dialog.Note("每行填写：节次 开始时间 结束时间。未配置的第11节不会被猜测。修改只影响按节次课程。");
        dialog.Field("from", "夏季开始 MM-dd", table.SummerFrom); dialog.Field("through", "夏季结束 MM-dd", table.SummerThrough);
        dialog.Field("summer", "夏季", string.Join("\n", table.Summer.Select(p => $"{p.Number} {p.Start:HH:mm} {p.End:HH:mm}")), true);
        dialog.Field("winter", "冬季", string.Join("\n", table.Winter.Select(p => $"{p.Number} {p.Start:HH:mm} {p.End:HH:mm}")), true);
        dialog.Submit = values =>
        {
            List<PeriodTime> Parse(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line =>
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3) throw new FormatException("每行须为：节次 HH:mm HH:mm。");
                return new PeriodTime { Number = int.Parse(parts[0]), Start = CourseFormWindow.Time(parts[1]), End = CourseFormWindow.Time(parts[2]) };
            }).ToList();
            table.SummerFrom = values["from"]; table.SummerThrough = values["through"];
            table.Summer = Parse(values["summer"]); table.Winter = Parse(values["winter"]); table.Validate();
            var semester = CourseDataCopy.Clone(_store.Data.Semester); semester.Timetable = table;
            foreach (var course in _store.Data.Courses) CourseScheduleService.Validate(course, semester);
            if (!ConfirmConflicts(CourseScheduleService.FindConflicts(_store.Data.Courses, semester, _store.Data.CourseAdjustments))) return false;
            _store.Data.Semester = semester;
            _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course);
            _store.Save(); return true;
        };
        if (dialog.ShowDialog() == true) RefreshList();
    }

    private void UndoImport_Click(object sender, RoutedEventArgs e)
    {
        if (_store.Data.LastCourseImport is not { } snapshot) { ValidationText.Text = "没有可撤销的导入。"; return; }
        var changed = snapshot.AfterFingerprint != _importer.ScheduleFingerprint();
        var message = $"课程安排 {_store.Data.Courses.Count} → {snapshot.Courses.Count} 条。将恢复导入前的课程、学期、作息和单次调整。\n"
            + (changed ? "导入后又修改过课表，这些课程修改也会回退。\n" : "") + "清单、普通提醒、番茄钟不会回退。";
        var confirmation = new CourseFormWindow(this, "撤销上次导入 · 差异预览");
        confirmation.Note(message);
        foreach (var course in _store.Data.Courses.Where(c => snapshot.Courses.All(old => old.Id != c.Id)))
            confirmation.Note($"将移除：{course.Name} · {course.DayOfWeek} · {course.StartTime:HH:mm}");
        foreach (var course in snapshot.Courses)
        {
            var current = _store.Data.Courses.FirstOrDefault(c => c.Id == course.Id);
            var copy = CourseDataCopy.Clone(course); copy.LastAlertedDate = null;
            var currentCopy = current is null ? null : CourseDataCopy.Clone(current);
            if (currentCopy is not null) currentCopy.LastAlertedDate = null;
            if (currentCopy is null || System.Text.Json.JsonSerializer.Serialize(copy) != System.Text.Json.JsonSerializer.Serialize(currentCopy))
                confirmation.Note($"将恢复：{course.Name} · {course.DayOfWeek} · 第{course.StartWeek}–{course.EndWeek}周 · {course.Location}");
        }
        confirmation.Note($"学期起点：{_store.Data.Semester.StartDate:yyyy-MM-dd} → {snapshot.Semester.StartDate:yyyy-MM-dd}\n单次调整：{_store.Data.CourseAdjustments.Count} → {snapshot.Adjustments.Count}条。作息将恢复为导入前版本。");
        confirmation.Submit = _ => true;
        if (confirmation.ShowDialog() != true) return;
        try
        {
            _importer.Undo();
            SemesterStartPicker.SelectedDateChanged -= SemesterStartPicker_Changed;
            SemesterStartPicker.SelectedDate = _store.Data.Semester.StartDate.ToDateTime(TimeOnly.MinValue);
            SemesterStartPicker.SelectedDateChanged += SemesterStartPicker_Changed;
            RefreshList();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { ValidationText.Text = "撤销保存失败，原课表未改变，请检查数据目录权限。"; }
    }

    private async void ImageImport_Click(object sender, RoutedEventArgs e)
    {
        var file = new OpenFileDialog { Filter = "课表图片 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (file.ShowDialog(this) != true) return;
        try
        {
            var progress = new CourseImportProgressWindow(this);
            var report = new Progress<string>(stage => progress.Stage = stage);
            var preview = await progress.RunAsync(token => new CourseImageImporter().PreviewAsync(file.FileName,
                CourseDataCopy.Clone(_store.Data.Semester), report, token));
            if (preview is null) return;
            var window = new CourseImportPreviewWindow(preview, _store) { Owner = this };
            if (window.ShowDialog() != true) return;
            _importer.Apply(preview, window.ReplaceCurrentSemester); RefreshList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ValidationText.Text = ex is ArgumentException ? ex.Message : "本地识图失败，请确认使用完整测试包，或改用 Excel 导入。图片未上传。"; }
    }

    private void SchoolImport_Click(object sender, RoutedEventArgs e)
    {
        var school = new SchoolImportWindow(this, _store);
        school.ShowDialog();
        if (school.CleanupNotice is not null) ValidationText.Text = school.CleanupNotice;
        if (school.Preview is not { } preview) return;
        var window = new CourseImportPreviewWindow(preview, _store) { Owner = this };
        if (window.ShowDialog() != true) return;
        try { _importer.Apply(preview, window.ReplaceCurrentSemester); RefreshList(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ValidationText.Text = "保存导入失败，请检查数据目录权限。"; }
    }

    private void SchoolPreset_Click(object sender, RoutedEventArgs e)
    {
        if (new ConfirmActionWindow("使用学校学期预设？", "湖南工程学院2026–2027第一学期：第1周从2026-09-06周日开始。将更新学期起点；不修改课程内容或自行推导节假日停课。", "使用预设") { Owner = this }.ShowDialog() != true) return;
        var semester = CourseDataCopy.Clone(_store.Data.Semester);
        semester.Name = "2026–2027 第一学期"; semester.StartDate = new DateOnly(2026, 9, 6);
        if (!ConfirmConflicts(CourseScheduleService.FindConflicts(_store.Data.Courses, semester, _store.Data.CourseAdjustments))) return;
        _store.Data.Semester = semester;
        SemesterStartPicker.SelectedDateChanged -= SemesterStartPicker_Changed;
        SemesterStartPicker.SelectedDate = new DateTime(2026, 9, 6);
        SemesterStartPicker.SelectedDateChanged += SemesterStartPicker_Changed;
        _weekStart = WeekStart(DateOnly.FromDateTime(DateTime.Today));
        _store.Data.PendingAlerts.RemoveAll(a => a.Source == AlertSource.Course);
        _store.Save(); RefreshList();
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
