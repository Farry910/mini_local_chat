using System.Drawing;
using System.Windows.Forms;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Services;

/// <summary>Notification-area icon. Left click opens the app; the menu is the only way to exit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _normal = AppIcons.CreateIcon(32, unreadDot: false);
    private readonly Icon _unread = AppIcons.CreateIcon(32, unreadDot: true);

    public TrayIcon(Action open, Action alarms, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        var openItem = menu.Items.Add("Open Mini Local Chat", null, (_, _) => open());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add("Alarms…", null, (_, _) => alarms());
        menu.Items.Add("Settings…", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = _normal,
            Text = "Mini Local Chat",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                open();
            }
        };
    }

    public void SetUnread(int count)
    {
        _icon.Icon = count > 0 ? _unread : _normal;
        _icon.Text = count switch
        {
            0 => AppInfo.Title,
            1 => $"{AppInfo.Title} – 1 new message",
            _ => $"{AppInfo.Title} – {count} new messages",
        };
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _normal.Dispose();
        _unread.Dispose();
    }
}
