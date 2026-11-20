using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Itrans.Localization;
using Itrans.Services;
using Forms = System.Windows.Forms;

namespace Itrans;

public partial class FileTranslationWindow : Window
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly ObservableCollection<FileItem> _files = new();
    private IReadOnlyList<string> _supportedExtensions = FileTranslationService.DefaultSupportedExtensions;
    private CancellationTokenSource? _cancellation;
    private bool _running;
    private string _statusKey = "file.supportedFormats";
    private object?[] _statusArgs = Array.Empty<object?>();

    public FileTranslationWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) => UpdateEmptyHint();
        UpdateColumnHeaders();
        UpdateServiceDot();
        PopulateLanguageBoxes(App.Instance.Languages);
        ApplyDirectionFromSettings();
        UpdateSupportedText();
        OutputDirBox.Text = ResolveInitialOutputDirectory();
        UpdateEmptyHint();
        SetRunning(false);

        Loaded += OnLoaded;
        Closing += (_, _) => _cancellation?.Cancel();
        App.Instance.SettingsChanged += OnSettingsChanged;
        App.Instance.Health.StateChanged += UpdateServiceDot;
        App.Instance.LanguagesChanged += OnLanguagesChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            App.Instance.SettingsChanged -= OnSettingsChanged;
            App.Instance.Health.StateChanged -= UpdateServiceDot;
            App.Instance.LanguagesChanged -= OnLanguagesChanged;
            Loc.LanguageChanged -= OnLanguageChanged;
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Instance.Health.Check();

        try
        {
            _supportedExtensions = await App.Instance.FileTranslation.GetSupportedExtensionsAsync(
                App.Instance.Settings.ServerUrl, CancellationToken.None);
        }
        catch
        {
        }

        await App.Instance.RefreshLanguagesAsync();
        ApplyDirectionFromSettings();
        UpdateSupportedText();
    }

    private void OnSettingsChanged()
    {
        App.Instance.Health.Check(true);
        ApplyDirectionFromSettings();
        UpdateSupportedText();
    }

    private void OnLanguagesChanged()
    {
        PopulateLanguageBoxes(App.Instance.Languages);
        ApplyDirectionFromSettings();
    }

    private void OnLanguageChanged()
    {
        UpdateColumnHeaders();
        RefreshStatus();
        PopulateLanguageBoxes(App.Instance.Languages);
        foreach (var item in _files) item.RefreshLocalization();
    }

    private void UpdateServiceDot()
    {
        var health = App.Instance.Health;
        ServiceDot.Fill = ServiceHealthBrushes.For(health.State);
        ServiceDot.ToolTip = health.Message;
        System.Windows.Automation.AutomationProperties.SetName(ServiceDot, health.Message);
    }

    private void UpdateColumnHeaders()
    {
        FileNameColumn.Header = Loc.T("file.columnFile");
        FileSizeColumn.Header = Loc.T("file.columnSize");
        FileStatusColumn.Header = Loc.T("file.columnStatus");
    }

    private void PopulateLanguageBoxes(IReadOnlyList<TranslationLanguage>? languages)
    {
        languages ??= App.Instance.Languages;
        var source = SourceBox.SelectedValue as string ?? App.Instance.Settings.GetSourceLanguage();
        var target = TargetBox.SelectedValue as string ?? App.Instance.Settings.GetTargetLanguage();

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

        SourceBox.ItemsSource = sourceItems;
        SourceBox.SelectedValue = source;
        if (SourceBox.SelectedIndex < 0) SourceBox.SelectedIndex = 0;

        TargetBox.ItemsSource = targetItems;
        TargetBox.SelectedValue = target;
        if (TargetBox.SelectedIndex < 0) TargetBox.SelectedIndex = 0;
    }

    private void UpdateSupportedText()
    {
        var timeout = App.Instance.Settings.FileTranslationTimeoutSeconds;
        SetStatus("file.supportedFormats", string.Join(" ", _supportedExtensions), timeout);
    }

    private static string GetDefaultOutputDirectory()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop)) return desktop;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? Environment.CurrentDirectory : profile;
    }

    private static string ResolveInitialOutputDirectory()
    {
        var configured = App.Instance.Settings.FileOutputDirectory?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? GetDefaultOutputDirectory() : configured;
    }

    private static void RememberOutputDirectory(string path)
    {
        var settings = App.Instance.Settings;
        if (string.Equals(settings.FileOutputDirectory, path, StringComparison.OrdinalIgnoreCase)) return;

        settings.FileOutputDirectory = path;
        try
        {
            settings.Save();
        }
        catch
        {
        }
    }

    private void ApplyDirectionFromSettings()
    {
        SourceBox.SelectedValue = App.Instance.Settings.GetSourceLanguage();
        TargetBox.SelectedValue = App.Instance.Settings.GetTargetLanguage();
        if (SourceBox.SelectedIndex < 0) SourceBox.SelectedIndex = 0;
        if (TargetBox.SelectedIndex < 0) TargetBox.SelectedIndex = 0;
    }

    private (string Source, string Target) GetDirection()
    {
        var source = SourceBox.SelectedValue as string;
        var target = TargetBox.SelectedValue as string;
        return (string.IsNullOrWhiteSpace(source) ? "auto" : source,
                string.IsNullOrWhiteSpace(target) ? "auto" : target);
    }

    private void UpdateEmptyHint()
    {
        EmptyHint.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        AddButton.IsEnabled = !running;
        RemoveButton.IsEnabled = !running;
        ClearButton.IsEnabled = !running;
        StartButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        SourceBox.IsEnabled = !running;
        TargetBox.IsEnabled = !running;
        OutputDirBox.IsEnabled = !running;
        BrowseButton.IsEnabled = !running;
        BusyBar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("file.selectFilesTitle"),
            Multiselect = true,
            CheckFileExists = true,
            Filter = BuildFilter(),
        };
        if (dialog.ShowDialog(this) != true) return;

        AddFiles(dialog.FileNames);
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_running && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_running) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        AddFiles(paths);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = 0;
        foreach (var path in paths)
        {
            if (_files.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!_supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            _files.Add(new FileItem(path));
            added++;
        }

        if (skipped > 0)
            SetStatus("file.addedWithSkipped", added, skipped);
        else if (added > 0)
            SetStatus("file.addedCount", added);
        else
            SetStatus("file.noNewFiles");
    }

    private string BuildFilter()
    {
        var patterns = string.Join(";", _supportedExtensions.Select(extension => "*" + extension));
        return $"{Loc.T("file.filterSupported")}|{patterns}|{Loc.T("file.filterAll")}|*.*";
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        var selected = FileList.SelectedItems.Cast<FileItem>().ToArray();
        foreach (var item in selected) _files.Remove(item);
        if (selected.Length > 0) SetStatus("file.removedCount", selected.Length);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        _files.Clear();
        SetStatus("file.cleared");
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = Loc.T("file.selectOutputDir"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        var current = OutputDirBox.Text.Trim();
        if (current.Length > 0 && Directory.Exists(current)) dialog.SelectedPath = current;
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputDirBox.Text = dialog.SelectedPath;
            RememberOutputDirectory(dialog.SelectedPath);
        }
    }

    private void OutputDirBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var path = OutputDirBox.Text.Trim();
        if (path.Length > 0) RememberOutputDirectory(path);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        SetStatus("file.cancelling");
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (_files.Count == 0)
        {
            SetStatus("file.addFilesFirst");
            return;
        }

        var settings = App.Instance.Settings;
        if (string.IsNullOrWhiteSpace(settings.ServerUrl))
        {
            SetStatus("file.serverUrlRequired");
            return;
        }

        var outputDirectory = OutputDirBox.Text.Trim();
        if (outputDirectory.Length == 0)
        {
            outputDirectory = GetDefaultOutputDirectory();
            OutputDirBox.Text = outputDirectory;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            SetStatus("file.outputUnavailable", ex.Message);
            return;
        }
        RememberOutputDirectory(outputDirectory);

        var (source, target) = GetDirection();
        if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && source.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("settings.sameLanguage");
            return;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var succeeded = 0;
        var failed = 0;

        SetRunning(true);
        SetStatus("file.starting", settings.FileTranslationTimeoutSeconds);
        try
        {
            foreach (var item in _files)
            {
                if (token.IsCancellationRequested) break;

                item.SetStatus("file.uploading");
                item.StatusBrush = FileItem.RunningBrush;
                ProgressText.Text = item.FileName;

                try
                {
                    var result = await App.Instance.FileTranslation.TranslateFileAsync(
                        item.FullPath, source, target, settings, token);

                    var outputPath = EnsureUniquePath(Path.Combine(outputDirectory, result.FileName));
                    await File.WriteAllBytesAsync(outputPath, result.Content, token);

                    item.OutputPath = outputPath;
                    item.SetStatus("file.done", Path.GetFileName(outputPath));
                    item.StatusBrush = FileItem.SuccessBrush;
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    item.SetStatus("file.cancelled");
                    item.StatusBrush = FileItem.WaitingBrush;
                    break;
                }
                catch (Exception ex)
                {
                    item.SetStatus("file.failed", Shorten(ex.Message, 80));
                    item.StatusBrush = FileItem.ErrorBrush;
                    failed++;
                }
            }
        }
        finally
        {
            var cancelled = token.IsCancellationRequested;
            _cancellation.Dispose();
            _cancellation = null;
            SetRunning(false);
            ProgressText.Text = "";

            if (cancelled)
                SetStatus("file.cancelledSummary", succeeded, failed);
            else if (failed == 0)
                SetStatus("file.completedSummary", succeeded);
            else
                SetStatus("file.endedSummary", succeeded, failed);

            if (!cancelled && succeeded > 0)
                App.Instance.Tray?.ShowBalloon(Loc.T("file.completedBalloonTitle"), Loc.Format("file.completedBalloonText", succeeded));
        }
    }

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        return path;
    }

    private static string Shorten(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem item) return;

        var target = item.OutputPath ?? item.FullPath;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"");
        }
        catch
        {
        }
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        RefreshStatus();
    }

    private void RefreshStatus() => StatusText.Text = Loc.Format(_statusKey, _statusArgs);
}

public sealed class FileItem : INotifyPropertyChanged
{
    public static readonly Brush WaitingBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x84, 0x94));
    public static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
    public static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x9D, 0x58));
    public static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

    private static LocalizationService Loc => LocalizationService.Instance;
    private string _statusKey = "file.statusWaiting";
    private object?[] _statusArgs = Array.Empty<object?>();
    private Brush _statusBrush = WaitingBrush;

    public FileItem(string fullPath)
    {
        FullPath = fullPath;
        var info = new FileInfo(fullPath);
        SizeText = FormatSize(info.Exists ? info.Length : 0);
    }

    public string FullPath { get; }

    public string FileName => Path.GetFileName(FullPath);

    public string SizeText { get; }

    public string? OutputPath { get; set; }

    public string Status => Loc.Format(_statusKey, _statusArgs);

    public void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
    }

    public void RefreshLocalization()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));

    public Brush StatusBrush
    {
        get => _statusBrush;
        set
        {
            if (ReferenceEquals(_statusBrush, value)) return;
            _statusBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / 1024.0 / 1024.0:0.#} MB";
    }
}
