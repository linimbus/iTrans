using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Itrans.Capture;
using Itrans.Services;

namespace Itrans;

public partial class MainWindow : Window
{
    private static readonly System.Windows.Media.Brush HealthyBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A));

    private static readonly System.Windows.Media.Brush UnhealthyBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26));

    private static readonly System.Windows.Media.Brush CheckingBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));

    private readonly HttpClient _healthHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _healthCts;
    private int _healthVersion;

    private bool _busy;
    private bool _allowClose;
    private bool _trayNoticeShown;
    private FileTranslationWindow? _fileWindow;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.Title;
        _healthTimer.Tick += (_, _) => RequestHealthCheck();
        _healthTimer.Start();
        Closed += OnClosedCleanupHealth;
        App.Instance.SettingsChanged += UpdateSettingsText;
        UpdateSettingsText();
        Closing += OnClosing;
        StatusText.Text = $"就绪 · 按 {App.Instance.Settings.Hotkey} 截图并翻译";
    }

    private void UpdateSettingsText()
    {
        var settings = App.Instance.Settings;
        CaptureButton.ToolTip = $"截图翻译（{settings.Hotkey}）";
        ApplyContentFont(settings);
        RequestHealthCheck(true);
    }

    private void RequestHealthCheck(bool showChecking = false)
    {
        var version = ++_healthVersion;
        _healthCts?.Cancel();
        _healthCts?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        _healthCts = cts;

        if (showChecking)
        {
            ServiceDot.Fill = CheckingBrush;
            ServiceDot.ToolTip = "正在检测翻译服务…";
        }

        _ = CheckServiceHealthAsync(version, cts.Token);
    }

    private async Task CheckServiceHealthAsync(int version, CancellationToken cancellationToken)
    {
        var baseUrl = (App.Instance.Settings.ServerUrl ?? "").Trim().TrimEnd('/');
        bool healthy;
        string message;

        try
        {
            if (baseUrl.Length == 0)
            {
                healthy = false;
                message = "未配置翻译服务地址";
            }
            else
            {
                using var response = await _healthHttp.GetAsync(baseUrl + "/health", cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                healthy = response.IsSuccessStatusCode && IsHealthyStatus(body);
                message = healthy ? "翻译服务正常" : $"翻译服务异常（HTTP {(int)response.StatusCode}）";
            }
        }
        catch (OperationCanceledException)
        {
            if (version != _healthVersion) return;
            healthy = false;
            message = "翻译服务连接超时";
        }
        catch (Exception ex)
        {
            if (version != _healthVersion) return;
            healthy = false;
            message = "翻译服务不可用：" + ex.Message;
        }

        if (version != _healthVersion) return;

        ServiceDot.Fill = healthy ? HealthyBrush : UnhealthyBrush;
        ServiceDot.ToolTip = message;
        System.Windows.Automation.AutomationProperties.SetName(ServiceDot, message);
    }

    private static bool IsHealthyStatus(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void OnClosedCleanupHealth(object? sender, EventArgs e)
    {
        _healthTimer.Stop();
        _healthCts?.Cancel();
        _healthCts?.Dispose();
        _healthHttp.Dispose();
    }

    private void ApplyContentFont(AppSettings settings)
    {
        try
        {
            var family = new System.Windows.Media.FontFamily(settings.FontFamily);
            OcrTextBox.FontFamily = family;
            TranslationTextBox.FontFamily = family;
        }
        catch
        {
        }

        var size = Math.Clamp(settings.FontSize, 8, 72);
        OcrTextBox.FontSize = size;
        TranslationTextBox.FontSize = size;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        if (!_trayNoticeShown)
        {
            _trayNoticeShown = true;
            App.Instance.Tray?.ShowBalloon("itrans 仍在运行", "已最小化到托盘，双击托盘图标或按快捷键可继续使用。");
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
        RequestHealthCheck();
    }

    public void TriggerCapture()
    {
        if (_busy) return;
        _ = RunCaptureAsync();
    }

    private async Task RunCaptureAsync()
    {
        _busy = true;
        CaptureButton.IsEnabled = false;
        var wasVisible = IsVisible;
        try
        {
            if (wasVisible && WindowState != WindowState.Minimized)
                WindowState = WindowState.Minimized;
            await Task.Delay(250);

            var bitmap = await ScreenCapturer.CaptureAsync();
            if (bitmap == null)
            {
                if (wasVisible) ShowMainWindow();
                SetStatus("已取消截图");
                return;
            }

            try
            {
                ShowMainWindow();
                await RecognizeAndTranslateAsync(bitmap);
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            ShowMainWindow();
            SetStatus("截图失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            CaptureButton.IsEnabled = true;
        }
    }

    private async Task RecognizeAndTranslateAsync(Bitmap bitmap)
    {
        OcrTextBox.Clear();
        TranslationTextBox.Clear();
        DirectionText.Text = "识别中…";
        SetStatus("正在用 Windows OCR 识别文字…");

        string text;
        try
        {
            text = await App.Instance.Ocr.RecognizeAsync(bitmap, GetOcrLanguageTags(App.Instance.Settings));
        }
        catch (Exception ex)
        {
            DirectionText.Text = "识别失败";
            SetStatus("文字识别失败：" + ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            DirectionText.Text = "未识别到文字";
            SetStatus("未识别到文字，请重新截图");
            return;
        }

        OcrTextBox.Text = text;
        await TranslateAsync();
    }

    private static List<string> GetOcrLanguageTags(AppSettings settings)
    {
        var tags = new List<string>();
        if (settings.OcrChineseSimplified) tags.Add("zh-Hans");
        if (settings.OcrChineseTraditional) tags.Add("zh-Hant");
        if (settings.OcrEnglish) tags.Add("en");
        if (tags.Count == 0) tags.Add("zh-Hans");
        return tags;
    }

    private async Task TranslateAsync()
    {
        var text = OcrTextBox.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("没有可翻译的文字，请先截图识别");
            return;
        }

        DirectionText.Text = "翻译中…";
        SetStatus("正在请求 LibreTranslate…");
        TranslationTextBox.Clear();

        try
        {
            var result = await App.Instance.Translation.TranslateAsync(text, App.Instance.Settings, CancellationToken.None);
            TranslationTextBox.Text = result.TranslatedText;
            DirectionText.Text = result.ChineseSource ? "中文 → English" : "English → 中文";
            SetStatus($"翻译完成 · {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            TranslationTextBox.Text = "翻译失败：" + ex.Message;
            DirectionText.Text = "翻译失败";
            SetStatus("翻译失败：" + ex.Message);
        }
    }

    private void Capture_Click(object sender, RoutedEventArgs e) => TriggerCapture();

    private async void Retranslate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        CaptureButton.IsEnabled = false;
        try
        {
            await TranslateAsync();
        }
        finally
        {
            _busy = false;
            CaptureButton.IsEnabled = true;
        }
    }

    private void CopyOcr_Click(object sender, RoutedEventArgs e) => CopyToClipboard(OcrTextBox.Text, "原文");

    private void CopyTranslation_Click(object sender, RoutedEventArgs e) => CopyToClipboard(TranslationTextBox.Text, "译文");

    private void CopyToClipboard(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus($"没有可复制的{name}");
            return;
        }
        try
        {
            Clipboard.SetText(text);
            SetStatus($"已复制{name}");
        }
        catch (Exception ex)
        {
            SetStatus("复制失败：" + ex.Message);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void FileTranslate_Click(object sender, RoutedEventArgs e) => OpenFileTranslation();

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
        var dialog = new SettingsWindow(App.Instance.Settings.Clone());
        if (IsVisible) dialog.Owner = this;
        if (dialog.ShowDialog() != true) return;

        var error = App.Instance.ApplySettings(dialog.Result);
        UpdateSettingsText();
        if (error != null)
        {
            SetStatus("设置已保存，但快捷键注册失败：" + error);
            App.Instance.Tray?.ShowBalloon("快捷键注册失败", error);
        }
        else
        {
            SetStatus("设置已保存");
            App.Instance.Tray?.ShowBalloon("设置已保存", $"截图快捷键：{App.Instance.Settings.Hotkey}");
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
