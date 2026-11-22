using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Itrans.Interop;
using Itrans.Localization;
using Itrans.Services;

namespace Itrans;

public partial class SettingsWindow : Window
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly AppSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public AppSettings Result => _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 48);
        _settings = settings;

        UiLanguageBox.ItemsSource = LocalizationService.SupportedLanguages;
        UiLanguageBox.SelectedValue = LocalizationService.Normalize(settings.UiLanguage);
        if (UiLanguageBox.SelectedIndex < 0) UiLanguageBox.SelectedIndex = 0;

        ServerUrlBox.Text = settings.ServerUrl;
        ApiKeyBox.Text = settings.ApiKey;
        FileTimeoutBox.Text = settings.FileTranslationTimeoutSeconds.ToString();
        HotkeyBox.Text = settings.Hotkey;
        FileHotkeyBox.Text = settings.FileTranslationHotkey;
        AudioHotkeyBox.Text = settings.AudioTranslationHotkey;
        SettingsHotkeyBox.Text = settings.SettingsHotkey;
        SelectionHotkeyBox.Text = settings.SelectionTranslationHotkey;
        SelectionPopupBox.IsChecked = settings.SelectionPopupEnabled;

        OcrSimplifiedBox.IsChecked = settings.OcrChineseSimplified;
        OcrTraditionalBox.IsChecked = settings.OcrChineseTraditional;
        OcrEnglishBox.IsChecked = settings.OcrEnglish;

        PopulateLanguageBoxes(App.Instance.Languages);

        AutoStartBox.IsChecked = AutoStartManager.IsEnabled();

        FontFamilyBox.ItemsSource = Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        FontFamilyBox.Text = settings.FontFamily;
        FontSizeBox.ItemsSource = new[] { "11", "11.5", "12", "12.5", "13", "13.5", "14", "15", "16", "18", "20", "22", "24", "28", "32" };
        FontSizeBox.Text = settings.FontSize.ToString("0.#", CultureInfo.InvariantCulture);

        Loaded += async (_, _) =>
        {
            ServerUrlBox.Focus();
            await RefreshLanguagesAsync();
        };
    }

    private async Task RefreshLanguagesAsync()
    {
        var url = ServerUrlBox.Text.Trim().TrimEnd('/');
        if (url.Length == 0) return;

        var temp = new AppSettings { ServerUrl = url, ApiKey = ApiKeyBox.Text.Trim() };
        var languages = await App.Instance.Translation.GetLanguagesAsync(temp, CancellationToken.None);
        if (languages.Count > 0) PopulateLanguageBoxes(languages);
    }

    private void PopulateLanguageBoxes(IReadOnlyList<TranslationLanguage>? languages)
    {
        languages ??= App.Instance.Languages;
        var source = SourceLanguageBox.SelectedValue as string ?? _settings.GetSourceLanguage();
        var target = TargetLanguageBox.SelectedValue as string ?? _settings.GetTargetLanguage();

        var options = languages
            .Where(item => !item.Code.Equals("auto", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sourceItems = new List<TranslationLanguage>
        {
            new("auto", Loc.T("language.auto")),
        };
        sourceItems.AddRange(options);
        if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && options.All(item => !item.Code.Equals(source, StringComparison.OrdinalIgnoreCase)))
        {
            sourceItems.Add(new TranslationLanguage(source, source));
        }

        var targetItems = new List<TranslationLanguage>
        {
            new("auto", Loc.T("language.autoPair")),
        };
        targetItems.AddRange(options);
        if (!target.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && options.All(item => !item.Code.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            targetItems.Add(new TranslationLanguage(target, target));
        }

        SourceLanguageBox.ItemsSource = sourceItems;
        SourceLanguageBox.SelectedValue = source;
        if (SourceLanguageBox.SelectedIndex < 0) SourceLanguageBox.SelectedIndex = 0;

        TargetLanguageBox.ItemsSource = targetItems;
        TargetLanguageBox.SelectedValue = target;
        if (TargetLanguageBox.SelectedIndex < 0) TargetLanguageBox.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadServerUrl(out var url)) return;

        if (!TryReadHotkey(HotkeyBox.Text, "settings.hotkey", true, out var hotkey, out var captureModifiers, out var captureKey)) return;
        if (!TryReadHotkey(FileHotkeyBox.Text, "settings.fileHotkey", false, out var fileHotkey, out var fileModifiers, out var fileKey)) return;
        if (!TryReadHotkey(AudioHotkeyBox.Text, "settings.audioHotkey", false, out var audioHotkey, out var audioModifiers, out var audioKey)) return;
        if (!TryReadHotkey(SettingsHotkeyBox.Text, "settings.settingsHotkey", false, out var settingsHotkey, out var settingsModifiers, out var settingsKey)) return;
        if (!TryReadHotkey(SelectionHotkeyBox.Text, "settings.selectionHotkey", false, out var selectionHotkey, out var selectionModifiers, out var selectionKey)) return;

        var bindings = new List<(string LabelKey, string Text, uint Modifiers, uint Key)>
        {
            ("settings.hotkey", hotkey, captureModifiers, captureKey),
        };
        if (fileHotkey.Length > 0) bindings.Add(("settings.fileHotkey", fileHotkey, fileModifiers, fileKey));
        if (audioHotkey.Length > 0) bindings.Add(("settings.audioHotkey", audioHotkey, audioModifiers, audioKey));
        if (settingsHotkey.Length > 0) bindings.Add(("settings.settingsHotkey", settingsHotkey, settingsModifiers, settingsKey));
        if (selectionHotkey.Length > 0) bindings.Add(("settings.selectionHotkey", selectionHotkey, selectionModifiers, selectionKey));

        for (var i = 0; i < bindings.Count; i++)
        {
            for (var j = i + 1; j < bindings.Count; j++)
            {
                if (bindings[i].Modifiers != bindings[j].Modifiers || bindings[i].Key != bindings[j].Key) continue;
                ShowMessage(
                    Loc.Format("settings.duplicateHotkey", Loc.T(bindings[i].LabelKey), Loc.T(bindings[j].LabelKey)),
                    Brushes.OrangeRed);
                return;
            }
        }

        var fontFamily = FontFamilyBox.Text.Trim();
        if (fontFamily.Length == 0)
        {
            ShowMessage(Loc.T("settings.emptyFont"), Brushes.OrangeRed);
            return;
        }

        if (!double.TryParse(FontSizeBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize)
            || double.IsNaN(fontSize) || fontSize < 8 || fontSize > 72)
        {
            ShowMessage(Loc.T("settings.invalidFontSize"), Brushes.OrangeRed);
            return;
        }

        if (!int.TryParse(FileTimeoutBox.Text.Trim(), out var fileTimeout)
            || fileTimeout < 30 || fileTimeout > 3600)
        {
            ShowMessage(Loc.T("settings.invalidTimeout"), Brushes.OrangeRed);
            return;
        }

        var simplified = OcrSimplifiedBox.IsChecked == true;
        var traditional = OcrTraditionalBox.IsChecked == true;
        var english = OcrEnglishBox.IsChecked == true;
        if (!simplified && !traditional && !english)
        {
            ShowMessage(Loc.T("settings.ocrRequired"), Brushes.OrangeRed);
            return;
        }

        var source = SourceLanguageBox.SelectedValue as string ?? "auto";
        var target = TargetLanguageBox.SelectedValue as string ?? "auto";
        if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && source.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            ShowMessage(Loc.T("settings.sameLanguage"), Brushes.OrangeRed);
            return;
        }

        var autoStart = AutoStartBox.IsChecked == true;
        try
        {
            AutoStartManager.SetEnabled(autoStart);
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.Format("settings.autoStartFailed", ex.Message), Brushes.OrangeRed);
            return;
        }

        _settings.UiLanguage = UiLanguageBox.SelectedValue as string ?? LocalizationService.Normalize(_settings.UiLanguage);
        _settings.ServerUrl = url;
        _settings.ApiKey = ApiKeyBox.Text.Trim();
        _settings.Hotkey = hotkey;
        _settings.FileTranslationHotkey = fileHotkey;
        _settings.AudioTranslationHotkey = audioHotkey;
        _settings.SettingsHotkey = settingsHotkey;
        _settings.SelectionTranslationHotkey = selectionHotkey;
        _settings.SelectionPopupEnabled = SelectionPopupBox.IsChecked == true;
        _settings.OcrChineseSimplified = simplified;
        _settings.OcrChineseTraditional = traditional;
        _settings.OcrEnglish = english;
        _settings.TranslationSource = source;
        _settings.TranslationTarget = target;
        _settings.AutoStart = autoStart;
        _settings.FontFamily = fontFamily;
        _settings.FontSize = fontSize;
        _settings.FileTranslationTimeoutSeconds = fileTimeout;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadServerUrl(out var url)) return;

        TestButton.IsEnabled = false;
        ShowMessage(Loc.Format("settings.testConnecting", url), Brushes.Gray);
        try
        {
            using var response = await _http.GetAsync(url + "/languages");
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                ShowMessage(Loc.Format("settings.testFailedHttp", (int)response.StatusCode), Brushes.OrangeRed);
                return;
            }

            var languages = TranslationService.ParseLanguages(body);
            if (languages.Count > 0) PopulateLanguageBoxes(languages);

            var hasChinese = languages.Any(item => item.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            var hasEnglish = languages.Any(item => item.Code.Equals("en", StringComparison.OrdinalIgnoreCase));
            if (hasChinese && hasEnglish)
            {
                ShowMessage(Loc.Format("settings.testSuccess", languages.Count), Brushes.SeaGreen);
            }
            else if (languages.Count > 0)
            {
                ShowMessage(Loc.T("settings.testMissingModels"), Brushes.DarkOrange);
            }
            else
            {
                ShowMessage(Loc.T("settings.testNoLanguages"), Brushes.DarkOrange);
            }
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.Format("settings.testFailed", ex.Message), Brushes.OrangeRed);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private bool TryReadHotkey(string text, string labelKey, bool required, out string value, out uint modifiers, out uint virtualKey)
    {
        value = text.Trim();
        modifiers = 0;
        virtualKey = 0;

        if (value.Length == 0)
        {
            if (!required) return true;
            ShowMessage(
                Loc.Format("settings.invalidHotkeyLabel", Loc.T(labelKey), Loc.T("error.hotkeyEmpty")),
                Brushes.OrangeRed);
            return false;
        }

        if (!HotkeyManager.TryParse(value, out modifiers, out virtualKey, out var error))
        {
            ShowMessage(
                Loc.Format("settings.invalidHotkeyLabel", Loc.T(labelKey), error),
                Brushes.OrangeRed);
            return false;
        }

        return true;
    }

    private bool TryReadServerUrl(out string url)
    {
        url = ServerUrlBox.Text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowMessage(Loc.T("settings.invalidServerUrl"), Brushes.OrangeRed);
            return false;
        }
        return true;
    }

    private void ShowMessage(string message, Brush brush)
    {
        MessageText.Text = message;
        MessageText.Foreground = brush;
    }
}
