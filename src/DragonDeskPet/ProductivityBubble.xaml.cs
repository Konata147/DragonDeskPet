using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;

namespace DragonDeskPet;

public partial class ProductivityBubble : UserControl
{
    private IProductivityStore? _store;
    private ReminderService? _reminders;
    private TodoService? _todos;
    private CourseScheduleService? _courses;
    private PomodoroService? _pomodoro;
    private ReminderDraft? _pendingDraft;
    private Guid? _editingReminderId;
    private Guid? _editingTodoId;
    private readonly DispatcherTimer _displayTimer;

    public ProductivityBubble()
    {
        InitializeComponent();
        ReminderDatePicker.SelectedDate = DateTime.Today;
        UpdateReminderRepeatMode();
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _displayTimer.Tick += (_, _) => RefreshPomodoro();
        _displayTimer.Start();
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? CourseManagementRequested;

    public void Initialize(IProductivityStore store, ReminderService reminders, TodoService todos, CourseScheduleService courses, PomodoroService pomodoro)
    {
        _store = store;
        _reminders = reminders;
        _todos = todos;
        _courses = courses;
        _pomodoro = pomodoro;
        FocusMinutesInput.Text = ((App)Application.Current).Settings.PomodoroFocusMinutes.ToString(CultureInfo.InvariantCulture);
        _pomodoro.StateChanged += (_, _) => Dispatcher.BeginInvoke(RefreshAll);
        RefreshAll();
    }

    public void ShowPage(int index)
    {
        Pages.SelectedIndex = Math.Clamp(index, 0, 2);
        RefreshAll();
    }

    public void PrefillReminder(ReminderDraft draft)
    {
        ShowPage(1);
        _editingReminderId = null;
        ReminderMessageBox.Text = draft.Message;
        var local = TimeZoneInfo.ConvertTime(draft.DueUtc, TimeZoneInfo.Local);
        ReminderDatePicker.SelectedDate = local.Date;
        ReminderTimeBox.Text = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        ReminderRepeatBox.SelectedIndex = draft.Repeat == ReminderRepeat.Daily ? 1 : 0;
        _pendingDraft = draft;
        ShowReminderPreview(draft);
    }

    public void ShowReminderError(string? message)
    {
        ShowPage(1);
        _pendingDraft = null;
        ReminderPreviewText.Text = message ?? "请使用下面的表单填写提醒。";
        ReminderConfirmation.Visibility = Visibility.Visible;
    }

    public void RefreshAll()
    {
        if (_store is null)
        {
            return;
        }

        RefreshToday();
        RefreshReminders();
        RefreshPomodoro();
    }

    private void RefreshToday()
    {
        if (_store is null || _todos is null || _courses is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        _todos.Maintain(today);
        var week = _courses.GetTeachingWeek(today);
        TeachingWeekText.Text = week > 0 ? $"第 {week} 教学周" : "学期尚未开始";
        CoursesPanel.Children.Clear();
        var courses = _courses.GetCoursesForDate(today);
        if (courses.Count == 0)
        {
            CoursesPanel.Children.Add(EmptyText("今天没有课程安排。"));
        }
        else
        {
            foreach (var occurrence in courses)
            {
                var row = new DockPanel();
                var todoButton = new Button
                {
                    Content = "+ 待办",
                    Tag = occurrence.Course,
                    Style = (Style)FindResource("MiniButton")
                };
                todoButton.Click += CourseTodo_Click;
                DockPanel.SetDock(todoButton, Dock.Right);
                row.Children.Add(todoButton);
                var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                details.Children.Add(new TextBlock
                {
                    Text = occurrence.Course.Name,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(64, 54, 83))
                });
                details.Children.Add(new TextBlock
                {
                    Text = $"{occurrence.Course.StartTime:HH:mm}–{occurrence.Course.EndTime:HH:mm}{(string.IsNullOrWhiteSpace(occurrence.Course.Location) ? string.Empty : $"  ·  {occurrence.Course.Location}")}",
                    Margin = new Thickness(0, 3, 0, 0),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(126, 112, 140))
                });
                row.Children.Add(details);
                CoursesPanel.Children.Add(Card(row));
            }
        }

        TodosPanel.Children.Clear();
        var todos = _todos.GetForDate(today);
        if (todos.Count == 0)
        {
            TodosPanel.Children.Add(EmptyText("还没有待办事项。"));
        }
        else
        {
            foreach (var todo in todos)
            {
                var row = new DockPanel();
                var delete = new Button
                {
                    Content = "×",
                    Width = 25,
                    Height = 25,
                    Padding = new Thickness(0),
                    Tag = todo.Id,
                    Style = (Style)FindResource("MiniButton")
                };
                delete.Click += DeleteTodo_Click;
                DockPanel.SetDock(delete, Dock.Right);
                row.Children.Add(delete);
                var edit = new Button
                {
                    Content = "编辑",
                    Width = 36,
                    Height = 25,
                    Padding = new Thickness(0),
                    Tag = todo,
                    Style = (Style)FindResource("MiniButton")
                };
                edit.Click += EditTodo_Click;
                DockPanel.SetDock(edit, Dock.Right);
                row.Children.Add(edit);
                var check = new CheckBox { Content = todo.Text, IsChecked = todo.IsCompleted, Tag = todo.Id, VerticalAlignment = VerticalAlignment.Center };
                check.Checked += TodoCompletionChanged;
                check.Unchecked += TodoCompletionChanged;
                row.Children.Add(check);
                TodosPanel.Children.Add(Card(row, compact: true));
            }
        }
    }

