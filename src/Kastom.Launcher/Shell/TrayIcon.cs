using Kastom.Launcher.Resources;

namespace Kastom.Launcher.Shell;

/// <summary>Значок в області сповіщень: ліва кнопка — швидка панель, права — меню.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _menu;
    private readonly System.Drawing.Icon _icon;
    private readonly System.Windows.Forms.ToolStripMenuItem _widgetItem;

    public TrayIcon(
        Action openMainWindow,
        Action toggleQuickPanel,
        Func<bool> isWidgetVisible,
        Action toggleWidget,
        Action checkUpdates,
        Action exit,
        Action balloonClicked)
    {
        _menu = new System.Windows.Forms.ContextMenuStrip();
        _menu.Items.Add(Strings.TrayOpen, null, (_, _) => openMainWindow());
        _menu.Items.Add(Strings.TrayQuickPanel, null, (_, _) => toggleQuickPanel());
        _widgetItem = new System.Windows.Forms.ToolStripMenuItem(Strings.TrayWidget);
        _widgetItem.Click += (_, _) => toggleWidget();
        _menu.Items.Add(_widgetItem);
        _menu.Items.Add(Strings.TrayCheckUpdates, null, (_, _) => checkUpdates());
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _menu.Items.Add(Strings.TrayExit, null, (_, _) => exit());
        _menu.Opening += (_, _) =>
        {
            try { _widgetItem.Checked = isWidgetVisible(); }
            catch (Exception) { _widgetItem.Checked = false; }
        };

        var iconSize = System.Windows.Forms.SystemInformation.SmallIconSize;
        _icon = AppIcon.Create(Math.Max(iconSize.Width, 16));
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _icon,
            Text = Strings.TrayTooltip,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) toggleQuickPanel();
        };
        _notifyIcon.BalloonTipClicked += (_, _) => balloonClicked();
    }

    public void ShowBalloon(string title, string text) =>
        _notifyIcon.ShowBalloonTip(8000, title, text, System.Windows.Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
