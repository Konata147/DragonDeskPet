using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DragonDeskPet;
using DragonDeskPet.Core;
using DragonDeskPet.Services;

internal static class CourseLayoutChecks
{
    public static int Render(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                var app = new App(); app.InitializeComponent();
                var bubble = new ProductivityBubble(); bubble.ShowPage(2);
                Save(bubble, 348, 448, Path.Combine(directory, "focus.png"));
                var store = new ProductivityStore(Path.Combine(directory, "isolated-data"));
                store.Data.Courses.Add(new CourseItem { Name = "这是用于检查完整显示的很长的课程名称", Teacher = "测试教师甲、测试教师乙", Location = "测试教学楼A栋203室", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 40), DayOfWeek = DayOfWeek.Monday });
                store.Data.Courses.Add(new CourseItem { Name = "高等数学", Teacher = "测试教师", Location = "B102", StartTime = new TimeOnly(10, 10), EndTime = new TimeOnly(11, 50), DayOfWeek = DayOfWeek.Tuesday });
                store.Data.Courses.Add(new CourseItem { Name = "大学英语", Teacher = "测试教师", Location = "C201", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 40), DayOfWeek = DayOfWeek.Wednesday });
                var manager = new CourseManagerWindow(store, new CourseScheduleService(store), new CourseScheduleImporter(store));
                ((Grid)manager.Content).Background = manager.Background;
                Save((FrameworkElement)manager.Content, 690, 700, Path.Combine(directory, "manager.png"));
                var toggle = Descendants(manager.Content as DependencyObject).OfType<System.Windows.Controls.Button>().First(b => Equals(b.Content, "列表 / 周课表"));
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Save((FrameworkElement)manager.Content, 690, 700, Path.Combine(directory, "week.png"));
                var cards = Descendants((DependencyObject)manager.Content).OfType<System.Windows.Controls.Button>().Where(b => Equals(b.Tag, "WeekCourseCard")).ToList();
                if (cards.Count != 3) throw new Exception("Expected three synthetic weekly cards.");
                var weekGrid = (Grid)manager.FindName("WeekColumns");
                var cells = weekGrid.Children.OfType<StackPanel>().Where(p => Equals(p.Tag, "WeekTimeCell")).ToList();
                if (cells.Count != 14 || cells.Single(p => Grid.GetColumn(p) == 2 && Grid.GetRow(p) == 1).Children.Count != 0
                    || cells.Single(p => Grid.GetColumn(p) == 1 && Grid.GetRow(p) == 2).Children.Count != 0)
                    throw new Exception("Empty weekly time slots must stay empty.");
                var monday = cells.Single(p => Grid.GetColumn(p) == 1 && Grid.GetRow(p) == 1);
                var tuesday = cells.Single(p => Grid.GetColumn(p) == 2 && Grid.GetRow(p) == 2);
                var wednesday = cells.Single(p => Grid.GetColumn(p) == 3 && Grid.GetRow(p) == 1);
                if (Math.Abs(monday.TranslatePoint(new System.Windows.Point(), weekGrid).Y - wednesday.TranslatePoint(new System.Windows.Point(), weekGrid).Y) > .1
                    || tuesday.TranslatePoint(new System.Windows.Point(), weekGrid).Y <= monday.TranslatePoint(new System.Windows.Point(), weekGrid).Y + monday.ActualHeight)
                    throw new Exception("Weekday classes did not align by actual start time.");
                if (Descendants(weekGrid).OfType<TextBlock>().Any(t => t.Text == "无课")) throw new Exception("Empty-day placeholder should not be displayed.");
                foreach (var card in cards)
                {
                    var fields = Descendants(card).OfType<TextBlock>().ToList();
                    var title = fields.Single(t => Equals(t.Tag, "WeekCardName"));
                    var room = fields.Single(t => Equals(t.Tag, "WeekCard地点"));
                    var teacher = fields.Single(t => Equals(t.Tag, "WeekCard教师"));
                    if (Math.Abs(room.TranslatePoint(new System.Windows.Point(), card).X - teacher.TranslatePoint(new System.Windows.Point(), card).X) > .1
                        || title.TextWrapping != TextWrapping.Wrap || title.ActualHeight < 40 || room.ActualWidth < 60)
                        throw new Exception("Weekly card fields are not aligned or lack wrapping space.");
                }
                var notice = (TextBlock)manager.FindName("ValidationText");
                notice.Text = CourseImportErrors.Describe(new IOException("private filename must not be shown", unchecked((int)0x80070020)));
                Save((FrameworkElement)manager.Content, 690, 700, Path.Combine(directory, "week-error.png"));
                for (DependencyObject? parent = notice; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                    if (parent is UIElement ui && ui.Visibility == Visibility.Collapsed) throw new Exception("Import error hidden in week mode.");
                var position = notice.TranslatePoint(new System.Windows.Point(), (UIElement)manager.Content);
                if (notice.ActualHeight < 20 || position.Y + notice.ActualHeight > 700) throw new Exception("Import error outside visible footer.");
                Save((FrameworkElement)manager.Content, 620, 550, Path.Combine(directory, "week-error-small.png"));
                position = notice.TranslatePoint(new System.Windows.Point(), (UIElement)manager.Content);
                if (position.Y + notice.ActualHeight > 550) throw new Exception("Import error outside small-window footer.");
                var preview = new CourseImportPreview(); preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Add, store.Data.Courses[0], "测试来源"));
                var window = new CourseImportPreviewWindow(preview, store);
                ((Grid)window.Content).Background = window.Background;
                Save((FrameworkElement)window.Content, 560, 490, Path.Combine(directory, "preview.png"));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) { Console.WriteLine(failure.ToString()); return 1; }
        Console.WriteLine("Rendered focus, manager and preview with isolated data."); return 0;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root is null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Save(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
