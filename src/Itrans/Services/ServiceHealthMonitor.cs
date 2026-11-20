using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Itrans.Localization;

namespace Itrans.Services;

public enum ServiceHealthState
{
    Unknown,
    Checking,
    Healthy,
    Unhealthy,
}

public static class ServiceHealthBrushes
{
    private static readonly Brush HealthyBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly Brush UnhealthyBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly Brush CheckingBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

    public static Brush For(ServiceHealthState state) => state switch
    {
        ServiceHealthState.Healthy => HealthyBrush,
        ServiceHealthState.Unhealthy => UnhealthyBrush,
        _ => CheckingBrush,
    };
}

public sealed class ServiceHealthMonitor : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _cts;
    private int _version;
    private bool _disposed;
    private string _messageKey = "service.checking";
    private object?[] _messageArgs = Array.Empty<object?>();

    public ServiceHealthState State { get; private set; } = ServiceHealthState.Unknown;

    public string Message => LocalizationService.Instance.Format(_messageKey, _messageArgs);

    public event Action? StateChanged;

    public ServiceHealthMonitor()
    {
        _timer.Tick += (_, _) => Check();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged() => StateChanged?.Invoke();

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        Check(true);
    }

    public void Check(bool showChecking = false)
    {
        if (_disposed) return;

        var version = ++_version;
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        _cts = cts;

        if (showChecking)
        {
            Update(ServiceHealthState.Checking, "service.checking");
        }

        _ = CheckAsync(version, cts.Token);
    }

    private async Task CheckAsync(int version, CancellationToken cancellationToken)
    {
        var baseUrl = (App.Instance.Settings.ServerUrl ?? "").Trim().TrimEnd('/');
        ServiceHealthState state;
        string messageKey;
        object?[] messageArgs;

        try
        {
            if (baseUrl.Length == 0)
            {
                state = ServiceHealthState.Unhealthy;
                messageKey = "service.notConfigured";
                messageArgs = Array.Empty<object?>();
            }
            else
            {
                using var response = await _http.GetAsync(baseUrl + "/health", cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var healthy = response.IsSuccessStatusCode && IsHealthyStatus(body);
                state = healthy ? ServiceHealthState.Healthy : ServiceHealthState.Unhealthy;
                messageKey = healthy ? "service.healthy" : "service.abnormalHttp";
                messageArgs = healthy ? Array.Empty<object?>() : new object?[] { (int)response.StatusCode };
            }
        }
        catch (OperationCanceledException)
        {
            if (version != _version) return;
            state = ServiceHealthState.Unhealthy;
            messageKey = "service.timeout";
            messageArgs = Array.Empty<object?>();
        }
        catch (Exception ex)
        {
            if (version != _version) return;
            state = ServiceHealthState.Unhealthy;
            messageKey = "service.unavailable";
            messageArgs = new object?[] { ex.Message };
        }

        if (version != _version) return;
        Update(state, messageKey, messageArgs);
    }

    private void Update(ServiceHealthState state, string messageKey, params object?[] messageArgs)
    {
        State = state;
        _messageKey = messageKey;
        _messageArgs = messageArgs;
        StateChanged?.Invoke();
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _http.Dispose();
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
}