    private void RefreshReminders()
    {
        if (_reminders is null)
        {
            return;
        }

        RemindersPanel.Children.Clear();
        var reminders = _reminders.GetReminders();
        if (reminders.Count == 0)
        {
            RemindersPanel.Children.Add(EmptyText("还没有提醒。"));
            return;
        }

        foreach (var reminder in reminders)
        {
            var local = TimeZoneInfo.ConvertTime(reminder.NextDueUtc, TimeZoneInfo.Local);
            var row = new DockPanel();
            var delete = new Button
            {
                Content = "×",
                Width = 25,
                Height = 25,
                Padding = new Thickness(0),
                Tag = reminder.Id,
                Style = (Style)FindResource("MiniButton")
            };
            delete.Click += DeleteReminder_Click;
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(delete);
            var edit = new Button { Content = "编辑", Tag = reminder, Style = (Style)FindResource("MiniButton") };
            edit.Click += EditReminder_Click;
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(edit);
            var enabled = new CheckBox
            {
                IsChecked = reminder.IsEnabled,
                Tag = reminder.Id,
                Content = reminder.Repeat == ReminderRepeat.Daily
                    ? $"{reminder.Message}\n每天 {local:HH:mm}（下次 {local:MM-dd}）"
                    : $"{reminder.Message}\n{local:MM-dd HH:mm}",
                VerticalAlignment = VerticalAlignment.Center
            };
            enabled.Checked += ReminderEnabledChanged;
            enabled.Unchecked += ReminderEnabledChanged;
            row.Children.Add(enabled);
            RemindersPanel.Children.Add(Card(row));
        }
    }

    private void RefreshPomodoro()
    {
        if (_pomodoro is null)
        {
            return;
        }

        var state = _pomodoro.State;
        PomodoroPhaseText.Text = state.Phase switch
        {
            PomodoroPhase.Focus => "专注",
            PomodoroPhase.ShortBreak => "短休息",
            PomodoroPhase.LongBreak => "长休息",
            _ => "专注"
        };
        var remaining = _pomodoro.GetRemaining(DateTimeOffset.UtcNow);
        if (!state.IsRunning && !state.IsPaused)
        {
            remaining = state.Phase switch
            {
                PomodoroPhase.Focus => TimeSpan.FromMinutes(((App)Application.Current).Settings.PomodoroFocusMinutes),
                PomodoroPhase.ShortBreak => TimeSpan.FromMinutes(((App)Application.Current).Settings.PomodoroShortBreakMinutes),
                _ => TimeSpan.FromMinutes(((App)Application.Current).Settings.PomodoroLongBreakMinutes)
            };
        }

        PomodoroTimeText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        PomodoroRoundsText.Text = $"已完成 {state.CompletedFocusRounds} 轮";
        PomodoroStartButton.Visibility = !state.IsRunning && !state.IsPaused && !state.IsAwaitingNextPhase ? Visibility.Visible : Visibility.Collapsed;
        PomodoroPauseButton.Visibility = state.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        PomodoroResumeButton.Visibility = state.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        PomodoroNextButton.Visibility = state.IsAwaitingNextPhase ? Visibility.Visible : Visibility.Collapsed;
        PomodoroCancelButton.Visibility = state.IsRunning || state.IsPaused || state.IsAwaitingNextPhase ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock EmptyText(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 4, 0, 4),
        Foreground = new SolidColorBrush(Color.FromRgb(130, 117, 143)),
        FontSize = 12
    };

    private static Border Card(UIElement content, bool compact = false) => new()
    {
        Child = content,
        Margin = new Thickness(0, 2, 0, 2),
        Padding = compact ? new Thickness(9, 6, 7, 6) : new Thickness(10, 8, 8, 8),
        Background = new SolidColorBrush(Color.FromRgb(247, 243, 252)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(232, 224, 244)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12)
    };

