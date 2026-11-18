using System;
using System.Drawing;
using System.Windows.Forms;

namespace Itrans.Controls;

public sealed class TrayController : IDisposable
{
    private readonly App _app;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _captureItem;

    public TrayController(App app)
    {
        _app = app;
        _icon = TrayIconFactory.CreateIcon();

        _captureItem = new ToolStripMenuItem();
        var showItem = new ToolStripMenuItem("显示主窗口");
        var settingsItem = new ToolStripMenuItem("设置…");
        var exitItem = new ToolStripMenuItem("退出");

        _menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9f),
            ShowImageMargin = false,
        };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _captureItem,
            showItem,
            settingsItem,
            new ToolStripSeparator(),
            exitItem,
        });

        _captureItem.Click += (_, _) => _app.Home?.TriggerCapture();
        showItem.Click += (_, _) => _app.Home?.ShowMainWindow();
        settingsItem.Click += (_, _) => _app.Home?.OpenSettings();
        exitItem.Click += (_, _) => _app.ExitApp();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "itrans 截图翻译",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _app.Home?.ShowMainWindow();

        UpdateCaptureText();
        _app.SettingsChanged += UpdateCaptureText;
    }

    private void UpdateCaptureText() => _captureItem.Text = $"截图翻译 ({_app.Settings.Hotkey})";

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _app.SettingsChanged -= UpdateCaptureText;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
