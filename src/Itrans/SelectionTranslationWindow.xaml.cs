using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Itrans.Localization;
using Itrans.Services;

namespace Itrans;

public partial class SelectionTranslationWindow : Window
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private CancellationTokenSource? _cts;
    private string _lastTranslatedText = "";
    private string _statusKey = "selection.noText";
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _directionKey = "main.directionAuto";
    private object?[] _directionArgs = Array.Empty<object?>();
    private string? _directionSource;
    private string? _directionTarget;

    public SelectionTranslationWindow(string? text)
    {
        InitializeComponent();
        OriginalBox.Text = text ?? "";
        App.Instance.Health.StateChanged += UpdateServiceDot;
        Loc.LanguageChanged += OnLanguageChanged;
        App.Instance.SettingsChanged += ApplyContentFont;
        ApplyContentFont();
        UpdateServiceDot();
        SetDirection("main.directionAuto");
        SetStatus(string.IsNullOrWhiteSpace(OriginalBox.Text) ? "selection.noText" : "main.statusTranslating");
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public void SetText(string? text)
    {
        _cts?.Cancel();
        OriginalBox.Text = text ?? "";
        TranslationBox.Clear();
        _lastTranslatedText = "";
        SetDirection("main.directionAuto");

        if (string.IsNullOrWhiteSpace(OriginalBox.Text))
        {
            SetStatus("selection.noText");
            return;
        }

        if (IsLoaded) _ = TranslateAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Instance.Health.Check();
        if (string.IsNullOrWhiteSpace(OriginalBox.Text))
        {
            SetStatus("selection.noText");
            return;
        }

        _ = TranslateAsync();
    }

    private async Task TranslateAsync()
    {
        var text = OriginalBox.Text.Trim();
        if (text.Length == 0)
        {
            _lastTranslatedText = "";
            TranslationBox.Clear();
            SetDirection("main.directionAuto");
            SetStatus("selection.noText");
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;

        SetDirection("main.directionTranslating");
        SetStatus("main.statusTranslating");
        TranslationBox.Clear();

        try
        {
            var result = await App.Instance.Translation.TranslateAsync(text, App.Instance.Settings, token);
            if (token.IsCancellationRequested) return;

            TranslationBox.Text = result.TranslatedText;
            _lastTranslatedText = text;
            SetDirectionResult(result.Source, result.Target);
            SetStatus("main.statusTranslated", DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            TranslationBox.Text = Loc.Format("main.statusTranslateFailed", ex.Message);
            SetDirection("main.directionTranslateFailed");
            SetStatus("main.statusTranslateFailed", ex.Message);
        }
    }

    private void ApplyContentFont()
    {
        try
        {
            var settings = App.Instance.Settings;
            var family = new FontFamily(settings.FontFamily);
            OriginalBox.FontFamily = family;
            TranslationBox.FontFamily = family;
            var size = Math.Clamp(settings.FontSize, 8, 72);
            OriginalBox.FontSize = size;
            TranslationBox.FontSize = size;
        }
        catch
        {
        }
    }

    private void Retranslate_Click(object sender, RoutedEventArgs e) => _ = TranslateAsync();

    private void OriginalBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var text = OriginalBox.Text.Trim();
        if (text.Length == 0 || string.Equals(text, _lastTranslatedText, StringComparison.Ordinal)) return;
        _ = TranslateAsync();
    }

    private void CopyOriginal_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(OriginalBox.Text, "main.nothingToCopySource", "main.copiedSource");

    private void CopyTranslation_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(TranslationBox.Text, "main.nothingToCopyTranslation", "main.copiedTranslation");

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

    private void OnLanguageChanged()
    {
        RefreshStatus();
        RefreshDirection();
        UpdateServiceDot();
    }

    private void UpdateServiceDot()
    {
        var health = App.Instance.Health;
        ServiceDot.Fill = ServiceHealthBrushes.For(health.State);
        ServiceDot.ToolTip = health.Message;
        System.Windows.Automation.AutomationProperties.SetName(ServiceDot, health.Message);
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        RefreshStatus();
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

    private void OnClosed(object? sender, EventArgs e)
    {
        App.Instance.Health.StateChanged -= UpdateServiceDot;
        Loc.LanguageChanged -= OnLanguageChanged;
        App.Instance.SettingsChanged -= ApplyContentFont;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
