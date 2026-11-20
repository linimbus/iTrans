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

    public HotkeyManager Hotkey { get; } = new();

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
    }

    public string? RegisterHotkey()
    {
        return Hotkey.Register(Settings.Hotkey, () => Home?.TriggerCapture(), out var error) ? null : error;
    }

    public string? ApplySettings(AppSettings settings)
    {
        settings.UiLanguage = LocalizationService.Normalize(settings.UiLanguage);
        Settings = settings;
        LocalizationService.Instance.SetLanguage(settings.UiLanguage);
        settings.Save();
        var error = RegisterHotkey();
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
        Hotkey.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Health.Dispose();
        Tray?.Dispose();
        Hotkey.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
