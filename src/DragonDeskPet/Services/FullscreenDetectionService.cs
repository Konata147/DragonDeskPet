using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace DragonDeskPet.Services;

public interface IFullscreenDetectionService
{
    bool IsForegroundWindowFullscreen();
}

public sealed class FullscreenDetectionService : IFullscreenDetectionService
{
    private const uint MonitorDefaultToNearest = 2;
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    public bool IsForegroundWindowFullscreen()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window))
            {
                return false;
            }

            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == Environment.ProcessId || processId == 0)
            {
                return false;
            }

            var className = new StringBuilder(128);
            _ = GetClassName(window, className, className.Capacity);
            if (ShellWindowClasses.Contains(className.ToString()))
            {
                return false;
            }

            if (!GetWindowRect(window, out var windowRect))
            {
                return false;
            }

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            return CoversMonitor(windowRect.ToRectangle(), monitorInfo.Monitor.ToRectangle());
        }
        catch
        {
            // Fullscreen detection is a convenience feature and must never break the pet.
            return false;
        }
    }

    public static bool CoversMonitor(Rectangle windowBounds, Rectangle monitorBounds, int tolerance = 4)
    {
        if (windowBounds.Width <= 0 || windowBounds.Height <= 0
            || monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
        {
            return false;
        }

        return windowBounds.Left <= monitorBounds.Left + tolerance
            && windowBounds.Top <= monitorBounds.Top + tolerance
            && windowBounds.Right >= monitorBounds.Right - tolerance
            && windowBounds.Bottom >= monitorBounds.Bottom - tolerance;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }
}
