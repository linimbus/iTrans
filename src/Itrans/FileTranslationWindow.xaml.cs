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
using Itrans.Services;
using Forms = System.Windows.Forms;

namespace Itrans;

public partial class FileTranslationWindow : Window
{
    private readonly ObservableCollection<FileItem> _files = new();
    private IReadOnlyList<string> _supportedExtensions = FileTranslationService.DefaultSupportedExtensions;
    private CancellationTokenSource? _cancellation;
    private bool _running;

    public FileTranslationWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) => UpdateEmptyHint();
        UpdateServiceText();
        UpdateSupportedText();
        ApplyDirectionFromSettings();
        OutputDirBox.Text = ResolveInitialOutputDirectory();
        UpdateEmptyHint();
        SetRunning(false);
        Loaded += OnLoaded;
        Closing += (_, _) => _cancellation?.Cancel();
        App.Instance.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) => App.Instance.SettingsChanged -= OnSettingsChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _supportedExtensions = await App.Instance.FileTranslation.GetSupportedExtensionsAsync(
                App.Instance.Settings.ServerUrl, CancellationToken.None);
        }
        catch
        {
        }

        UpdateSupportedText();
    }

    private void OnSettingsChanged()
    {
        UpdateServiceText();
        UpdateSupportedText();
    }

    private void UpdateServiceText()
    {
        var url = App.Instance.Settings.ServerUrl;
        ServiceText.Text = string.IsNullOrWhiteSpace(url) ? "服务未配置" : "服务 " + url;
    }

    private void UpdateSupportedText()
    {
        var timeout = App.Instance.Settings.FileTranslationTimeoutSeconds;
        SupportedText.Text = $"支持格式：{string.Join(" ", _supportedExtensions)} · 单文件超时 {timeout} 秒（可在设置修改）";
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
        DirectionBox.SelectedValue = App.Instance.Settings.TranslationMode switch
        {
            "zh-en" => "zh-en",
            "en-zh" => "en-zh",
            _ => "auto-zh",
        };
        if (DirectionBox.SelectedIndex < 0) DirectionBox.SelectedIndex = 0;
    }

    private (string Source, string Target) GetDirection()
    {
        return (DirectionBox.SelectedValue as string) switch
        {
            "zh-en" => ("zh", "en"),
            "en-zh" => ("en", "zh"),
            "auto-en" => ("auto", "en"),
            _ => ("auto", "zh"),
        };
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
        DirectionBox.IsEnabled = !running;
        OutputDirBox.IsEnabled = !running;
        BrowseButton.IsEnabled = !running;
        BusyBar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要翻译的文件",
            Multiselect = true,
            CheckFileExists = true,
            Filter = BuildFilter(),
        };
        if (dialog.ShowDialog(this) != true) return;

        var added = 0;
        var skipped = 0;
        foreach (var path in dialog.FileNames)
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
            SetStatus($"已添加 {added} 个文件，跳过 {skipped} 个不支持的格式");
        else if (added > 0)
            SetStatus($"已添加 {added} 个文件");
        else
            SetStatus("没有添加新文件");
    }

    private string BuildFilter()
    {
        var patterns = string.Join(";", _supportedExtensions.Select(extension => "*" + extension));
        return $"LibreTranslate 支持的文件|{patterns}|所有文件|*.*";
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        var selected = FileList.SelectedItems.Cast<FileItem>().ToArray();
        foreach (var item in selected) _files.Remove(item);
        if (selected.Length > 0) SetStatus($"已移除 {selected.Length} 个文件");
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        _files.Clear();
        SetStatus("已清空文件列表");
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择译文保存目录",
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
        SetStatus("正在取消…");
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (_files.Count == 0)
        {
            SetStatus("请先添加要翻译的文件");
            return;
        }

        var settings = App.Instance.Settings;
        if (string.IsNullOrWhiteSpace(settings.ServerUrl))
        {
            SetStatus("请先在设置中填写 LibreTranslate 服务地址");
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
            SetStatus("输出目录不可用：" + ex.Message);
            return;
        }
        RememberOutputDirectory(outputDirectory);

        var (source, target) = GetDirection();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var succeeded = 0;
        var failed = 0;

        SetRunning(true);
        SetStatus($"开始翻译…（单文件超时 {settings.FileTranslationTimeoutSeconds} 秒）");
        try
        {
            foreach (var item in _files)
            {
                if (token.IsCancellationRequested) break;

                item.Status = "上传翻译中…";
                item.StatusBrush = FileItem.RunningBrush;
                ProgressText.Text = item.FileName;

                try
                {
                    var result = await App.Instance.FileTranslation.TranslateFileAsync(
                        item.FullPath, source, target, settings, token);

                    var outputPath = EnsureUniquePath(Path.Combine(outputDirectory, result.FileName));
                    await File.WriteAllBytesAsync(outputPath, result.Content, token);

                    item.OutputPath = outputPath;
                    item.Status = "完成 · " + Path.GetFileName(outputPath);
                    item.StatusBrush = FileItem.SuccessBrush;
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "已取消";
                    item.StatusBrush = FileItem.WaitingBrush;
                    break;
                }
                catch (Exception ex)
                {
                    item.Status = "失败：" + Shorten(ex.Message, 80);
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
                SetStatus($"已取消：成功 {succeeded}，失败 {failed}");
            else
                SetStatus(failed == 0 ? $"翻译完成：成功 {succeeded} 个文件" : $"翻译结束：成功 {succeeded}，失败 {failed}");

            if (!cancelled && succeeded > 0)
                App.Instance.Tray?.ShowBalloon("文件翻译完成", $"已保存 {succeeded} 个译文文件。");
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

    private void SetStatus(string message) => StatusText.Text = message;
}

public sealed class FileItem : INotifyPropertyChanged
{
    public static readonly Brush WaitingBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x84, 0x94));
    public static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
    public static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x9D, 0x58));
    public static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

    private string _status = "等待";
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

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

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