    private void AddTodo_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void TodoTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddTodo();
        }
    }

    private void AddTodo()
    {
        var text = TodoTextBox.Text.Trim();
        if (text.Length == 0 || _todos is null)
        {
            return;
        }

        if (_editingTodoId is { } id)
        {
            _todos.Update(id, text);
        }
        else
        {
            _todos.Add(text, DateOnly.FromDateTime(DateTime.Today));
        }

        ResetTodoEditor();
        RefreshToday();
    }

    private void EditTodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TodoItem todo })
        {
            return;
        }

        _editingTodoId = todo.Id;
        TodoTextBox.Text = todo.Text;
        TodoAddButton.Content = "保存修改";
        TodoCancelEditButton.Visibility = Visibility.Visible;
        TodoTextBox.Focus();
        TodoTextBox.SelectAll();
    }

    private void CancelTodoEdit_Click(object sender, RoutedEventArgs e) => ResetTodoEditor();

    private void ResetTodoEditor()
    {
        _editingTodoId = null;
        TodoTextBox.Clear();
        TodoAddButton.Content = "添加";
        TodoCancelEditButton.Visibility = Visibility.Collapsed;
    }

    private void CourseTodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CourseItem course } && _todos is not null)
        {
            _todos.Add($"准备{course.Name}", DateOnly.FromDateTime(DateTime.Today));
            RefreshToday();
        }
    }

    private void TodoCompletionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: Guid id } check && _todos is not null)
        {
            _todos.SetCompleted(id, check.IsChecked == true, DateTimeOffset.UtcNow);
            RefreshToday();
        }
    }

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id } && _todos is not null)
        {
            _todos.Delete(id);
            if (_editingTodoId == id)
            {
                ResetTodoEditor();
            }
            RefreshToday();
        }
    }

    private void PreviewReminder_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildDraft(out var draft, out var error))
        {
            ReminderPreviewText.Text = error;
            ReminderConfirmation.Visibility = Visibility.Visible;
            return;
        }

        _pendingDraft = draft;
        ShowReminderPreview(draft!);
    }

    private bool TryBuildDraft(out ReminderDraft? draft, out string error)
    {
        draft = null;
        var message = ReminderMessageBox.Text.Trim();
        if (message.Length == 0)
        {
            error = "请填写提醒内容。";
            return false;
        }

        if (!TimeOnly.TryParseExact(ReminderTimeBox.Text.Trim().Replace('：', ':'), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            error = "请用 HH:mm 填写时间。";
            return false;
        }

        var repeat = ReminderRepeatBox.SelectedIndex == 1 ? ReminderRepeat.Daily : ReminderRepeat.None;
        DateTimeOffset dueUtc;
        if (repeat == ReminderRepeat.Daily)
        {
            dueUtc = ReminderService.GetNextDailyOccurrence(time, DateTimeOffset.UtcNow);
        }
        else
        {
            if (ReminderDatePicker.SelectedDate is not { } date)
            {
                error = "请选择提醒日期。";
                return false;
            }

            var localDateTime = DateOnly.FromDateTime(date).ToDateTime(time);
            dueUtc = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime)).ToUniversalTime();
        }
        if (repeat == ReminderRepeat.None && dueUtc <= DateTimeOffset.UtcNow)
        {
            error = "提醒时间必须晚于现在。";
            return false;
        }

        draft = new ReminderDraft(message, repeat, dueUtc, repeat == ReminderRepeat.Daily ? time : null);
        error = string.Empty;
        return true;
    }

    private void ShowReminderPreview(ReminderDraft draft)
    {
        var local = TimeZoneInfo.ConvertTime(draft.DueUtc, TimeZoneInfo.Local);
        ReminderPreviewText.Text = draft.Repeat == ReminderRepeat.Daily
            ? $"每天 {local:HH:mm} 提醒：{draft.Message}"
            : $"{local:yyyy-MM-dd HH:mm} 提醒：{draft.Message}";
        ReminderConfirmation.Visibility = Visibility.Visible;
    }

    private void ConfirmReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDraft is null || _reminders is null)
        {
            return;
        }

        if (_editingReminderId is { } id)
        {
            _reminders.Update(new ReminderItem
            {
                Id = id,
                Message = _pendingDraft.Message,
                Repeat = _pendingDraft.Repeat,
                NextDueUtc = _pendingDraft.DueUtc,
                DailyLocalTime = _pendingDraft.DailyLocalTime,
                IsEnabled = true
            });
        }
        else
        {
            _reminders.Create(_pendingDraft);
        }

        _editingReminderId = null;
        _pendingDraft = null;
        ReminderMessageBox.Clear();
        ReminderConfirmation.Visibility = Visibility.Collapsed;
        RefreshReminders();
    }

    private void CancelReminderPreview_Click(object sender, RoutedEventArgs e)
    {
        _pendingDraft = null;
        ReminderConfirmation.Visibility = Visibility.Collapsed;
    }

    private void EditReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ReminderItem reminder })
        {
            return;
        }

        _editingReminderId = reminder.Id;
        var local = TimeZoneInfo.ConvertTime(reminder.NextDueUtc, TimeZoneInfo.Local);
        ReminderMessageBox.Text = reminder.Message;
        ReminderDatePicker.SelectedDate = local.Date;
        ReminderTimeBox.Text = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        ReminderRepeatBox.SelectedIndex = reminder.Repeat == ReminderRepeat.Daily ? 1 : 0;
        ReminderConfirmation.Visibility = Visibility.Collapsed;
    }

    private void ReminderRepeatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReminderRepeatBox is null || ReminderDatePicker is null || DailyRepeatHint is null)
        {
            return;
        }

        UpdateReminderRepeatMode();
        InvalidateReminderPreview();
    }

    private void ReminderTextChanged(object sender, TextChangedEventArgs e) => InvalidateReminderPreview();

    private void ReminderDateChanged(object sender, SelectionChangedEventArgs e) => InvalidateReminderPreview();

    private void InvalidateReminderPreview()
    {
        _pendingDraft = null;
        if (ReminderConfirmation is not null)
        {
            ReminderConfirmation.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateReminderRepeatMode()
    {
        var daily = ReminderRepeatBox.SelectedIndex == 1;
        ReminderDatePicker.Visibility = daily ? Visibility.Collapsed : Visibility.Visible;
        DailyRepeatHint.Visibility = daily ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id } && _reminders is not null)
        {
            _reminders.Delete(id);
            RefreshReminders();
        }
    }

    private void ReminderEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: Guid id } check && _reminders is not null)
        {
            _reminders.SetEnabled(id, check.IsChecked == true);
        }
    }

    private void PomodoroStart_Click(object sender, RoutedEventArgs e) => _pomodoro?.Start(DateTimeOffset.UtcNow);
    private void PomodoroPause_Click(object sender, RoutedEventArgs e) => _pomodoro?.Pause(DateTimeOffset.UtcNow);
    private void PomodoroResume_Click(object sender, RoutedEventArgs e) => _pomodoro?.Resume(DateTimeOffset.UtcNow);
    private void PomodoroNext_Click(object sender, RoutedEventArgs e) => _pomodoro?.StartNextPhase(DateTimeOffset.UtcNow);
    private void PomodoroCancel_Click(object sender, RoutedEventArgs e) => _pomodoro?.Cancel();

    private void FocusMinutesInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveFocusMinutes();
            e.Handled = true;
        }
    }

    private void SaveFocusMinutes_Click(object sender, RoutedEventArgs e) => SaveFocusMinutes();

    private void SaveFocusMinutes()
    {
        if (!int.TryParse(FocusMinutesInput.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes is < 1 or > 240)
        {
            FocusMinutesFeedback.Text = "请输入 1–240 之间的整数分钟。";
            FocusMinutesFeedback.Foreground = new SolidColorBrush(Color.FromRgb(179, 75, 105));
            return;
        }

        var app = (App)Application.Current;
        var oldMinutes = app.Settings.PomodoroFocusMinutes;
        try
        {
            app.Settings.PomodoroFocusMinutes = minutes;
            app.SettingsService.Save(app.Settings);
            FocusMinutesInput.Text = minutes.ToString(CultureInfo.InvariantCulture);
            FocusMinutesFeedback.Text = _pomodoro?.State.IsRunning == true || _pomodoro?.State.IsPaused == true
                ? "已保存；当前计时不变，从下一轮专注生效。"
                : "已保存；下一轮专注将使用新时长。";
            FocusMinutesFeedback.Foreground = new SolidColorBrush(Color.FromRgb(103, 77, 148));
            RefreshPomodoro();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            app.Settings.PomodoroFocusMinutes = oldMinutes;
            FocusMinutesFeedback.Text = "保存失败，请检查程序目录是否可写。";
            FocusMinutesFeedback.Foreground = new SolidColorBrush(Color.FromRgb(179, 75, 105));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void ManageCourses_Click(object sender, RoutedEventArgs e) => CourseManagementRequested?.Invoke(this, EventArgs.Empty);

    private void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshAll();
        }
    }
}
