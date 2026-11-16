using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace QuickRewrite.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private Icon? _ownedIcon;
    private MemoryStream? _iconStream;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var menu = new ContextMenuStrip();
        var settingsItem = new ToolStripMenuItem("打开设置");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        var exitItem = new ToolStripMenuItem("退出 QuickRewrite");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        var icon = LoadAppIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "QuickRewrite - 快捷改写",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    private Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/QuickRewrite;component/Assets/app.ico", UriKind.Absolute));
            if (resource?.Stream is not null)
            {
                _iconStream = new MemoryStream();
                using (var source = resource.Stream)
                {
                    source.CopyTo(_iconStream);
                }

                _iconStream.Position = 0;
                _ownedIcon = new Icon(_iconStream, SystemInformation.SmallIconSize);
                return _ownedIcon;
            }
        }
        catch
        {
            // 资源缺失时退回系统图标，避免托盘初始化失败。
        }

        return SystemIcons.Application;
    }

    public void ShowInfo(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
    }

    public void ShowWarning(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(4000, title, message, ToolTipIcon.Warning);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _ownedIcon?.Dispose();
        _iconStream?.Dispose();
    }
}