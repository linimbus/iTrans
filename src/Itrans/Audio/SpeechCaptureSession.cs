using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;
using Itrans.Localization;
using Itrans.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Itrans.Audio;

public sealed class SpeechCaptureSession : IDisposable
{
    private readonly object _gate = new();
    private WasapiRecorder? _recorder;
    private PcmBufferStream? _pcmStream;
    private SpeechRecognitionEngine? _recognizer;
    private CancellationTokenSource? _keepAliveCts;
    private long _lastAudioTicks;
    private bool _stopping;

    public event Action<string>? Recognized;

    public event Action? SpeechDetected;

    public string? RecognizerCulture { get; private set; }

    public string? RequestedCulture { get; private set; }

    public bool UsedRecognizerFallback { get; private set; }

    public async Task StartAsync(AudioSourceKind kind, uint processId, string? deviceId, string listeningLanguage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _stopping = false;
        }

        RecognizerCulture = null;
        RequestedCulture = null;
        UsedRecognizerFallback = false;

        var builder = new WasapiRecorderBuilder()
            .WithSharedMode()
            .WithEventSync()
            .WithFormat(new WaveFormat(16000, 16, 1));

        MMDevice? microphone = null;
        try
        {
            switch (kind)
            {
                case AudioSourceKind.System:
                    builder.WithLoopbackCapture();
                    break;
                case AudioSourceKind.Application:
                    if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                    {
                        throw new InvalidOperationException(
                            LocalizationService.Instance.T("audio.statusProcessLoopbackUnsupported"));
                    }
                    builder.WithProcessLoopback(processId, ProcessLoopbackMode.IncludeTargetProcessTree);
                    break;
                case AudioSourceKind.Microphone:
                    microphone = AudioDeviceCatalog.GetMicrophone(deviceId ?? "");
                    if (microphone == null)
                        throw new InvalidOperationException(LocalizationService.Instance.T("audio.statusNoSource"));
                    builder.WithDevice(microphone);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            _recorder = await builder.BuildAsync();
        }
        finally
        {
            microphone?.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var recognizerInfo = SelectRecognizer(listeningLanguage);
        if (recognizerInfo == null)
            throw new InvalidOperationException(LocalizationService.Instance.T("audio.statusNoRecognizer"));

        RecognizerCulture = recognizerInfo.Culture.Name;
        _pcmStream = new PcmBufferStream();
        _recognizer = new SpeechRecognitionEngine(recognizerInfo);
        _recognizer.InitialSilenceTimeout = TimeSpan.Zero;
        _recognizer.BabbleTimeout = TimeSpan.Zero;
        _recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(600);
        _recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1000);
        _recognizer.LoadGrammar(new DictationGrammar());
        _recognizer.SpeechRecognized += OnSpeechRecognized;
        _recognizer.SpeechDetected += OnSpeechDetected;
        _recognizer.SetInputToAudioStream(
            _pcmStream,
            new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));

        _recorder.DataAvailable += OnDataAvailable;

        try
        {
            _recorder.StartRecording();
            _recognizer.RecognizeAsync(RecognizeMode.Multiple);
            Interlocked.Exchange(ref _lastAudioTicks, Environment.TickCount64);
            var keepAliveCts = new CancellationTokenSource();
            _keepAliveCts = keepAliveCts;
            _ = Task.Run(() => KeepAliveAsync(keepAliveCts.Token));
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private async Task KeepAliveAsync(CancellationToken cancellationToken)
    {
        var silence = new byte[3200];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
                var elapsed = Environment.TickCount64 - Interlocked.Read(ref _lastAudioTicks);
                if (elapsed >= 100) _pcmStream?.Enqueue(silence);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
        }

        try
        {
            if (_recorder != null)
            {
                _recorder.DataAvailable -= OnDataAvailable;
                _recorder.StopRecording();
            }
        }
        catch
        {
        }

        try
        {
            _keepAliveCts?.Cancel();
        }
        catch
        {
        }

        _keepAliveCts?.Dispose();
        _keepAliveCts = null;

        _pcmStream?.Complete();

        try
        {
            _recognizer?.RecognizeAsyncCancel();
        }
        catch
        {
        }

        try
        {
            if (_recognizer != null)
            {
                _recognizer.SpeechRecognized -= OnSpeechRecognized;
                _recognizer.SpeechDetected -= OnSpeechDetected;
                _recognizer.Dispose();
            }
        }
        catch
        {
        }

        _recognizer = null;

        try
        {
            _recorder?.Dispose();
        }
        catch
        {
        }

        _recorder = null;
        _pcmStream?.Dispose();
        _pcmStream = null;
    }

    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (_stopping) return;
        Interlocked.Exchange(ref _lastAudioTicks, Environment.TickCount64);
        _pcmStream?.Enqueue(data);
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (_stopping) return;
        var text = e.Result?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        Recognized?.Invoke(text);
    }

    private void OnSpeechDetected(object? sender, SpeechDetectedEventArgs e)
    {
        if (_stopping) return;
        SpeechDetected?.Invoke();
    }

    private RecognizerInfo? SelectRecognizer(string listeningLanguage)
    {
        IReadOnlyList<RecognizerInfo> recognizers;
        try
        {
            recognizers = SpeechRecognitionEngine.InstalledRecognizers();
        }
        catch
        {
            return null;
        }

        if (recognizers.Count == 0) return null;

        var requested = ResolveCulture(listeningLanguage);
        RequestedCulture = requested.Name;

        var match = recognizers.FirstOrDefault(info =>
                        info.Culture.Name.Equals(requested.Name, StringComparison.OrdinalIgnoreCase))
                    ?? recognizers.FirstOrDefault(info =>
                        info.Culture.TwoLetterISOLanguageName.Equals(requested.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));

        UsedRecognizerFallback = match == null;
        return match ?? recognizers[0];
    }

    private static CultureInfo ResolveCulture(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return CultureInfo.CurrentUICulture;

        try
        {
            return code.ToLowerInvariant() switch
            {
                "zh" => CultureInfo.GetCultureInfo("zh-CN"),
                "en" => CultureInfo.GetCultureInfo("en-US"),
                "ja" => CultureInfo.GetCultureInfo("ja-JP"),
                "de" => CultureInfo.GetCultureInfo("de-DE"),
                "fr" => CultureInfo.GetCultureInfo("fr-FR"),
                _ => CultureInfo.GetCultureInfo(code),
            };
        }
        catch
        {
            return CultureInfo.CurrentUICulture;
        }
    }

    public void Dispose() => Stop();
}
