using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Itrans.Capture;
using Itrans.Localization;
using Itrans.Services;

namespace Itrans;

public partial class ScreenshotTranslationWindow : Window
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private bool _busy;
    private string _lastTranslatedText = "";
    private string _statusKey = "main.statusReady";
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _directionKey = "main.directionAuto";
    private object?[] _directionArgs = Array.Empty<object?>();
    private string? _directionSource;
    private string? _directionTarget;

    public ScreenshotTranslationWindow()
    {
        InitializeComponent();
        App.Instance.Health.StateChanged += UpdateServiceDot;
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += OnClosedCleanup;
        App.Instance.SettingsChanged += UpdateSettingsText;
        UpdateSettingsText();
        UpdateServiceDot();
        SetStatus("main.statusReady", App.Instance.Settings.Hotkey);
        SetDirection("main.directionAuto");
    }

    public void StartCapture()
    {
        if (_busy) return;
        _ = RunCaptureAsync();
    }

    private void UpdateSettingsText()
    {
        var settings = App.Instance.Settings;
        CaptureButton.ToolTip = Loc.Format("main.captureTooltip", settings.Hotkey);
        ApplyContentFont(settings);
        App.Instance.Health.Check(true);
    }

    private void OnLanguageChanged()
    {
        CaptureButton.ToolTip = Loc.Format("main.captureTooltip", App.Instance.Settings.Hotkey);
        RefreshStatus();
        RefreshDirection();
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

    public void ShowScreenshotWindow()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        App.Instance.Health.Check();
    }

    private async Task RunCaptureAsync()
    {
        _busy = true;
        CaptureButton.IsEnabled = false;
        var wasVisible = IsVisible;
        var home = App.Instance.Home;
        var homeWasVisible = home?.IsVisible == true;

        try
        {
            if (wasVisible) Hide();
            if (homeWasVisible) home!.Hide();

            await Task.Delay(250);

            var bitmap = await ScreenCapturer.CaptureAsync();
            if (bitmap == null)
            {
                if (wasVisible) ShowScreenshotWindow();
                else if (homeWasVisible) home!.ShowMainWindow();
                SetStatus("main.statusCaptureCancelled");
                return;
            }

            try
            {
                ShowScreenshotWindow();
                await RecognizeAndTranslateAsync(bitmap);
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            ShowScreenshotWindow();
            SetStatus("main.statusCaptureFailed", ex.Message);
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
        _lastTranslatedText = "";
        TranslationTextBox.Clear();
        SetDirection("main.directionRecognizing");
        SetStatus("main.statusRecognizing");

        string text;
        try
        {
            text = await App.Instance.Ocr.RecognizeAsync(bitmap, GetOcrLanguageTags(App.Instance.Settings));
        }
        catch (Exception ex)
        {
            SetDirection("main.directionRecognizeFailed");
            SetStatus("main.statusRecognizeFailed", ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            SetDirection("main.directionNoText");
            SetStatus("main.statusNoTextRecognized");
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
            _lastTranslatedText = "";
            SetStatus("main.noTextToTranslate");
            return;
        }

        SetDirection("main.directionTranslating");
        SetStatus("main.statusTranslating");
        TranslationTextBox.Clear();

        try
        {
            var result = await App.Instance.Translation.TranslateAsync(text, App.Instance.Settings, CancellationToken.None);
            TranslationTextBox.Text = result.TranslatedText;
            _lastTranslatedText = text;
            SetDirectionResult(result.Source, result.Target);
            SetStatus("main.statusTranslated", DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            TranslationTextBox.Text = Loc.Format("main.statusTranslateFailed", ex.Message);
            SetDirection("main.directionTranslateFailed");
            SetStatus("main.statusTranslateFailed", ex.Message);
        }
    }

    private void Capture_Click(object sender, RoutedEventArgs e) => StartCapture();

    private async void OcrTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var text = OcrTextBox.Text.Trim();
        if (_busy || text.Length == 0 || string.Equals(text, _lastTranslatedText, StringComparison.Ordinal)) return;

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

    private void CopyOcr_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(OcrTextBox.Text, "main.nothingToCopySource", "main.copiedSource");

    private void CopyTranslation_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(TranslationTextBox.Text, "main.nothingToCopyTranslation", "main.copiedTranslation");

    private void CopyToClipboard(string text, string emptyKey, string copiedKey)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus(emptyKey);
            return;
        }

        try
        {
            Clipboard.SetText(text);
            SetStatus(copiedKey);
        }
        catch (Exception ex)
        {
            SetStatus("main.copyFailed", ex.Message);
        }
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText.Text = Loc.Format(key, args);
    }

    private void RefreshStatus() => StatusText.Text = Loc.Format(_statusKey, _statusArgs);

    private void SetDirection(string key, params object?[] args)
    {
        _directionKey = key;
        _directionArgs = args;
        _directionSource = null;
        _directionTarget = null;
        DirectionText.Text = Loc.Format(key, args);
    }

    private void SetDirectionResult(string source, string target)
    {
        _directionSource = source;
        _directionTarget = target;
        DirectionText.Text = FormatDirection(source, target);
    }

    private string FormatDirection(string source, string target)
        => Loc.Format("main.directionFormat", App.Instance.GetLanguageName(source), App.Instance.GetLanguageName(target));

    private void RefreshDirection()
    {
        if (_directionSource != null && _directionTarget != null)
        {
            DirectionText.Text = FormatDirection(_directionSource, _directionTarget);
            return;
        }

        DirectionText.Text = Loc.Format(_directionKey, _directionArgs);
    }
}
