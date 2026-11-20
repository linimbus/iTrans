using System;
using System.Threading;
using System.Windows;
using Itrans.Controls;
using Itrans.Interop;
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

    public HotkeyManager Hotkey { get; } = new();

    public TrayController? Tray { get; private set; }

    public MainWindow? Home { get; private set; }

    public event Action? SettingsChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var createdNew = true;
        _mutex = new Mutex(true, @"Local\Itrans.SingleInstance", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show("itrans 已在运行，请查看系统托盘图标。", "itrans", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Settings = AppSettings.Load();
        Home = new MainWindow();
        Tray = new TrayController(this);
        Home.Show();
        Health.Start();

        var error = RegisterHotkey();
        if (error != null)
        {
            Tray.ShowBalloon("快捷键注册失败", $"{Settings.Hotkey}：{error}。可在设置中更换快捷键。");
        }
    }

    public string? RegisterHotkey()
    {
        return Hotkey.Register(Settings.Hotkey, () => Home?.TriggerCapture(), out var error) ? null : error;
    }

    public string? ApplySettings(AppSettings settings)
    {
        Settings = settings;
        settings.Save();
        var error = RegisterHotkey();
        SettingsChanged?.Invoke();
        return error;
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
