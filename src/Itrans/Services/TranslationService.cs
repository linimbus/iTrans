using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Itrans.Services;

public sealed record TranslationResult(string TranslatedText, bool ChineseSource);

public sealed class TranslationService
{
    private const int MaxChunkLength = 2500;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(75) };

    public async Task<TranslationResult> TranslateAsync(string text, AppSettings settings, CancellationToken cancellationToken)
    {
        var baseUrl = (settings.ServerUrl ?? "").Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            throw new InvalidOperationException("请先在设置中填写 LibreTranslate 服务地址。");

        var mode = settings.TranslationMode ?? "auto";
        bool chinese;
        string source;

        if (mode == "zh-en")
        {
            chinese = true;
            source = "zh";
        }
        else if (mode == "en-zh")
        {
            chinese = false;
            source = "en";
        }
        else
        {
            var detected = await DetectAsync(baseUrl, settings.ApiKey, text, cancellationToken);
            chinese = IsChinese(detected) || (detected.Length == 0 && DetectByText(text) == "zh");
            source = chinese ? "zh" : "en";
            if (!chinese && detected.Length > 0 && !detected.Equals("en", StringComparison.OrdinalIgnoreCase))
                source = "auto";
        }

        var target = chinese ? "en" : "zh";

        var builder = new StringBuilder();
        foreach (var chunk in SplitText(text, MaxChunkLength))
        {
            var translated = await TranslateChunkAsync(baseUrl, settings.ApiKey, chunk, source, target, cancellationToken);
            if (builder.Length > 0) builder.AppendLine();
            builder.Append(translated);
        }

        return new TranslationResult(builder.ToString(), chinese);
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
            throw new InvalidOperationException($"翻译服务返回 HTTP {(int)response.StatusCode}：{ExtractError(body)}");

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("translatedText", out var translated)
            && translated.ValueKind == JsonValueKind.String)
        {
            return translated.GetString() ?? "";
        }

        throw new InvalidOperationException("翻译服务响应格式不正确。");
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
