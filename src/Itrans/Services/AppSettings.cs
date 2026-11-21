using System;
using System.IO;
using System.Text.Json;

namespace Itrans.Services;

public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "http://192.168.3.5:5000";

    public string ApiKey { get; set; } = "";

    public string Hotkey { get; set; } = "Ctrl+Alt+Q";

    public string FileTranslationHotkey { get; set; } = "";

    public string AudioTranslationHotkey { get; set; } = "";

    public string SettingsHotkey { get; set; } = "";

    public string AudioListeningLanguage { get; set; } = "en";

    public bool OcrChineseSimplified { get; set; } = true;

    public bool OcrChineseTraditional { get; set; }

    public bool OcrEnglish { get; set; }

    public string TranslationMode { get; set; } = "auto";

    public string? TranslationSource { get; set; }

    public string? TranslationTarget { get; set; }

    public string UiLanguage { get; set; } = "";

    public bool AutoStart { get; set; }

    public string FontFamily { get; set; } = "Microsoft YaHei UI";

    public double FontSize { get; set; } = 12.5;

    public int FileTranslationTimeoutSeconds { get; set; } = 600;

    public string FileOutputDirectory { get; set; } = "";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Itrans");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (settings != null) return settings;
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, SerializerOptions));
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public string GetSourceLanguage()
    {
        if (!string.IsNullOrWhiteSpace(TranslationSource)) return TranslationSource!;
        return TranslationMode switch
        {
            "zh-en" => "zh",
            "en-zh" => "en",
            _ => "auto",
        };
    }

    public string GetTargetLanguage()
    {
        if (!string.IsNullOrWhiteSpace(TranslationTarget)) return TranslationTarget!;
        return TranslationMode switch
        {
            "zh-en" => "en",
            "en-zh" => "zh",
            _ => "auto",
        };
    }
}
