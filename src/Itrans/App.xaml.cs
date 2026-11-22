using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Itrans.Controls;
using Itrans.Interop;
using Itrans.Localization;
using Itrans.Services;

namespace Itrans;

public partial class App : Application
{
    private Mutex? _mutex;

    public static App Instance => (App)Current;

    public AppSettings Settings { get; private set; } = new();

    public OcrService Ocr { get; } = new();

    public TranslationService Translation { get; } = new();

    public FileTranslationService FileTranslation { get; } = new();

    public ServiceHealthMonitor Health { get; } = new();

    public IReadOnlyList<TranslationLanguage> Languages { get; private set; } = TranslationService.DefaultLanguages;

    private readonly HotkeyManager _captureHotkey = new();
    private readonly HotkeyManager _fileHotkey = new();
    private readonly HotkeyManager _audioHotkey = new();
    private readonly HotkeyManager _settingsHotkey = new();
    private readonly HotkeyManager _selectionHotkey = new();

    public SelectionPopupService SelectionPopup { get; } = new();

    public TrayController? Tray { get; private set; }

    public MainWindow? Home { get; private set; }

    public event Action? SettingsChanged;

    public event Action? LanguagesChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LocalizationService.Instance.SetLanguage(null);

        var createdNew = true;
        _mutex = new Mutex(true, @"Local\Itrans.SingleInstance", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show(LocalizationService.Instance.T("app.alreadyRunning"), "iTrans", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Settings = AppSettings.Load();
        Settings.UiLanguage = LocalizationService.Normalize(Settings.UiLanguage);
        LocalizationService.Instance.SetLanguage(Settings.UiLanguage);
        Home = new MainWindow();
        Tray = new TrayController(this);
        Home.Show();
        Health.Start();
        _ = RefreshLanguagesAsync();
        SelectionPopup.UpdateEnabled(Settings.SelectionPopupEnabled);

        var hotkeyError = RegisterHotkeys();
        if (hotkeyError != null)
        {
            Tray.ShowBalloon(LocalizationService.Instance.T("main.hotkeyRegisterFailedTitle"), hotkeyError);
        }
    }

    public string? RegisterHotkeys()
    {
        var errors = new List<string>();
        RegisterOne(_captureHotkey, Settings.Hotkey, "settings.hotkey", () => Home?.TriggerCapture(), errors);
        RegisterOne(_fileHotkey, Settings.FileTranslationHotkey, "settings.fileHotkey", () => Home?.OpenFileTranslation(), errors);
        RegisterOne(_audioHotkey, Settings.AudioTranslationHotkey, "settings.audioHotkey", () => Home?.OpenAudioTranslation(), errors);
        RegisterOne(_settingsHotkey, Settings.SettingsHotkey, "settings.settingsHotkey", () => Home?.OpenSettings(), errors);
        RegisterOne(_selectionHotkey, Settings.SelectionTranslationHotkey, "settings.selectionHotkey", () => Home?.TriggerSelectionTranslation(), errors);
        return errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
    }

    private static void RegisterOne(HotkeyManager manager, string? hotkeyText, string labelKey, Action callback, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            manager.UnregisterCurrent();
            return;
        }

        if (manager.Register(hotkeyText, callback, out var error)) return;

        var label = LocalizationService.Instance.T(labelKey);
        errors.Add(LocalizationService.Instance.Format("settings.hotkeyRegisterFailed", label, hotkeyText, error));
    }

    public string? ApplySettings(AppSettings settings)
    {
        settings.UiLanguage = LocalizationService.Normalize(settings.UiLanguage);
        Settings = settings;
        LocalizationService.Instance.SetLanguage(settings.UiLanguage);
        settings.Save();
        var error = RegisterHotkeys();
        SelectionPopup.UpdateEnabled(settings.SelectionPopupEnabled);
        SettingsChanged?.Invoke();
        _ = RefreshLanguagesAsync();
        return error;
    }

    public async Task RefreshLanguagesAsync()
    {
        try
        {
            var languages = await Translation.GetLanguagesAsync(Settings, CancellationToken.None);
            if (languages.Count == 0) return;
            Languages = languages;
            LanguagesChanged?.Invoke();
        }
        catch
        {
        }
    }

    public string GetLanguageName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        if (code.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return LocalizationService.Instance.T("language.auto");

        var match = Languages.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        return match?.Name ?? code;
    }

    public void ExitApp()
    {
        Home?.AllowClose();
        Home?.Close();
        Tray?.Dispose();
        Tray = null;
        SelectionPopup.Dispose();
        DisposeHotkeys();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Health.Dispose();
        Tray?.Dispose();
        SelectionPopup.Dispose();
        DisposeHotkeys();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void DisposeHotkeys()
    {
        _captureHotkey.Dispose();
        _fileHotkey.Dispose();
        _audioHotkey.Dispose();
        _settingsHotkey.Dispose();
        _selectionHotkey.Dispose();
    }
}
