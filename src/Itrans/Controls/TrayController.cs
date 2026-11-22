using System;
using System.Drawing;
using System.Windows.Forms;
using Itrans.Localization;

namespace Itrans.Controls;

public sealed class TrayController : IDisposable
{
    private readonly App _app;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _selectionItem;
    private readonly ToolStripMenuItem _audioItem;
    private readonly ToolStripMenuItem _fileItem;
    private readonly ToolStripMenuItem _showItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _exitItem;

    public TrayController(App app)
    {
        _app = app;
        _icon = TrayIconFactory.CreateIcon();

        _captureItem = new ToolStripMenuItem();
        _selectionItem = new ToolStripMenuItem();
        _audioItem = new ToolStripMenuItem();
        _fileItem = new ToolStripMenuItem();
        _showItem = new ToolStripMenuItem();
        _settingsItem = new ToolStripMenuItem();
        _exitItem = new ToolStripMenuItem();

        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
        };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _captureItem,
            _selectionItem,
            _audioItem,
            _fileItem,
            _showItem,
            _settingsItem,
            new ToolStripSeparator(),
            _exitItem,
        });

        _captureItem.Click += (_, _) => _app.Home?.TriggerCapture();
        _selectionItem.Click += (_, _) => _app.Home?.TriggerSelectionTranslation();
        _audioItem.Click += (_, _) => _app.Home?.OpenAudioTranslation();
        _fileItem.Click += (_, _) => _app.Home?.OpenFileTranslation();
        _showItem.Click += (_, _) => _app.Home?.ShowMainWindow();
        _settingsItem.Click += (_, _) => _app.Home?.OpenSettings();
        _exitItem.Click += (_, _) => _app.ExitApp();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _app.Home?.ShowMainWindow();

        UpdateTexts();
        _app.SettingsChanged += UpdateTexts;
        LocalizationService.Instance.LanguageChanged += UpdateTexts;
    }

    private void UpdateTexts()
    {
        var loc = LocalizationService.Instance;
        var settings = _app.Settings;
        _captureItem.Text = WithHotkey(loc.T("tray.capture"), settings.Hotkey);
        _selectionItem.Text = WithHotkey(loc.T("tray.selection"), settings.SelectionTranslationHotkey);
        _audioItem.Text = WithHotkey(loc.T("tray.audio"), settings.AudioTranslationHotkey);
        _fileItem.Text = WithHotkey(loc.T("tray.file"), settings.FileTranslationHotkey);
        _showItem.Text = loc.T("tray.show");
        _settingsItem.Text = WithHotkey(loc.T("tray.settings"), settings.SettingsHotkey);
        _exitItem.Text = loc.T("tray.exit");
        _notifyIcon.Text = loc.T("tray.tooltip");
    }

    private static string WithHotkey(string name, string? hotkey)
        => string.IsNullOrWhiteSpace(hotkey) ? name : name + "  (" + hotkey.Trim() + ")";

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _app.SettingsChanged -= UpdateTexts;
        LocalizationService.Instance.LanguageChanged -= UpdateTexts;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
