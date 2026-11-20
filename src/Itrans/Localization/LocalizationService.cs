using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Data;

namespace Itrans.Localization;

public sealed record UiLanguage(string Code, string NativeName);

public sealed class LocalizationService : INotifyPropertyChanged
{
    private const string FallbackCode = "en";

    public static LocalizationService Instance { get; } = new();

    public static readonly IReadOnlyList<UiLanguage> SupportedLanguages = new[]
    {
        new UiLanguage("zh-Hans", "简体中文"),
        new UiLanguage("zh-Hant", "繁體中文"),
        new UiLanguage("en", "English"),
        new UiLanguage("ja", "日本語"),
        new UiLanguage("de", "Deutsch"),
        new UiLanguage("fr", "Français"),
    };

    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
    private string _language = "zh-Hans";

    private LocalizationService()
    {
        _fallback = LoadResource(FallbackCode);
        _strings = LoadResource(_language);
    }

    public string Language => _language;

    public string this[string key] => T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? LanguageChanged;

    public string T(string key)
    {
        if (_strings.TryGetValue(key, out var value)) return value;
        if (_fallback.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    public string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(key), args);

    public void SetLanguage(string? code)
    {
        var normalized = Normalize(code);
        var changed = !string.Equals(_language, normalized, StringComparison.OrdinalIgnoreCase);
        _language = normalized;
        _strings = LoadResource(normalized);
        ApplyCulture(normalized);

        if (!changed) return;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            code = CultureInfo.CurrentUICulture.Name;

        var value = code.Trim().Replace('_', '-');
        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return value.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("-TW", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("-HK", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("-MO", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant"
                : "zh-Hans";
        }

        if (value.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (value.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return "de";
        if (value.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr";
        if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
        return "en";
    }

    private static void ApplyCulture(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch
        {
        }
    }

    private static Dictionary<string, string> LoadResource(string code)
    {
        try
        {
            var assembly = typeof(LocalizationService).Assembly;
            var suffix = $".Localization.{code}.json";
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(resource => resource.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (name == null) return new Dictionary<string, string>(StringComparer.Ordinal);

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) return new Dictionary<string, string>(StringComparer.Ordinal);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = reader.ReadToEnd();
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return values == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(values, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
