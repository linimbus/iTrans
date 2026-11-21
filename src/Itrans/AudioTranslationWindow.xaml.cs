using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Itrans.Audio;
using Itrans.Localization;
using Itrans.Services;

namespace Itrans;

public partial class AudioTranslationWindow : Window
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly SpeechCaptureSession _session = new();
    private readonly SemaphoreSlim _translateLock = new(1, 1);
    private readonly object _sessionGate = new();
    private CancellationTokenSource? _sessionCts;
    private IReadOnlyList<AudioApplication> _applications = Array.Empty<AudioApplication>();
    private IReadOnlyList<AudioDevice> _microphones = Array.Empty<AudioDevice>();
    private string? _defaultMicrophoneId;
    private bool _updatingSource;
    private bool _initializingListening;
    private bool _recording;
    private bool _starting;
    private bool _closing;
    private int _segmentIndex;
    private string _activeListeningLanguage = "en";
    private string _statusKey = "audio.statusReady";
    private object?[] _statusArgs = Array.Empty<object?>();

    public AudioTranslationWindow()
    {
        InitializeComponent();
        _session.Recognized += OnRecognized;
        _session.SpeechDetected += OnSpeechDetected;
        App.Instance.Health.StateChanged += UpdateServiceDot;
        App.Instance.Health.Check();
        Loc.LanguageChanged += OnLanguageChanged;

        PopulateSourceOptions();
        InitializeListeningLanguages();
        UpdateDeviceOptions();
        UpdateServiceDot();
        SetRecordingUi(false);
        SetStatus("audio.statusReady");

        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void PopulateSourceOptions()
    {
        var selected = SourceBox.SelectedValue as string ?? "system";
        var items = new List<AudioSourceOption>
        {
            new("system", Loc.T("audio.sourceSystem")),
            new("application", Loc.T("audio.sourceApplication")),
            new("microphone", Loc.T("audio.sourceMicrophone")),
        };

        _updatingSource = true;
        try
        {
            SourceBox.ItemsSource = items;
            SourceBox.SelectedValue = selected;
            if (SourceBox.SelectedIndex < 0) SourceBox.SelectedIndex = 0;
        }
        finally
        {
            _updatingSource = false;
        }
    }

    private AudioSourceKind GetSourceKind()
        => (SourceBox.SelectedValue as string) switch
        {
            "application" => AudioSourceKind.Application,
            "microphone" => AudioSourceKind.Microphone,
            _ => AudioSourceKind.System,
        };

    private void InitializeListeningLanguages()
    {
        _initializingListening = true;
        try
        {
            var selected = App.Instance.Settings.AudioListeningLanguage;
            if (string.IsNullOrWhiteSpace(selected)) selected = "en";

            var items = new List<AudioLanguageOption>
            {
                new("en", "English"),
                new("zh", "中文（简体）"),
            };

            ListeningLanguageBox.ItemsSource = items;
            ListeningLanguageBox.SelectedValue = selected;
            if (ListeningLanguageBox.SelectedIndex < 0) ListeningLanguageBox.SelectedIndex = 0;
            _activeListeningLanguage = ListeningLanguageBox.SelectedValue as string ?? "en";
        }
        finally
        {
            _initializingListening = false;
        }
    }

    private void ListeningLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingListening) return;
        if (ListeningLanguageBox.SelectedValue is not string code || string.IsNullOrWhiteSpace(code)) return;

        _activeListeningLanguage = code;
        var settings = App.Instance.Settings;
        if (string.Equals(settings.AudioListeningLanguage, code, StringComparison.OrdinalIgnoreCase)) return;

        settings.AudioListeningLanguage = code;
        try
        {
            settings.Save();
        }
        catch
        {
        }
    }

    private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSource || _recording || _starting) return;
        if (UpdateDeviceOptions()) SetStatus("audio.statusReady");
    }

    private bool UpdateDeviceOptions()
    {
        var kind = GetSourceKind();
        var previousApplication = (DeviceBox.SelectedItem as AudioApplication)?.ProcessId;
        var previousDevice = (DeviceBox.SelectedItem as AudioDevice)?.Id;
        var showDevice = kind != AudioSourceKind.System;
        DeviceLabel.Visibility = showDevice ? Visibility.Visible : Visibility.Collapsed;
        DeviceBox.Visibility = showDevice ? Visibility.Visible : Visibility.Collapsed;

        if (kind == AudioSourceKind.Application)
        {
            _applications = AudioDeviceCatalog.GetApplications();
            if (_applications.Count == 0)
            {
                DeviceBox.ItemsSource = new[] { new AudioApplication(0, Loc.T("audio.noApps")) };
                DeviceBox.SelectedIndex = 0;
                if (!_recording && !_starting) SetStatus("audio.statusNoApps");
                return false;
            }

            DeviceBox.ItemsSource = _applications;
            var index = 0;
            if (previousApplication.HasValue)
            {
                var found = _applications
                    .Select((application, i) => (application, i))
                    .FirstOrDefault(pair => pair.application.ProcessId == previousApplication.Value);
                if (found.application != null) index = found.i;
            }
            DeviceBox.SelectedIndex = index;
        }
        else if (kind == AudioSourceKind.Microphone)
        {
            _microphones = AudioDeviceCatalog.GetMicrophones(out _defaultMicrophoneId);
            if (_microphones.Count == 0)
            {
                DeviceBox.ItemsSource = new[] { new AudioDevice("", Loc.T("audio.noDevices")) };
                DeviceBox.SelectedIndex = 0;
                if (!_recording && !_starting) SetStatus("audio.statusNoSource");
                return false;
            }

            DeviceBox.ItemsSource = _microphones;
            var index = 0;
            var preferred = !string.IsNullOrWhiteSpace(previousDevice) ? previousDevice : _defaultMicrophoneId;
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var found = _microphones
                    .Select((device, i) => (device, i))
                    .FirstOrDefault(pair => pair.device.Id == preferred);
                if (found.device != null) index = found.i;
            }
            DeviceBox.SelectedIndex = index;
        }

        return true;
    }

    private void DeviceBox_DropDownOpened(object sender, EventArgs e)
    {
        if (_recording || _starting || GetSourceKind() != AudioSourceKind.Application) return;

        var selected = (DeviceBox.SelectedItem as AudioApplication)?.ProcessId;
        _applications = AudioDeviceCatalog.GetApplications();
        if (_applications.Count == 0)
        {
            DeviceBox.ItemsSource = new[] { new AudioApplication(0, Loc.T("audio.noApps")) };
            DeviceBox.SelectedIndex = 0;
            return;
        }

        DeviceBox.ItemsSource = _applications;
        var index = 0;
        if (selected.HasValue)
        {
            var found = _applications
                .Select((application, i) => (application, i))
                .FirstOrDefault(pair => pair.application.ProcessId == selected.Value);
            if (found.application != null) index = found.i;
        }
        DeviceBox.SelectedIndex = index;
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recording)
        {
            StopRecording();
            return;
        }

        if (_starting) return;
        await StartRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        if (_closing || _recording || _starting) return;

        var kind = GetSourceKind();
        uint processId = 0;
        string? deviceId = null;

        if (kind == AudioSourceKind.Application)
        {
            if (DeviceBox.SelectedItem is not AudioApplication application || application.ProcessId == 0)
            {
                SetStatus("audio.statusNoApps");
                return;
            }
            processId = application.ProcessId;
        }
        else if (kind == AudioSourceKind.Microphone)
        {
            if (DeviceBox.SelectedItem is not AudioDevice device || string.IsNullOrWhiteSpace(device.Id))
            {
                SetStatus("audio.statusNoSource");
                return;
            }
            deviceId = device.Id;
        }

        _starting = true;
        RecordButton.IsEnabled = false;
        SourceBox.IsEnabled = false;
        DeviceBox.IsEnabled = false;
        ListeningLanguageBox.IsEnabled = false;
        SetStatus("audio.statusStarting");

        var listeningLanguage = ListeningLanguageBox.SelectedValue as string ?? "en";
        _activeListeningLanguage = listeningLanguage;

        var cts = new CancellationTokenSource();
        lock (_sessionGate)
        {
            _sessionCts?.Dispose();
            _sessionCts = cts;
        }

        try
        {
            await _session.StartAsync(kind, processId, deviceId, listeningLanguage, cts.Token);
            if (_closing)
            {
                _session.Stop();
                return;
            }

            _recording = true;
            SetRecordingUi(true);

            if (_session.UsedRecognizerFallback
                && !string.IsNullOrWhiteSpace(_session.RequestedCulture)
                && !string.IsNullOrWhiteSpace(_session.RecognizerCulture))
            {
                SetStatus("audio.statusRecognizerFallback", _session.RequestedCulture, _session.RecognizerCulture);
            }
            else
            {
                SetStatus("audio.statusRecording");
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("audio.statusStopped");
        }
        catch (Exception ex)
        {
            _session.Stop();
            SetStatus("audio.statusAudioError", ex.Message);
        }
        finally
        {
            _starting = false;
            RecordButton.IsEnabled = true;
            SourceBox.IsEnabled = !_recording;
            DeviceBox.IsEnabled = !_recording;
            ListeningLanguageBox.IsEnabled = !_recording;
        }
    }

    private void StopRecording()
    {
        if (!_recording && !_starting) return;

        SetStatus("audio.statusStopping");

        CancellationTokenSource? cts;
        lock (_sessionGate)
        {
            cts = _sessionCts;
            _sessionCts = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        _session.Stop();
        cts?.Dispose();

        _recording = false;
        SetRecordingUi(false);
        SetStatus("audio.statusStopped");
    }

    private void SetRecordingUi(bool recording)
    {
        RecordButton.Content = Loc.T(recording ? "audio.stop" : "audio.start");
        SourceBox.IsEnabled = !recording;
        DeviceBox.IsEnabled = !recording;
        ListeningLanguageBox.IsEnabled = !recording;
        ClearButton.IsEnabled = true;
    }

    private void OnSpeechDetected()
    {
        if (_closing) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_recording) SetStatus("audio.statusSpeech");
        });
    }

    private void OnRecognized(string text)
    {
        if (_closing || string.IsNullOrWhiteSpace(text)) return;

        var index = Interlocked.Increment(ref _segmentIndex);
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            AppendLine(OriginalBox, index, text);
            if (_recording) SetStatus("audio.statusRecording");
        });

        _ = TranslateSegmentAsync(index, text);
    }

    private async Task TranslateSegmentAsync(int index, string text)
    {
        CancellationToken token;
        lock (_sessionGate)
        {
            if (_sessionCts == null) return;
            token = _sessionCts.Token;
        }

        try
        {
            await _translateLock.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (token.IsCancellationRequested) return;

            var translationSettings = App.Instance.Settings.Clone();
            if (!string.IsNullOrWhiteSpace(_activeListeningLanguage))
                translationSettings.TranslationSource = _activeListeningLanguage;

            var result = await App.Instance.Translation.TranslateAsync(text, translationSettings, token);
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_closing) return;
                AppendLine(TranslationBox, index, result.TranslatedText);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_closing) return;
                AppendLine(TranslationBox, index, Loc.Format("audio.translationFailed", ex.Message));
                SetStatus("audio.statusTranslatorError", ex.Message);
            });
        }
        finally
        {
            _translateLock.Release();
        }
    }

    private static void AppendLine(TextBox box, int index, string text)
    {
        if (box.Text.Length > 0) box.AppendText(Environment.NewLine);
        box.AppendText(index.ToString(CultureInfo.CurrentCulture) + ". " + text);
        box.ScrollToEnd();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        OriginalBox.Clear();
        TranslationBox.Clear();
        _segmentIndex = 0;
        SetStatus(_recording ? "audio.statusRecording" : "audio.statusReady");
    }

    private void OnLanguageChanged()
    {
        PopulateSourceOptions();
        UpdateDeviceOptions();
        SetRecordingUi(_recording);
        RefreshStatus();
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

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        StopRecording();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        App.Instance.Health.StateChanged -= UpdateServiceDot;
        Loc.LanguageChanged -= OnLanguageChanged;
        _session.Recognized -= OnRecognized;
        _session.SpeechDetected -= OnSpeechDetected;
        _session.Dispose();
    }
}
