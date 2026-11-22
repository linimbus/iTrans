using System;
using System.ComponentModel;
using System.Windows;
using Itrans.Localization;
using Itrans.Services;

namespace Itrans;

public partial class MainWindow : Window
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private bool _allowClose;
    private bool _trayNoticeShown;
    private ScreenshotTranslationWindow? _screenshotWindow;
    private FileTranslationWindow? _fileWindow;
    private AudioTranslationWindow? _audioWindow;
    private SettingsWindow? _settingsWindow;
    private SelectionTranslationWindow? _selectionWindow;
    private string _statusKey = "main.statusReady";
    private object?[] _statusArgs = Array.Empty<object?>();

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.Title;
        App.Instance.Health.StateChanged += UpdateServiceDot;
        Loc.LanguageChanged += OnLanguageChanged;
        App.Instance.SettingsChanged += UpdateSettingsText;
        Closed += OnClosedCleanup;
        Closing += OnClosing;
        UpdateSettingsText();
        UpdateServiceDot();
        SetStatus("main.statusReady", App.Instance.Settings.Hotkey);
    }

    private void UpdateSettingsText()
    {
        var settings = App.Instance.Settings;
        SetHotkeyText(CaptureHotkeyText, settings.Hotkey);
        SetHotkeyText(SelectionHotkeyText, settings.SelectionTranslationHotkey);
        SetHotkeyText(AudioHotkeyText, settings.AudioTranslationHotkey);
        SetHotkeyText(FileHotkeyText, settings.FileTranslationHotkey);
        SetHotkeyText(SettingsHotkeyText, settings.SettingsHotkey);
        App.Instance.Health.Check(true);
    }

    private static void SetHotkeyText(System.Windows.Controls.TextBlock textBlock, string? hotkey)
    {
        var value = hotkey?.Trim() ?? "";
        textBlock.Text = value;
        textBlock.Visibility = value.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLanguageChanged()
    {
        Title = AppInfo.Title;
        UpdateSettingsText();
        RefreshStatus();
        UpdateServiceDot();
    }

    private void UpdateServiceDot()
    {
        var health = App.Instance.Health;
        ServiceDot.Fill = ServiceHealthBrushes.For(health.State);
        ServiceDot.ToolTip = health.Message;
        System.Windows.Automation.AutomationProperties.SetName(ServiceDot, health.Message);
    }

    private void OnClosedCleanup(object? sender, EventArgs e)
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        App.Instance.Health.StateChanged -= UpdateServiceDot;
        App.Instance.SettingsChanged -= UpdateSettingsText;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        if (!_trayNoticeShown)
        {
            _trayNoticeShown = true;
            App.Instance.Tray?.ShowBalloon(Loc.T("main.trayStillRunningTitle"), Loc.T("main.trayStillRunningText"));
        }
    }

    public void AllowClose() => _allowClose = true;

    public void ShowMainWindow()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        App.Instance.Health.Check();
    }

    public void TriggerCapture()
    {
        var window = EnsureScreenshotWindow();
        window.StartCapture();
    }

    private ScreenshotTranslationWindow EnsureScreenshotWindow()
    {
        if (_screenshotWindow != null) return _screenshotWindow;

        var window = new ScreenshotTranslationWindow();
        window.Closed += (_, _) => _screenshotWindow = null;
        _screenshotWindow = window;
        return window;
    }

    public void TriggerSelectionTranslation()
    {
        var text = SelectedTextProvider.TryGetSelectedText();
        ShowSelectionTranslation(text);
    }

    public void ShowSelectionTranslation(string? text)
    {
        if (_selectionWindow != null)
        {
            if (_selectionWindow.IsVisible)
            {
                _selectionWindow.SetText(text);
                _selectionWindow.Activate();
                return;
            }
            _selectionWindow = null;
        }

        var window = new SelectionTranslationWindow(text);
        window.Closed += (_, _) => _selectionWindow = null;
        _selectionWindow = window;
        window.Show();
        window.Activate();
    }

    public void OpenAudioTranslation()
    {
        if (_audioWindow != null)
        {
            if (_audioWindow.IsVisible)
            {
                _audioWindow.Activate();
                return;
            }
            _audioWindow = null;
        }

        var window = new AudioTranslationWindow();
        window.Closed += (_, _) => _audioWindow = null;
        _audioWindow = window;
        window.Show();
        window.Activate();
    }

    public void OpenFileTranslation()
    {
        if (_fileWindow != null)
        {
            if (_fileWindow.IsVisible)
            {
                _fileWindow.Activate();
                return;
            }
            _fileWindow = null;
        }

        var window = new FileTranslationWindow();
        window.Closed += (_, _) => _fileWindow = null;
        _fileWindow = window;
        window.Show();
        window.Activate();
    }

    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.IsVisible)
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = null;
        }

        var dialog = new SettingsWindow(App.Instance.Settings.Clone());
        _settingsWindow = dialog;
        bool? result;
        try
        {
            if (IsVisible) dialog.Owner = this;
            result = dialog.ShowDialog();
        }
        finally
        {
            _settingsWindow = null;
        }

        if (result != true) return;

        var error = App.Instance.ApplySettings(dialog.Result);
        UpdateSettingsText();
        if (error != null)
        {
            SetStatus("main.settingsSavedHotkeyFailed", error);
            App.Instance.Tray?.ShowBalloon(Loc.T("main.hotkeyRegisterFailedTitle"), error);
        }
        else
        {
            SetStatus("main.settingsSaved");
            App.Instance.Tray?.ShowBalloon(Loc.T("main.settingsSaved"), Loc.Format("main.settingsSavedBalloon", App.Instance.Settings.Hotkey));
        }
    }

    private void Capture_Click(object sender, RoutedEventArgs e) => TriggerCapture();

    private void Selection_Click(object sender, RoutedEventArgs e) => TriggerSelectionTranslation();

    private void Audio_Click(object sender, RoutedEventArgs e) => OpenAudioTranslation();

    private void File_Click(object sender, RoutedEventArgs e) => OpenFileTranslation();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText.Text = Loc.Format(key, args);
    }

    private void RefreshStatus() => StatusText.Text = Loc.Format(_statusKey, _statusArgs);
}
