using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Itrans.Localization;

namespace Itrans.Services;

public sealed record TranslationLanguage(string Code, string Name);

public sealed record TranslationResult(string TranslatedText, string Source, string Target);

public sealed class TranslationService
{
    private const int MaxChunkLength = 2500;

    public static readonly IReadOnlyList<TranslationLanguage> DefaultLanguages = new[]
    {
        new TranslationLanguage("zh", "Chinese"),
        new TranslationLanguage("en", "English"),
        new TranslationLanguage("ja", "Japanese"),
        new TranslationLanguage("ko", "Korean"),
        new TranslationLanguage("de", "German"),
        new TranslationLanguage("fr", "French"),
        new TranslationLanguage("es", "Spanish"),
        new TranslationLanguage("ru", "Russian"),
        new TranslationLanguage("pt", "Portuguese"),
        new TranslationLanguage("it", "Italian"),
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(75) };

    public async Task<IReadOnlyList<TranslationLanguage>> GetLanguagesAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var baseUrl = (settings.ServerUrl ?? "").Trim().TrimEnd('/');
        if (baseUrl.Length == 0) return Array.Empty<TranslationLanguage>();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await _http.GetAsync(baseUrl + "/languages", timeout.Token);
            if (!response.IsSuccessStatusCode) return Array.Empty<TranslationLanguage>();

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseLanguages(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<TranslationLanguage>();
        }
    }

    public static IReadOnlyList<TranslationLanguage> ParseLanguages(string body)
    {
        var list = new List<TranslationLanguage>();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return list;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                if (!element.TryGetProperty("code", out var codeElement) || codeElement.ValueKind != JsonValueKind.String)
                    continue;

                var code = codeElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(code)) continue;

                var name = code;
                if (element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                    name = nameElement.GetString()?.Trim() is { Length: > 0 } value ? value : code;

                list.Add(new TranslationLanguage(code, name));
            }
        }
        catch (JsonException)
        {
            return list;
        }

        return list
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<TranslationResult> TranslateAsync(string text, AppSettings settings, CancellationToken cancellationToken)
    {
        var baseUrl = (settings.ServerUrl ?? "").Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            throw new InvalidOperationException(LocalizationService.Instance.T("error.serverUrlRequired"));

        var source = settings.GetSourceLanguage();
        var target = settings.GetTargetLanguage();
        var actualSource = source;
        var actualTarget = target;

        if (string.Equals(target, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var detected = await DetectAsync(baseUrl, settings.ApiKey, text, cancellationToken);
            var chinese = IsChinese(detected) || (detected.Length == 0 && DetectByText(text) == "zh");
            actualTarget = chinese ? "en" : "zh";
            if (string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase))
                actualSource = chinese ? "zh" : "auto";
        }

        var builder = new StringBuilder();
        foreach (var chunk in SplitText(text, MaxChunkLength))
        {
            var translated = await TranslateChunkAsync(baseUrl, settings.ApiKey, chunk, actualSource, actualTarget, cancellationToken);
            if (builder.Length > 0) builder.AppendLine();
            builder.Append(translated);
        }

        return new TranslationResult(builder.ToString(), actualSource, actualTarget);
    }

    private async Task<string> TranslateChunkAsync(string baseUrl, string? apiKey, string text, string source, string target, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["q"] = text,
            ["source"] = source,
            ["target"] = target,
            ["format"] = "text",
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) form["api_key"] = apiKey!;

        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(baseUrl + "/translate", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(LocalizationService.Instance.Format("error.translateHttp", (int)response.StatusCode, ExtractError(body)));

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("translatedText", out var translated)
            && translated.ValueKind == JsonValueKind.String)
        {
            return translated.GetString() ?? "";
        }

        throw new InvalidOperationException(LocalizationService.Instance.T("error.translateBadResponse"));
    }

    private async Task<string> DetectAsync(string baseUrl, string? apiKey, string text, CancellationToken cancellationToken)
    {
        try
        {
            var form = new Dictionary<string, string> { ["q"] = text };
            if (!string.IsNullOrWhiteSpace(apiKey)) form["api_key"] = apiKey!;

            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(baseUrl + "/detect", content, cancellationToken);
            if (!response.IsSuccessStatusCode) return "";

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0)
            {
                var first = document.RootElement[0];
                if (first.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String)
                    return language.GetString() ?? "";
            }
            return "";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return "";
        }
    }

    public static bool IsChinese(string? language)
        => !string.IsNullOrEmpty(language) && language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static string DetectByText(string text)
    {
        int cjk = 0, latin = 0;
        foreach (var c in text)
        {
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF)) cjk++;
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
        }
        return cjk >= 1 && cjk * 2 >= latin ? "zh" : "en";
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString() ?? body;
        }
        catch
        {
        }
        var trimmed = body.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }

    private static IEnumerable<string> SplitText(string text, int maxLength)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new StringBuilder();
        foreach (var line in normalized.Split('\n'))
        {
            if (line.Length > maxLength)
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }
                foreach (var piece in SplitLongLine(line, maxLength)) yield return piece;
                continue;
            }

            if (builder.Length > 0 && builder.Length + line.Length + 1 > maxLength)
            {
                yield return builder.ToString();
                builder.Clear();
            }

            if (builder.Length > 0) builder.Append('\n');
            builder.Append(line);
        }

        if (builder.Length > 0) yield return builder.ToString();
    }

    private static IEnumerable<string> SplitLongLine(string line, int maxLength)
    {
        var start = 0;
        while (start < line.Length)
        {
            var length = Math.Min(maxLength, line.Length - start);
            if (start + length < line.Length)
            {
                var cut = -1;
                for (var i = start + length - 1; i > start + length - 200 && i > start; i--)
                {
                    var c = line[i];
                    if (c == '。' || c == '！' || c == '？' || c == '.' || c == '!' || c == '?'
                        || c == '；' || c == ';' || c == '，' || c == ',')
                    {
                        cut = i + 1;
                        break;
                    }
                }
                if (cut > start) length = cut - start;
            }
            yield return line.Substring(start, length);
            start += length;
        }
    }
}
