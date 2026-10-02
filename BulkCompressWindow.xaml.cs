using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PhotoKeepKill;

public partial class BulkCompressWindow : Window
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    private readonly ObservableCollection<CompressionRow> _results = new();
    private string? _sourceFolder;
    private string? _outputFolder;
    private bool _outputWasChosen;
    private bool _isRunning;
    private CancellationTokenSource? _cancellation;
    private List<string> _files = new();

    public BulkCompressWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
    }

    private void ChooseSource_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder of images to compress",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = _sourceFolder ?? ""
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetSourceFolder(dialog.SelectedPath);
    }

    private void ChooseDestination_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose or create a folder for compressed copies",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = _outputFolder ?? ""
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _outputFolder = Path.GetFullPath(dialog.SelectedPath);
            _outputWasChosen = true;
            OutputFolderText.Text = _outputFolder;
            UpdateControls();
        }
    }

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var folder = paths.FirstOrDefault(Directory.Exists);
        if (folder is null && File.Exists(paths[0])) folder = Path.GetDirectoryName(paths[0]);
        if (!string.IsNullOrWhiteSpace(folder)) SetSourceFolder(folder);
    }

    private void SetSourceFolder(string path)
    {
        if (_isRunning) return;
        try
        {
            _sourceFolder = Path.GetFullPath(path);
            SourceFolderText.Text = _sourceFolder;
            SourceFolderText.ToolTip = _sourceFolder;
            if (!_outputWasChosen)
            {
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(_sourceFolder));
                if (string.IsNullOrWhiteSpace(name)) name = "Photos";
                _outputFolder = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_sourceFolder)) ?? _sourceFolder, $"{name} - Compressed");
                OutputFolderText.Text = _outputFolder;
                OutputFolderText.ToolTip = _outputFolder;
            }
            _results.Clear();
            ScanFiles();
        }
        catch (Exception ex)
        {
            ShowError("Could not open the source folder", ex.Message);
        }
    }

    private void IncludeSubfolders_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && !_isRunning && _sourceFolder is not null) ScanFiles();
    }

    private void ScanFiles()
    {
        if (_sourceFolder is null) return;
        try
        {
            var option = IncludeSubfoldersCheck.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            _files = Directory.EnumerateFiles(_sourceFolder, "*", option)
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file)))
                .OrderBy(file => Path.GetRelativePath(_sourceFolder, file), StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => Path.GetRelativePath(_sourceFolder, file), StringComparer.Ordinal)
                .ToList();
            SourceCountText.Text = _files.Count == 0
                ? "No supported images found in this folder."
                : $"{_files.Count:N0} supported images · JPG, PNG, WebP, BMP, GIF, and TIFF";
            RunStatusText.Text = _files.Count == 0
                ? "No images to process. Choose another folder or include its subfolders."
                : "Originals will stay in place. Compressed copies will be saved separately.";
            TotalsText.Text = _files.Count == 0 ? "No images found" : $"Ready · {_files.Count:N0} images";
            CompressProgress.Minimum = 0;
            CompressProgress.Maximum = Math.Max(1, _files.Count);
            CompressProgress.Value = 0;
        }
        catch (Exception ex)
        {
            _files.Clear();
            SourceCountText.Text = "Could not scan this folder.";
            RunStatusText.Text = "The folder could not be fully scanned. Check access permissions and try again.";
            ShowError("Could not scan the selected folder", ex.Message);
        }
        UpdateControls();
    }

    private void UpdateControls()
    {
        var pathsReady = _sourceFolder is not null && _outputFolder is not null;
        var qualitySelected = QualityCombo.SelectedItem is System.Windows.Controls.ComboBoxItem;
        StartButton.IsEnabled = !_isRunning && pathsReady && _files.Count > 0 && qualitySelected;
        OpenOutputButton.IsEnabled = !_isRunning && _outputFolder is not null && Directory.Exists(_outputFolder);
        ChooseSourceButton.IsEnabled = !_isRunning;
        ChooseDestinationButton.IsEnabled = !_isRunning;
        IncludeSubfoldersCheck.IsEnabled = !_isRunning;
        QualityCombo.IsEnabled = !_isRunning;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceFolder is null || _outputFolder is null || _files.Count == 0 || _isRunning) return;
        if (IsSameOrChildFolder(_sourceFolder, _outputFolder))
        {
            ShowError("Choose a separate destination", "The destination cannot be the source folder or one of its subfolders. Choose a different folder so Keeply does not mix new copies into your originals.");
            return;
        }

        var quality = SelectedJpegQuality();
        _isRunning = true;
        _results.Clear();
        CompressProgress.Maximum = _files.Count;
        CompressProgress.Value = 0;
        _cancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        RunStatusText.Text = "Compressing copies on this PC…";
        UpdateControls();

        var progress = new Progress<CompressionProgress>(update =>
        {
            CompressProgress.Value = update.Completed;
            RunStatusText.Text = update.Status;
            TotalsText.Text = update.Totals;
            foreach (var result in update.NewResults) _results.Add(result);
        });

        try
        {
            var summary = await Task.Run(() => CompressFiles(_sourceFolder, _outputFolder, _files, quality, progress, _cancellation.Token));
            RunStatusText.Text = summary.Cancelled
                ? "Stopped. Files already completed are in the destination; originals remain unchanged."
                : summary.Failed == 0
                    ? "Compression finished. Your original files were not changed."
                    : $"Finished with {summary.Failed:N0} file(s) that could not be processed. See their rows below.";
            TotalsText.Text = FormatSummary(summary);
            CompressProgress.Value = summary.Completed;
        }
        catch (Exception ex)
        {
            RunStatusText.Text = "The compression run stopped unexpectedly. Originals remain unchanged.";
            ShowError("Compression could not finish", ex.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _isRunning = false;
            CancelButton.IsEnabled = false;
            UpdateControls();
        }
    }

    private static CompressionSummary CompressFiles(
        string source,
        string destination,
        IReadOnlyList<string> files,
        int jpegQuality,
        IProgress<CompressionProgress> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        long originalBytes = 0;
        long resultBytes = 0;
        var compressed = 0;
        var copied = 0;
        var failed = 0;
        var completed = 0;

        var cancelled = false;
        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
            var pending = new List<CompressionRow>();
            string? tempPath = null;
            try
            {
                var sourceBytes = new FileInfo(file).Length;
                originalBytes += sourceBytes;
                var relative = Path.GetRelativePath(source, file);
                var relativeDirectory = Path.GetDirectoryName(relative);
                var outputDirectory = string.IsNullOrEmpty(relativeDirectory)
                    ? destination
                    : Path.Combine(destination, relativeDirectory);
                Directory.CreateDirectory(outputDirectory);

                var extension = Path.GetExtension(file);
                var targetExtension = extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                      extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                                      extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                    ? ".png"
                    : extension;

                long encodedBytes = -1;
                string? transformLabel = null;
                string? copyReason = null;
                try
                {
                    using var stream = File.OpenRead(file);
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 1)
                    {
                        copyReason = "Animation or multiple pages preserved unchanged";
                    }
                    else
                    {
                        var frame = decoder.Frames[0];
                        BitmapEncoder encoder;
                        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                        {
                            encoder = new JpegBitmapEncoder { QualityLevel = jpegQuality };
                            transformLabel = $"JPEG quality {jpegQuality}";
                        }
                        else if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                                 extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                 extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                                 extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                        {
                            encoder = new PngBitmapEncoder();
                            transformLabel = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "PNG lossless" : "Lossless PNG copy";
                        }
                        else if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                        {
                            encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
                            transformLabel = "TIFF lossless · ZIP";
                        }
                        else
                        {
                            copyReason = "No safe quality-controlled encoder is available";
                            encoder = new PngBitmapEncoder();
                        }

                        if (copyReason is null)
                        {
                            encoder.Frames.Add(frame);
                            tempPath = Path.Combine(outputDirectory, $".keeply-{Guid.NewGuid():N}.tmp");
                            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                encoder.Save(output);
                            encodedBytes = new FileInfo(tempPath).Length;
                        }
                    }
                }
                catch (NotSupportedException ex) when (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    copyReason = $"WebP preserved unchanged · {ex.Message}";
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    if (tempPath is not null && File.Exists(tempPath)) File.Delete(tempPath);
                    cancelled = true;
                    break;
                }
                string outputPath;
                string resultLabel;
                long outputBytes;
                if (copyReason is not null || encodedBytes >= sourceBytes || tempPath is null)
                {
                    if (tempPath is not null && File.Exists(tempPath)) File.Delete(tempPath);
                    outputPath = GetUniqueOutputPath(outputDirectory, Path.GetFileNameWithoutExtension(file), extension);
                    File.Copy(file, outputPath, false);
                    outputBytes = new FileInfo(outputPath).Length;
                    resultLabel = copyReason ?? "No size reduction";
                    copied++;
                }
                else
                {
                    outputPath = GetUniqueOutputPath(outputDirectory, Path.GetFileNameWithoutExtension(file), targetExtension);
                    File.Move(tempPath, outputPath);
                    outputBytes = new FileInfo(outputPath).Length;
                    resultLabel = transformLabel ?? "Compressed copy";
                    compressed++;
                }

                resultBytes += outputBytes;
                var isJpeg = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
                var detail = resultLabel == "No size reduction"
                    ? isJpeg
                        ? "The encoded copy was not smaller, so Keeply preserved the original bytes. Choose Smaller files to try a lower JPEG quality."
                        : "The lossless output was not smaller, so Keeply preserved the original bytes."
                    : "Saved as a separate copy; your original was not changed.";
                pending.Add(new CompressionRow(file, resultLabel, FormatSavings(sourceBytes - outputBytes), outputPath, detail));
            }
            catch (OperationCanceledException)
            {
                if (tempPath is not null && File.Exists(tempPath)) File.Delete(tempPath);
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                if (tempPath is not null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* Keep the source; report the operation error below. */ }
                }
                failed++;
                pending.Add(new CompressionRow(file, "Could not process", "—", file, ex.Message));
            }

            completed++;
            var saved = Math.Max(0, originalBytes - resultBytes);
            progress.Report(new CompressionProgress(
                completed,
                $"Processed {completed:N0} of {files.Count:N0} · originals remain untouched",
                $"{completed:N0}/{files.Count:N0} · saved {FormatSize(saved)} · {failed:N0} failed",
                pending));
        }

        return new CompressionSummary(completed, compressed, copied, failed, originalBytes, resultBytes, cancelled);
    }

    private static string GetUniqueOutputPath(string directory, string fileName, string extension)
    {
        var path = Path.Combine(directory, fileName + extension);
        for (var number = 1; File.Exists(path) || Directory.Exists(path); number++)
            path = Path.Combine(directory, $"{fileName} ({number}){extension}");
        return path;
    }

    private static bool IsSameOrChildFolder(string source, string destination)
    {
        var normalizedSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var normalizedDestination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        return StringComparer.OrdinalIgnoreCase.Equals(normalizedSource, normalizedDestination) ||
               normalizedDestination.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private int SelectedJpegQuality()
    {
        if (QualityCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var quality)) return quality;
        return 82;
    }

    private static string FormatSummary(CompressionSummary summary)
    {
        var saved = Math.Max(0, summary.OriginalBytes - summary.OutputBytes);
        return $"{summary.Completed:N0} done · {summary.Compressed:N0} compressed · {summary.Copied:N0} unchanged · {summary.Failed:N0} failed · saved {FormatSize(saved)}";
    }

    private static string FormatSavings(long bytes)
    {
        if (bytes > 0) return $"−{FormatSize(bytes)}";
        if (bytes < 0) return $"+{FormatSize(-bytes)}";
        return "0 saved";
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:N0} {units[unit]}" : $"{value:N1} {units[unit]}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        RunStatusText.Text = "Stopping after the current image…";
        _cancellation?.Cancel();
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_outputFolder is null || !Directory.Exists(_outputFolder)) return;
        try { Process.Start(new ProcessStartInfo(_outputFolder) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Could not open the destination folder", ex.Message); }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            RunStatusText.Text = "Cancel the compression run before returning to sorting.";
            return;
        }
        Close();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isRunning) return;
        e.Cancel = true;
        Cancel_Click(this, new RoutedEventArgs());
    }

    private void ShowError(string title, string detail) => MessageBox.Show(this, $"{title}.\n\n{detail}", "Keeply · Bulk Compress", MessageBoxButton.OK, MessageBoxImage.Warning);

    private sealed record CompressionProgress(int Completed, string Status, string Totals, IReadOnlyList<CompressionRow> NewResults);
    private sealed record CompressionSummary(int Completed, int Compressed, int Copied, int Failed, long OriginalBytes, long OutputBytes, bool Cancelled);

    public sealed record CompressionRow(string SourcePath, string Result, string Savings, string OutputPath, string Detail)
    {
        public string FileName => Path.GetFileName(SourcePath);
    }
}
