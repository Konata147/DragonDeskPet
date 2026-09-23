using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DragonDeskPet.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon? _customIcon;

    public TrayIconService(Action show, Action hide, Action openSettings, Action exit, string? iconPath = null)
    {
        if (iconPath is { Length: > 0 } resolvedIconPath && File.Exists(resolvedIconPath))
        {
            try
            {
                _customIcon = new Icon(resolvedIconPath);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                _customIcon = null;
            }
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示桌宠", null, (_, _) => show());
        menu.Items.Add("隐藏桌宠", null, (_, _) => hide());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => openSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = _customIcon ?? SystemIcons.Application,
            Text = "龙娘桌宠",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => show();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _customIcon?.Dispose();
    }
}
