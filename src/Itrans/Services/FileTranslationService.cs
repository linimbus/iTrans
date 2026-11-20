using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Itrans.Localization;

namespace Itrans.Services;

public sealed record TranslatedFile(byte[] Content, string FileName);

public sealed class FileTranslationService
{
    // Reference: LibreTranslate /frontend/settings -> supportedFilesFormat
    public static readonly string[] DefaultSupportedExtensions =
    {
        ".txt", ".odt", ".odp", ".docx", ".pptx", ".epub", ".html", ".srt", ".pdf",
    };

    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(string serverUrl, CancellationToken cancellationToken)
    {
        var baseUrl = (serverUrl ?? "").Trim().TrimEnd('/');
        if (baseUrl.Length == 0) return DefaultSupportedExtensions;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await _http.GetAsync(baseUrl + "/frontend/settings", timeout.Token);
            if (!response.IsSuccessStatusCode) return DefaultSupportedExtensions;

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("supportedFilesFormat", out var formats)
                && formats.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in formats.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var extension = item.GetString()?.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(extension)) continue;
                    if (!extension.StartsWith('.')) extension = "." + extension;
                    if (!list.Contains(extension)) list.Add(extension);
                }

                if (list.Count > 0) return list;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        return DefaultSupportedExtensions;
    }

    public async Task<TranslatedFile> TranslateFileAsync(string filePath, string source, string target, AppSettings settings, CancellationToken cancellationToken)
    {
        var baseUrl = (settings.ServerUrl ?? "").Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            throw new InvalidOperationException(LocalizationService.Instance.T("error.serverUrlRequired"));

        var timeoutSeconds = settings.FileTranslationTimeoutSeconds > 0
            ? Math.Clamp(settings.FileTranslationTimeoutSeconds, 30, 3600)
            : 600;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return await TranslateFileCoreAsync(baseUrl, filePath, source, target, settings, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(LocalizationService.Instance.Format("error.fileTimeout", timeoutSeconds));
        }
    }

    private async Task<TranslatedFile> TranslateFileCoreAsync(string baseUrl, string filePath, string source, string target, AppSettings settings, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(File.OpenRead(filePath));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
        form.Add(fileContent, "file", Path.GetFileName(filePath));
        form.Add(new StringContent(source), "source");
        form.Add(new StringContent(target), "target");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            form.Add(new StringContent(settings.ApiKey), "api_key");

        using var response = await _http.PostAsync(baseUrl + "/translate_file", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(LocalizationService.Instance.Format("error.fileHttp", (int)response.StatusCode, ExtractError(errorBody)));
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return await DownloadTranslatedFileAsync(baseUrl, response, filePath, target, cancellationToken);

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new TranslatedFile(content, GetDownloadFileName(response, filePath, target));
    }

    private async Task<TranslatedFile> DownloadTranslatedFileAsync(string baseUrl, HttpResponseMessage response, string originalPath, string target, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? url = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("translatedFileUrl", out var value) && value.ValueKind == JsonValueKind.String)
                    url = value.GetString();
                if (string.IsNullOrEmpty(url)
                    && document.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.String)
                {
                    throw new InvalidOperationException(LocalizationService.Instance.Format("error.fileFailed", error.GetString()));
                }
            }
        }
        catch (JsonException)
        {
        }

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(LocalizationService.Instance.T("error.fileNoUrl"));

        using var download = await _http.GetAsync(ResolveDownloadUri(baseUrl, url!), cancellationToken);
        if (!download.IsSuccessStatusCode)
        {
            var errorBody = await download.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(LocalizationService.Instance.Format("error.fileDownload", (int)download.StatusCode, ExtractError(errorBody)));
        }

        var content = await download.Content.ReadAsByteArrayAsync(cancellationToken);
        return new TranslatedFile(content, GetDownloadFileName(download, originalPath, target));
    }

    private static Uri ResolveDownloadUri(string baseUrl, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            if (absolute.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || absolute.Host == "127.0.0.1"
                || absolute.Host == "::1")
            {
                return new Uri(new Uri(baseUrl + "/"), absolute.PathAndQuery.TrimStart('/'));
            }
            return absolute;
        }

        return new Uri(new Uri(baseUrl + "/"), url.TrimStart('/'));
    }

    private static string GetDownloadFileName(HttpResponseMessage response, string originalPath, string target)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var serverName = (disposition?.FileNameStar ?? disposition?.FileName)?.Trim().Trim('"');

        var extension = Path.GetExtension(serverName);
        if (string.IsNullOrWhiteSpace(extension)) extension = Path.GetExtension(originalPath);

        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "translated";

        return SanitizeFileName($"{baseName}_{target}{extension}");
    }

    private static string SanitizeFileName(string name)
    {
        name = Path.GetFileName(name);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "translated" : name;
    }

    private static string GetContentType(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".srt" => "application/x-subrip",
            ".pdf" => "application/pdf",
            ".epub" => "application/epub+zip",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".odp" => "application/vnd.oasis.opendocument.presentation",
            _ => "application/octet-stream",
        };

    private static string ExtractError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? "";
            }
        }
        catch
        {
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }
}
