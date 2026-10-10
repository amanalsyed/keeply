using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoKeepKill;

public partial class ImageConverterWindow : Window
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    private readonly ObservableCollection<ConversionRow> _results = new();
    private string? _sourceFolder;
    private string? _outputFolder;
    private bool _outputWasChosen;
    private bool _isRunning;
    private CancellationTokenSource? _cancellation;
    private List<string> _files = new();

    public ImageConverterWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
        UpdateFormatControls();
    }

    private void ChooseSource_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder of images to convert",
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
            Description = "Choose or create a folder for converted copies",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = _outputFolder ?? ""
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _outputFolder = Path.GetFullPath(dialog.SelectedPath);
            _outputWasChosen = true;
            OutputFolderText.Text = _outputFolder;
            OutputFolderText.ToolTip = _outputFolder;
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
                _outputFolder = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_sourceFolder)) ?? _sourceFolder, $"{name} - Converted");
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

    private void Format_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsInitialized) UpdateFormatControls();
    }

    private void UpdateFormatControls()
    {
        var format = SelectedFormat();
        var isWebp = format.Equals("webp", StringComparison.OrdinalIgnoreCase);
        WebpLosslessCheck.IsEnabled = isWebp && !_isRunning;
        WebpLosslessCheck.Opacity = isWebp ? 1 : 0.45;
        var qualityEnabled = !format.Equals("png", StringComparison.OrdinalIgnoreCase) && !(isWebp && WebpLosslessCheck.IsChecked == true);
        QualityCombo.IsEnabled = qualityEnabled && !_isRunning;
        QualityLabel.Opacity = qualityEnabled ? 1 : 0.45;
    }

    private void WebpLossless_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) UpdateFormatControls();
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
                ? "No images to convert. Choose another folder or include its subfolders."
                : "Originals will stay in place. Converted copies will be saved separately.";
            TotalsText.Text = _files.Count == 0 ? "No images found" : $"Ready · {_files.Count:N0} images";
            ConvertProgress.Minimum = 0;
            ConvertProgress.Maximum = Math.Max(1, _files.Count);
            ConvertProgress.Value = 0;
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
        StartButton.IsEnabled = !_isRunning && pathsReady && _files.Count > 0 && FormatCombo.SelectedItem is not null;
        OpenOutputButton.IsEnabled = !_isRunning && _outputFolder is not null && Directory.Exists(_outputFolder);
        ChooseSourceButton.IsEnabled = !_isRunning;
        ChooseDestinationButton.IsEnabled = !_isRunning;
        IncludeSubfoldersCheck.IsEnabled = !_isRunning;
        FormatCombo.IsEnabled = !_isRunning;
        UpdateFormatControls();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceFolder is null || _outputFolder is null || _files.Count == 0 || _isRunning) return;
        if (IsSameOrChildFolder(_sourceFolder, _outputFolder))
        {
            ShowError("Choose a separate destination", "The destination cannot be the source folder or one of its subfolders. Choose a different folder so Keeply does not mix new copies into your originals.");
            return;
        }

        var targetFormat = SelectedFormat();
        var quality = SelectedQuality();
        var losslessWebp = WebpLosslessCheck.IsChecked == true;
        _isRunning = true;
        _results.Clear();
        ConvertProgress.Maximum = _files.Count;
        ConvertProgress.Value = 0;
        _cancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        RunStatusText.Text = "Converting copies on this PC…";
        UpdateControls();

        var progress = new Progress<ConversionProgress>(update =>
        {
            ConvertProgress.Value = update.Completed;
            RunStatusText.Text = update.Status;
            TotalsText.Text = update.Totals;
            foreach (var result in update.NewResults) _results.Add(result);
        });

        try
        {
            var cacheScope = $"convert|{Path.GetFullPath(_sourceFolder)}|{Path.GetFullPath(_outputFolder)}|{IncludeSubfoldersCheck.IsChecked == true}|{targetFormat}|{quality}|{losslessWebp}";
            var summary = await Task.Run(() => ConvertFiles(_sourceFolder, _outputFolder, _files, targetFormat, quality, losslessWebp, cacheScope, progress, _cancellation.Token));
            RunStatusText.Text = summary.Cancelled
                ? "Stopped. Converted files are in the destination; originals remain unchanged."
                : summary.Failed == 0
                    ? "Conversion finished. Your original files were not changed."
                    : $"Finished with {summary.Failed:N0} file(s) that could not be converted. See their rows below.";
            TotalsText.Text = FormatSummary(summary);
            ConvertProgress.Value = summary.Completed;
        }
        catch (Exception ex)
        {
            RunStatusText.Text = "The conversion run stopped unexpectedly. Originals remain unchanged.";
            ShowError("Conversion could not finish", ex.Message);
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

    private static ConversionSummary ConvertFiles(
        string source,
        string destination,
        IReadOnlyList<string> files,
        string targetFormat,
        int quality,
        bool losslessWebp,
        string cacheScope,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        var converted = 0;
        var unchanged = 0;
        var skipped = 0;
        var failed = 0;
        var completed = 0;
        var reused = 0;
        long inputBytes = 0;
        long outputBytes = 0;
        var cancelled = false;
        var targetExtension = "." + targetFormat;
        var cache = RepeatWorkCache.Open(cacheScope);

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
            var pending = new List<ConversionRow>();
            string? tempPath = null;
            try
            {
                var sourceBytes = new FileInfo(file).Length;
                if (cache.TryGetCompletedOutput(file, out var cached))
                {
                    inputBytes += sourceBytes;
                    outputBytes += cached.OutputLength;
                    if (cached.WasConverted) converted++; else unchanged++;
                    reused++;
                    pending.Add(new ConversionRow(file, cached.Result, cached.Savings, cached.OutputPath, "Reused the verified converted copy from an earlier run."));
                    completed++;
                    progress.Report(new ConversionProgress(completed,
                        $"Processed {completed:N0} of {files.Count:N0} · reused {reused:N0} saved copies",
                        $"{completed:N0}/{files.Count:N0} · reused {reused:N0} · {converted:N0} converted · {unchanged:N0} copied · {skipped:N0} skipped · {failed:N0} failed",
                        pending));
                    continue;
                }
                var relative = Path.GetRelativePath(source, file);
                var relativeDirectory = Path.GetDirectoryName(relative);
                var outputDirectory = string.IsNullOrEmpty(relativeDirectory)
                    ? destination
                    : Path.Combine(destination, relativeDirectory);
                Directory.CreateDirectory(outputDirectory);
                var inputExtension = Path.GetExtension(file);

                if (IsSameFormat(inputExtension, targetFormat))
                {
                    var sameFormatPath = GetUniqueOutputPath(outputDirectory, Path.GetFileNameWithoutExtension(file), targetExtension);
                    File.Copy(file, sameFormatPath, false);
                    inputBytes += sourceBytes;
                    outputBytes += sourceBytes;
                    unchanged++;
                    var sameFormatSavings = "0 change";
                    pending.Add(new ConversionRow(file, "Already this format · copied", sameFormatSavings, sameFormatPath, "This file already uses the selected output format. A separate copy was saved."));
                    var entry = CreateOutputCacheEntry(file, sameFormatPath, "Already this format · copied", sameFormatSavings, wasConverted: false);
                    cache.Set(entry);
                }
                else
                {
                    using var codec = inputExtension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                        ? SKCodec.Create(file)
                        : null;

                    if (codec is not null && codec.FrameCount > 1)
                    {
                        skipped++;
                        pending.Add(new ConversionRow(file, "Skipped · animated image", "—", file, "Animated images are skipped to avoid flattening the animation."));
                    }
                    else if (IsMultiFrameWpfImage(inputExtension, file))
                    {
                        skipped++;
                        pending.Add(new ConversionRow(file, "Skipped · multiple frames/pages", "—", file, "Animated GIFs and multi-page TIFFs are skipped to preserve every frame or page."));
                    }
                    else
                    {
                        using var bitmap = codec is not null
                            ? SKBitmap.Decode(codec) ?? throw new InvalidDataException("Keeply could not decode this WebP image.")
                            : DecodeWpfBitmap(file);

                        tempPath = Path.Combine(outputDirectory, $".keeply-{Guid.NewGuid():N}.tmp");
                        using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            Encode(bitmap, output, targetFormat, quality, losslessWebp);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            if (File.Exists(tempPath)) File.Delete(tempPath);
                            cancelled = true;
                            break;
                        }

                        var outputPath = GetUniqueOutputPath(outputDirectory, Path.GetFileNameWithoutExtension(file), targetExtension);
                        File.Move(tempPath, outputPath);
                        tempPath = null;
                        var writtenBytes = new FileInfo(outputPath).Length;
                        inputBytes += sourceBytes;
                        outputBytes += writtenBytes;
                        converted++;
                        var mode = targetFormat.Equals("webp", StringComparison.OrdinalIgnoreCase)
                            ? losslessWebp ? " · lossless" : $" · quality {quality}"
                            : targetFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? $" · quality {quality}" : "";
                        var result = $"Converted to {targetFormat.ToUpperInvariant()}{mode}";
                        var savings = FormatSizeChange(sourceBytes - writtenBytes);
                        pending.Add(new ConversionRow(file, result, savings, outputPath, $"Saved as {outputPath}"));
                        cache.Set(CreateOutputCacheEntry(file, outputPath, result, savings, wasConverted: true));
                    }
                }
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
                pending.Add(new ConversionRow(file, "Could not convert", "—", file, ex.Message));
            }

            completed++;
            progress.Report(new ConversionProgress(
                completed,
                $"Processed {completed:N0} of {files.Count:N0} · originals remain untouched",
                $"{completed:N0}/{files.Count:N0} · {converted:N0} converted · {unchanged:N0} already in format · {skipped:N0} skipped · {failed:N0} failed",
                pending));
        }

        try { cache.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        return new ConversionSummary(completed, converted, unchanged, skipped, failed, inputBytes, outputBytes, cancelled, reused);
    }

    private static RepeatWorkEntry CreateOutputCacheEntry(string sourcePath, string outputPath, string result, string savings, bool wasConverted)
    {
        var entry = RepeatWorkCache.SourceEntry(sourcePath);
        var output = new FileInfo(outputPath);
        entry.OutputPath = outputPath;
        entry.OutputLength = output.Length;
        entry.OutputLastWriteUtcTicks = output.LastWriteTimeUtc.Ticks;
        entry.Result = result;
        entry.Savings = savings;
        entry.WasConverted = wasConverted;
        return entry;
    }

    private static SKBitmap DecodeWpfBitmap(string path)
    {
        BitmapSource frame;
        using (var stream = File.OpenRead(path))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count > 1)
                throw new InvalidOperationException("This image contains multiple frames or pages and cannot be converted safely as a single image.");
            frame = decoder.Frames[0];
            frame.Freeze();
        }

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var bitmap = new SKBitmap(new SKImageInfo(converted.PixelWidth, converted.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        try
        {
            var pixels = new byte[bitmap.RowBytes * bitmap.Height];
            converted.CopyPixels(pixels, bitmap.RowBytes, 0);
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static bool IsMultiFrameWpfImage(string extension, string path)
    {
        if (!extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            return false;

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames.Count > 1;
    }

    private static void Encode(SKBitmap bitmap, Stream output, string targetFormat, int quality, bool losslessWebp)
    {
        if (targetFormat.Equals("webp", StringComparison.OrdinalIgnoreCase))
        {
            using var pixmap = bitmap.PeekPixels();
            if (pixmap is null) throw new InvalidOperationException("Could not access the image pixels for WebP conversion.");
            var compression = losslessWebp ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy;
            if (!pixmap.Encode(output, new SKWebpEncoderOptions(compression, quality)))
                throw new InvalidOperationException("The WebP encoder could not write this image.");
            return;
        }

        if (targetFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase))
        {
            using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface is null) throw new InvalidOperationException("Could not prepare the image for JPEG output.");
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.DrawBitmap(bitmap, 0, 0);
            surface.Canvas.Flush();
            using var snapshot = surface.Snapshot();
            using var flattened = SKBitmap.FromImage(snapshot);
            if (!flattened.Encode(output, SKEncodedImageFormat.Jpeg, quality))
                throw new InvalidOperationException("The JPEG encoder could not write this image.");
            return;
        }

        var imageFormat = SKEncodedImageFormat.Png;
        var outputQuality = 100;
        if (!bitmap.Encode(output, imageFormat, outputQuality))
            throw new InvalidOperationException($"The {targetFormat.ToUpperInvariant()} encoder could not write this image.");
    }

    private static bool IsSameFormat(string extension, string targetFormat) =>
        targetFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase)
            ? extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            : extension.Equals("." + targetFormat, StringComparison.OrdinalIgnoreCase);

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

    private string SelectedFormat() =>
        FormatCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item
            ? item.Tag?.ToString() ?? "webp"
            : "webp";

    private int SelectedQuality()
    {
        if (QualityCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var quality)) return quality;
        return 78;
    }

    private static string FormatSummary(ConversionSummary summary)
    {
        var changed = summary.InputBytes - summary.OutputBytes;
        var sizeResult = changed >= 0 ? $"size down {FormatSize(changed)}" : $"size up {FormatSize(-changed)}";
        return $"{summary.Completed:N0} done · {summary.Converted:N0} converted · {summary.Unchanged:N0} copied · {summary.Reused:N0} reused · {summary.Skipped:N0} skipped · {summary.Failed:N0} failed · {sizeResult}";
    }

    private static string FormatSizeChange(long bytes) => bytes > 0 ? $"−{FormatSize(bytes)}" : bytes < 0 ? $"+{FormatSize(-bytes)}" : "0 change";

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
            RunStatusText.Text = "Cancel the conversion run before returning to sorting.";
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

    private void ShowError(string title, string detail) => MessageBox.Show(this, $"{title}.\n\n{detail}", "Keeply · Convert images", MessageBoxButton.OK, MessageBoxImage.Warning);

    private sealed record ConversionProgress(int Completed, string Status, string Totals, IReadOnlyList<ConversionRow> NewResults);
    private sealed record ConversionSummary(int Completed, int Converted, int Unchanged, int Skipped, int Failed, long InputBytes, long OutputBytes, bool Cancelled, int Reused);

    public sealed record ConversionRow(string SourcePath, string Result, string SizeChange, string OutputPath, string Detail)
    {
        public string FileName => Path.GetFileName(SourcePath);
    }
}
