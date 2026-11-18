using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Itrans.Interop;
using Itrans.Services;

namespace Itrans;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public AppSettings Result => _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 48);
        _settings = settings;
        ServerUrlBox.Text = settings.ServerUrl;
        ApiKeyBox.Text = settings.ApiKey;
        HotkeyBox.Text = settings.Hotkey;

        OcrSimplifiedBox.IsChecked = settings.OcrChineseSimplified;
        OcrTraditionalBox.IsChecked = settings.OcrChineseTraditional;
        OcrEnglishBox.IsChecked = settings.OcrEnglish;

        TranslationModeBox.SelectedValue = settings.TranslationMode;
        if (TranslationModeBox.SelectedIndex < 0) TranslationModeBox.SelectedIndex = 0;

        AutoStartBox.IsChecked = AutoStartManager.IsEnabled();

        FontFamilyBox.ItemsSource = Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        FontFamilyBox.Text = settings.FontFamily;
        FontSizeBox.ItemsSource = new[] { "11", "12", "13", "13.5", "14", "15", "16", "18", "20", "22", "24", "28", "32" };
        FontSizeBox.Text = settings.FontSize.ToString("0.#", CultureInfo.InvariantCulture);

        Loaded += (_, _) => ServerUrlBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadServerUrl(out var url)) return;

        var hotkey = HotkeyBox.Text.Trim();
        if (!HotkeyManager.TryParse(hotkey, out _, out _, out var hotkeyError))
        {
            ShowMessage("快捷键无效：" + hotkeyError, Brushes.OrangeRed);
            return;
        }

        var fontFamily = FontFamilyBox.Text.Trim();
        if (fontFamily.Length == 0)
        {
            ShowMessage("字体名称不能为空", Brushes.OrangeRed);
            return;
        }

        if (!double.TryParse(FontSizeBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize)
            || double.IsNaN(fontSize) || fontSize < 8 || fontSize > 72)
        {
            ShowMessage("字号需为 8 到 72 之间的数字", Brushes.OrangeRed);
            return;
        }

        var simplified = OcrSimplifiedBox.IsChecked == true;
        var traditional = OcrTraditionalBox.IsChecked == true;
        var english = OcrEnglishBox.IsChecked == true;
        if (!simplified && !traditional && !english)
        {
            ShowMessage("请至少勾选一种 OCR 识别语种", Brushes.OrangeRed);
            return;
        }

        var autoStart = AutoStartBox.IsChecked == true;
        try
        {
            AutoStartManager.SetEnabled(autoStart);
        }
        catch (Exception ex)
        {
            ShowMessage("设置开机启动失败：" + ex.Message, Brushes.OrangeRed);
            return;
        }

        _settings.ServerUrl = url;
        _settings.ApiKey = ApiKeyBox.Text.Trim();
        _settings.Hotkey = hotkey;
        _settings.OcrChineseSimplified = simplified;
        _settings.OcrChineseTraditional = traditional;
        _settings.OcrEnglish = english;
        _settings.TranslationMode = TranslationModeBox.SelectedValue as string ?? "auto";
        _settings.AutoStart = autoStart;
        _settings.FontFamily = fontFamily;
        _settings.FontSize = fontSize;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadServerUrl(out var url)) return;

        TestButton.IsEnabled = false;
        ShowMessage("正在连接 " + url + " …", Brushes.Gray);
        try
        {
            using var response = await _http.GetAsync(url + "/languages");
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                ShowMessage($"连接失败：HTTP {(int)response.StatusCode}", Brushes.OrangeRed);
                return;
            }

            var codes = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                        {
                            codes.Add(code.GetString() ?? "");
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }

            var hasChinese = codes.Any(c => c.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            var hasEnglish = codes.Any(c => string.Equals(c, "en", StringComparison.OrdinalIgnoreCase));
            if (hasChinese && hasEnglish)
            {
                ShowMessage($"连接成功，已加载 {codes.Count} 种语言（含中文和英文）", Brushes.SeaGreen);
            }
            else if (codes.Count > 0)
            {
                ShowMessage("连接成功，但服务缺少中文或英文模型，请检查 LT_LOAD_ONLY 配置。", Brushes.DarkOrange);
            }
            else
            {
                ShowMessage("连接成功，但服务未返回语言列表。", Brushes.DarkOrange);
            }
        }
        catch (Exception ex)
        {
            ShowMessage("连接失败：" + ex.Message, Brushes.OrangeRed);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private bool TryReadServerUrl(out string url)
    {
        url = ServerUrlBox.Text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowMessage("服务地址必须是 http:// 或 https:// 开头的完整地址", Brushes.OrangeRed);
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
