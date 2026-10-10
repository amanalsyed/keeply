using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Drawing;
using System.Drawing.Imaging;
using DrawingImage = System.Drawing.Image;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;

namespace PhotoKeepKill;

public partial class LibraryOrganizerWindow : Window
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff" };
    private static readonly string[] Categories = { "Date · year", "Date · month", "Date · day", "Camera / device", "GPS location", "Likely screenshots", "File format", "Dimensions", "Exact duplicates", "Similar photos" };
    private readonly LocalLicenseStore _licenseStore;
    private readonly Action _showLicense;
    private readonly Action<string, IReadOnlyList<string>, string> _review;
    private readonly List<LibraryPhoto> _photos = new();
    private string? _root;
    private string? _indexedRoot;
    private bool _indexedSubfolders;
    private bool _busy;
    private CancellationTokenSource? _scanCancellation;

    internal LibraryOrganizerWindow(LocalLicenseStore licenseStore, Action showLicense, Action<string, IReadOnlyList<string>, string> review)
    {
        InitializeComponent();
        _licenseStore = licenseStore;
        _showLicense = showLicense;
        _review = review;
        CategoryBox.ItemsSource = Categories;
        CategoryBox.SelectedIndex = 0;
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a photo library folder", UseDescriptionForTitle = true, ShowNewFolderButton = false, SelectedPath = _root ?? "" };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { _root = Path.GetFullPath(dialog.SelectedPath); ScanStatus.Text = _root; }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { _scanCancellation?.Cancel(); return; }
        if (string.IsNullOrWhiteSpace(_root) || !Directory.Exists(_root)) { ChooseFolder_Click(sender, e); if (string.IsNullOrWhiteSpace(_root)) return; }
        _busy = true; ScanButton.Content = "Cancel"; ScanProgress.Value = 0; _photos.Clear(); GroupsList.ItemsSource = null; PhotosList.ItemsSource = null; ReviewButton.IsEnabled = false; SaveGroupButton.IsEnabled = false; SaveAllButton.IsEnabled = false;
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;
        try
        {
            // Snapshot WPF-owned values on the UI thread before starting background folder enumeration.
            var root = _root!;
            var includeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
            var forceRefresh = RecheckAllCheck.IsChecked == true;
            var paths = await Task.Run(() => Enumerate(root, includeSubfolders, token), token);
            if (!_licenseStore.IsLicensed && paths.Count > 100)
            {
                ScanStatus.Text = $"This library has {paths.Count:N0} photos. Free use supports up to 100 per folder; nothing was changed.";
                _showLicense(); return;
            }
            if (!_licenseStore.IsLicensed && paths.Count > 0)
            {
                var hash = LocalLicenseStore.HashFolder(_root!);
                if (!_licenseStore.FolderHashes.Contains(hash) && _licenseStore.FolderHashes.Count >= 3)
                { ScanStatus.Text = "Free use includes 3 unique folders on this PC; nothing was changed."; _showLicense(); return; }
                if (!_licenseStore.FolderHashes.Contains(hash)) _licenseStore.RememberFolder(hash);
            }
            var progress = new Progress<(int Percent, string Message)>(p => { ScanProgress.Value = p.Percent; ScanStatus.Text = p.Message; });
            var cached = forceRefresh
                ? new Dictionary<string, LibraryIndexEntry>(StringComparer.OrdinalIgnoreCase)
                : await Task.Run(() => LibraryIndexCache.Load(root, includeSubfolders), token);
            var result = await RunStaAsync(() => Analyze(paths, cached, progress, token), token);
            _photos.AddRange(result.Photos);
            _indexedRoot = root; _indexedSubfolders = includeSubfolders;
            try { await Task.Run(() => LibraryIndexCache.Save(root, includeSubfolders, _photos.Select(ToIndexEntry)), token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { ScanStatus.Text = $"Photos analyzed, but the local scan index could not be saved: {ex.Message}"; }
            RecheckAllCheck.IsChecked = false;
            ScanProgress.Value = 100;
            ScanStatus.Text = $"Analyzed {_photos.Count:N0} photos from {root}. Reused {result.Reused:N0} cached; read {result.Refreshed:N0} new or changed. Files were not changed.";
            CategoryBox.IsEnabled = _photos.Count > 0;
            RefreshGroups();
        }
        catch (OperationCanceledException) { ScanStatus.Text = "Analysis canceled. No files were changed."; }
        catch (Exception ex) { ScanStatus.Text = $"Analysis failed; no files were changed. {ex.Message}"; }
        finally { _busy = false; ScanButton.Content = "Analyze"; _scanCancellation?.Dispose(); _scanCancellation = null; }
    }

    private static List<string> Enumerate(string root, bool recursive, CancellationToken token)
    {
        var result = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested(); var folder = pending.Pop();
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
                if (Supported.Contains(Path.GetExtension(path))) result.Add(path);
            if (!recursive) continue;
            foreach (var child in Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
        }
        return result.OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static AnalyzeResult Analyze(List<string> paths, IReadOnlyDictionary<string, LibraryIndexEntry> cache, IProgress<(int Percent, string Message)> progress, CancellationToken token)
    {
        var result = new List<LibraryPhoto>(paths.Count);
        var reused = 0; var refreshed = 0;
        for (var i = 0; i < paths.Count; i++)
        {
            token.ThrowIfCancellationRequested(); var path = paths[i]; var info = new FileInfo(path);
            LibraryPhoto photo;
            if (cache.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
            {
                photo = FromIndexEntry(cached); reused++;
            }
            else
            {
                photo = ReadPhoto(path, info);
                try { using var stream = File.OpenRead(path); photo.Sha256 = Convert.ToHexString(SHA256.HashData(stream)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { photo.Sha256 = "unreadable:" + path; }
                refreshed++;
            }
            result.Add(photo);
            if (i % 10 == 0 || i == paths.Count - 1) progress.Report((paths.Count == 0 ? 100 : (i + 1) * 100 / paths.Count, $"Checking local index {i + 1:N0}/{paths.Count:N0} · reused {reused:N0}, refreshed {refreshed:N0}…"));
        }
        return new AnalyzeResult(result, reused, refreshed);
    }

    private static LibraryPhoto ReadPhoto(string path, FileInfo file)
    {
        var photo = new LibraryPhoto { Path = path, Length = file.Length, LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks, Format = file.Extension.TrimStart('.').ToUpperInvariant(), Width = 0, Height = 0, Date = file.LastWriteTime, DateSource = "file modified" };
        try
        {
            using var image = DrawingImage.FromFile(path);
            photo.Width = image.Width; photo.Height = image.Height;
            photo.Camera = FirstExif(image, 0x0110, 0x010F);
            var dateText = FirstExif(image, 0x9003, 0x0132);
            if (TryParseExifDate(dateText, out var captured)) { photo.Date = captured; photo.DateSource = "capture date"; }
            photo.Gps = ReadGps(image);
            var software = FirstExif(image, 0x0131);
            var searchable = (Path.GetFileName(path) + " " + software).ToLowerInvariant();
            photo.IsScreenshot = searchable.Contains("screenshot") || searchable.Contains("screen shot") || searchable.Contains("snipping tool") || searchable.Contains("snip & sketch") || searchable.Contains("screen capture");
        }
        catch
        {
            // GDI+ does not decode every supported format (notably some WebP files); Skia supplies dimensions where possible.
            try
            {
                using var bitmap = SKBitmap.Decode(path);
                if (bitmap is not null) { photo.Width = bitmap.Width; photo.Height = bitmap.Height; }
            }
            catch { }
        }
        if (!photo.IsScreenshot)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            photo.IsScreenshot = name.Contains("screenshot") || name.Contains("screen_shot") || name.Contains("screen-shot") || name.Contains("screen capture");
        }
        return photo;
    }

    private static LibraryPhoto FromIndexEntry(LibraryIndexEntry entry) => new()
    {
        Path = entry.Path, Length = entry.Length, LastWriteUtcTicks = entry.LastWriteUtcTicks, Format = entry.Format,
        Width = entry.Width, Height = entry.Height, Date = entry.Date, DateSource = entry.DateSource,
        Camera = entry.Camera, Gps = entry.Gps, IsScreenshot = entry.IsScreenshot, Sha256 = entry.Sha256,
        DifferenceFingerprint = entry.DifferenceFingerprint
    };

    private static LibraryIndexEntry ToIndexEntry(LibraryPhoto photo) => new()
    {
        Path = photo.Path, Length = photo.Length, LastWriteUtcTicks = photo.LastWriteUtcTicks, Format = photo.Format,
        Width = photo.Width, Height = photo.Height, Date = photo.Date, DateSource = photo.DateSource,
        Camera = photo.Camera, Gps = photo.Gps, IsScreenshot = photo.IsScreenshot, Sha256 = photo.Sha256,
        DifferenceFingerprint = photo.DifferenceFingerprint
    };

    private static string FirstExif(DrawingImage image, params int[] ids)
    {
        foreach (var id in ids)
        {
            try
            {
                if (!image.PropertyIdList.Contains(id)) continue;
                var item = image.GetPropertyItem(id);
                if (item?.Value is not { Length: > 0 } bytes) continue;
                var text = System.Text.Encoding.ASCII.GetString(bytes).Trim('\0', ' ', '\t', '\r', '\n');
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch { }
        }
        return "";
    }

    private static bool TryParseExifDate(string text, out DateTime date)
    {
        return DateTime.TryParseExact(text, new[] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
    }

    private static string ReadGps(DrawingImage image)
    {
        try
        {
            var latRef = FirstExif(image, 0x0001); var lonRef = FirstExif(image, 0x0003);
            var latitudeItem = image.GetPropertyItem(0x0002); var longitudeItem = image.GetPropertyItem(0x0004);
            if (latitudeItem?.Value is not { Length: > 0 } latitudeBytes || longitudeItem?.Value is not { Length: > 0 } longitudeBytes ||
                !TryGpsValue(latitudeBytes, out var latitude) || !TryGpsValue(longitudeBytes, out var longitude)) return "";
            if (string.Equals(latRef, "S", StringComparison.OrdinalIgnoreCase)) latitude = -latitude;
            if (string.Equals(lonRef, "W", StringComparison.OrdinalIgnoreCase)) longitude = -longitude;
            return $"{latitude:F2}, {longitude:F2}";
        }
        catch { return ""; }
    }

    private static bool TryGpsValue(byte[] value, out double degrees)
    {
        degrees = 0;
        try
        {
            if (value.Length < 24) return false;
            double Rational(int offset)
            {
                var numerator = BitConverter.ToUInt32(value, offset); var denominator = BitConverter.ToUInt32(value, offset + 4);
                return denominator == 0 ? 0 : (double)numerator / denominator;
            }
            degrees = Rational(0) + Rational(8) / 60 + Rational(16) / 3600;
            return true;
        }
        catch { }
        return false;
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded && !_busy) RefreshGroups(); }

    private async void RefreshGroups()
    {
        if (CategoryBox.SelectedItem is not string category) return;
        GroupsList.ItemsSource = null; PhotosList.ItemsSource = null; ReviewButton.IsEnabled = false; SaveGroupButton.IsEnabled = false; SaveAllButton.IsEnabled = false;
        if (category == "Similar photos")
        {
            ScanStatus.Text = "Comparing visual fingerprints locally…"; ScanButton.IsEnabled = false; CategoryBox.IsEnabled = false;
            try
            {
                await RunStaAsync(() => { BuildSimilarGroups(); return true; });
                if (_indexedRoot is string indexedRoot)
                    await Task.Run(() => LibraryIndexCache.Save(indexedRoot, _indexedSubfolders, _photos.Select(ToIndexEntry)));
            }
            catch (Exception ex) { ScanStatus.Text = $"Similar-photo comparison failed: {ex.Message}"; }
            finally { ScanButton.IsEnabled = true; CategoryBox.IsEnabled = _photos.Count > 0; }
        }
        var groups = BuildGroups(category);
        GroupsList.ItemsSource = groups;
        SaveAllButton.IsEnabled = groups.Count > 0;
        GroupStatus.Text = groups.Count == 0 ? "No groups found for this category." : $"{groups.Count:N0} groups · choose one to review.";
    }

    private List<PhotoGroup> BuildGroups(string category)
    {
        if (category == "Similar photos")
            return _similarGroups.Select((photos, i) => new PhotoGroup($"Similar match {i + 1}", photos.Count, photos))
                .OrderByDescending(g => g.Count).ToList();
        IEnumerable<IGrouping<string, LibraryPhoto>> grouped = category switch
        {
            "Date · year" => _photos.GroupBy(p => p.Date.ToString("yyyy", CultureInfo.InvariantCulture)),
            "Date · month" => _photos.GroupBy(p => p.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture)),
            "Date · day" => _photos.GroupBy(p => p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "Camera / device" => _photos.Where(p => !string.IsNullOrWhiteSpace(p.Camera)).GroupBy(p => p.Camera),
            "GPS location" => _photos.Where(p => !string.IsNullOrWhiteSpace(p.Gps)).GroupBy(p => p.Gps),
            "Likely screenshots" => _photos.Where(p => p.IsScreenshot).GroupBy(_ => "Likely screenshots"),
            "File format" => _photos.GroupBy(p => p.Format),
            "Dimensions" => _photos.Where(p => p.Width > 0 && p.Height > 0).GroupBy(p => $"{p.Width} × {p.Height}"),
            "Exact duplicates" => _photos.Where(p => !p.Sha256.StartsWith("unreadable:", StringComparison.Ordinal)).GroupBy(p => p.Sha256).Where(g => g.Count() > 1),
            _ => Enumerable.Empty<LibraryPhoto>().GroupBy(_ => "")
        };
        return grouped.Select(g => new PhotoGroup(category == "Exact duplicates" ? "Exact match" : category == "Similar photos" ? "Visually similar" : g.Key, g.Count(), g.Select(p => p).ToList()))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private readonly List<List<LibraryPhoto>> _similarGroups = new();

    // Difference-hash candidates are bucketed before comparison, avoiding the all-pairs scan for large libraries.
    private void BuildSimilarGroups()
    {
        _similarGroups.Clear();
        var candidates = new List<(LibraryPhoto Photo, ulong Hash)>();
        var exactDuplicatePaths = _photos.Where(p => !p.Sha256.StartsWith("unreadable:", StringComparison.Ordinal))
            .GroupBy(p => p.Sha256, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(p => p.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var photo in _photos.Where(p => !exactDuplicatePaths.Contains(p.Path)))
        {
            try
            {
                photo.DifferenceFingerprint ??= DifferenceHash(photo.Path);
                candidates.Add((photo, photo.DifferenceFingerprint.Value));
            }
            catch { }
        }
        var union = new DisjointSet(candidates.Count);
        var buckets = new Dictionary<(int Band, ushort Value), List<int>>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var hash = candidates[i].Hash; var possible = new HashSet<int>();
            for (var band = 0; band < 4; band++)
            {
                var value = (ushort)((hash >> (band * 16)) & 0xFFFF);
                AddBucketCandidates(buckets, band, value, possible);
                for (var bit = 0; bit < 16; bit++) AddBucketCandidates(buckets, band, (ushort)(value ^ (1 << bit)), possible);
            }
            foreach (var other in possible)
                if (BitOperations.PopCount(hash ^ candidates[other].Hash) <= 7) union.Join(i, other);
            for (var band = 0; band < 4; band++)
            {
                var value = (ushort)((hash >> (band * 16)) & 0xFFFF); var key = (band, value);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(i);
            }
        }
        _similarGroups.AddRange(Enumerable.Range(0, candidates.Count).GroupBy(union.Find).Where(g => g.Count() > 1)
            .Select(g => g.Select(index => candidates[index].Photo).ToList()));
    }

    private static void AddBucketCandidates(Dictionary<(int Band, ushort Value), List<int>> buckets, int band, ushort value, HashSet<int> result)
    { if (buckets.TryGetValue((band, value), out var indexes)) foreach (var index in indexes) result.Add(index); }

    private static ulong DifferenceHash(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException("Image could not be decoded.");
        var pixels = new byte[72];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 9; x++)
            {
                var color = bitmap.GetPixel(Math.Min(bitmap.Width - 1, x * bitmap.Width / 9), Math.Min(bitmap.Height - 1, y * bitmap.Height / 8));
                pixels[y * 9 + x] = (byte)((color.Red * 299 + color.Green * 587 + color.Blue * 114) / 1000);
            }
        ulong hash = 0; var bit = 0;
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++, bit++) if (pixels[y * 9 + x] > pixels[y * 9 + x + 1]) hash |= 1UL << bit;
        return hash;
    }

    private static Task<T> RunStaAsync<T>(Func<T> work, CancellationToken token = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(work());
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Keeply local image analysis" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsList.SelectedItem is not PhotoGroup group) { PhotosList.ItemsSource = null; ReviewButton.IsEnabled = false; SaveGroupButton.IsEnabled = false; return; }
        var rows = group.Photos.OrderBy(p => p.Date).Select(p => new PhotoRow(Path.GetFileName(p.Path), p.Path,
            $"{p.Date:yyyy-MM-dd} ({p.DateSource}) · {(string.IsNullOrWhiteSpace(p.Camera) ? "Camera unknown" : p.Camera)} · {p.Width}×{p.Height} · {p.Format}" )).ToList();
        PhotosList.ItemsSource = rows; ReviewButton.IsEnabled = rows.Count > 0; SaveGroupButton.IsEnabled = rows.Count > 0;
        GroupStatus.Text = $"{rows.Count:N0} photos · Review is manual; Keeply will not delete or move anything unless you choose an action.";
    }

    private async void SaveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || GroupsList.SelectedItem is not PhotoGroup group) return;
        var parent = ChooseSaveLocation();
        if (parent is null) return;
        var folderName = AskFolderName(group.Title);
        if (folderName is null) return;
        await SaveGroupsAsync(new[] { group }, parent, new[] { folderName }, folderName, createBatchFolder: false);
    }

    private async void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || GroupsList.ItemsSource is not IEnumerable<PhotoGroup> currentGroups) return;
        var groups = currentGroups.ToList();
        if (groups.Count == 0) return;
        var parent = ChooseSaveLocation();
        if (parent is null) return;
        var category = CategoryBox.SelectedItem as string ?? "Photo groups";
        await SaveGroupsAsync(groups, parent, groups.Select(g => g.Title).ToList(), $"Keeply - {category}", createBatchFolder: true);
    }

    private string? ChooseSaveLocation()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where Keeply should create the copied group folders",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = _root ?? ""
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? Path.GetFullPath(dialog.SelectedPath) : null;
    }

    private string? AskFolderName(string suggestedName)
    {
        var dialog = new Window
        {
            Owner = this, Title = "Name this photo group", Width = 430, Height = 205,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(MediaColor.FromRgb(25, 28, 31)), Foreground = new SolidColorBrush(MediaColor.FromRgb(244, 245, 245))
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Folder name", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var nameBox = new TextBox { Text = suggestedName, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 14), Background = new SolidColorBrush(MediaColor.FromRgb(32, 36, 39)), Foreground = MediaBrushes.White, BorderBrush = new SolidColorBrush(MediaColor.FromRgb(69, 77, 80)) };
        panel.Children.Add(nameBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Continue", MinWidth = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(MediaColor.FromRgb(36, 88, 67)) };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        buttons.Children.Add(save); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
        string? answer = null;
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(nameBox.Text)) { answer = nameBox.Text.Trim(); dialog.Close(); } };
        dialog.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
        dialog.ShowDialog();
        return answer;
    }

    private async Task SaveGroupsAsync(IReadOnlyList<PhotoGroup> groups, string destination, IReadOnlyList<string> names, string batchFolderName, bool createBatchFolder)
    {
        if (_busy) return;
        _busy = true; ScanButton.IsEnabled = SaveAllButton.IsEnabled = SaveGroupButton.IsEnabled = ReviewButton.IsEnabled = CategoryBox.IsEnabled = false;
        var totalFiles = groups.Sum(g => g.Photos.Count);
        ScanProgress.Minimum = 0; ScanProgress.Maximum = Math.Max(1, totalFiles); ScanProgress.Value = 0;
        var progress = new Progress<(int Done, string Message)>(value => { ScanProgress.Value = value.Done; GroupStatus.Text = value.Message; });
        try
        {
            var result = await Task.Run(() => CopyGroups(groups, destination, names, batchFolderName, createBatchFolder, progress));
            GroupStatus.Text = result.Failures == 0
                ? $"Copied {result.Copied:N0} photos into {result.GroupCount:N0} folder(s). Originals were left untouched. Saved to {result.RootPath}"
                : $"Copied {result.Copied:N0}; {result.Failures:N0} could not be copied. Originals were left untouched. Saved to {result.RootPath}. First issue: {result.FirstError}";
            ScanProgress.Value = ScanProgress.Maximum;
        }
        catch (Exception ex) { GroupStatus.Text = $"Could not save the groups. Any copies already completed remain in the destination; originals were not changed. {ex.Message}"; }
        finally
        {
            _busy = false; ScanButton.IsEnabled = true; CategoryBox.IsEnabled = _photos.Count > 0;
            SaveAllButton.IsEnabled = GroupsList.Items.Count > 0;
            SaveGroupButton.IsEnabled = GroupsList.SelectedItem is PhotoGroup selected && selected.Photos.Count > 0;
            ReviewButton.IsEnabled = SaveGroupButton.IsEnabled;
        }
    }

    private static CopyResult CopyGroups(IReadOnlyList<PhotoGroup> groups, string destination, IReadOnlyList<string> names, string batchFolderName, bool createBatchFolder, IProgress<(int Done, string Message)> progress)
    {
        var root = createBatchFolder ? CreateUniqueDirectory(destination, SafeFolderName(batchFolderName)) : destination;
        var totalPhotos = groups.Sum(g => g.Photos.Count);
        var copied = 0; var failed = 0; var done = 0; string? firstError = null;
        var usedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastSavedFolder = root;
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var baseName = SafeFolderName(names[groupIndex]); var groupFolderName = baseName; var suffix = 2;
            while (!usedFolderNames.Add(groupFolderName)) groupFolderName = $"{baseName} ({suffix++})";
            var groupFolder = CreateUniqueDirectory(root, groupFolderName);
            lastSavedFolder = groupFolder;
            foreach (var photo in groups[groupIndex].Photos)
            {
                try
                {
                    if (!File.Exists(photo.Path)) throw new FileNotFoundException("The source photo is no longer available.", photo.Path);
                    var target = UniqueFilePath(groupFolder, Path.GetFileName(photo.Path));
                    File.Copy(photo.Path, target, false); copied++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { failed++; firstError ??= $"{Path.GetFileName(photo.Path)}: {ex.Message}"; }
                done++;
                if (done % 5 == 0 || done == totalPhotos)
                    progress.Report((done, $"Saving group {groupIndex + 1:N0}/{groups.Count:N0} · {done:N0}/{totalPhotos:N0} photos copied…"));
            }
        }
        return new CopyResult(copied, failed, groups.Count, createBatchFolder ? root : lastSavedFolder, firstError ?? "");
    }

    private static string CreateUniqueDirectory(string parent, string name)
    {
        var candidate = Path.Combine(parent, name); var suffix = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate)) candidate = Path.Combine(parent, $"{name} ({suffix++})");
        return Directory.CreateDirectory(candidate).FullName;
    }

    private static string UniqueFilePath(string folder, string filename)
    {
        var path = Path.Combine(folder, filename); if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(filename); var extension = Path.GetExtension(filename);
        for (var suffix = 2; ; suffix++) { path = Path.Combine(folder, $"{stem} ({suffix}){extension}"); if (!File.Exists(path)) return path; }
    }

    private static string SafeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) || char.IsControl(character) ? '-' : character).ToArray()).Trim(' ', '.');
        if (safe.Length > 90) safe = safe[..90].TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(safe)) safe = "Photo group";
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reserved.Contains(safe, StringComparer.OrdinalIgnoreCase)) safe = "_" + safe;
        return safe;
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsList.SelectedItem is PhotoGroup group && _root is not null) { _review(_root, group.Photos.Select(p => p.Path).ToList(), group.Title); DialogResult = true; Close(); }
    }

    private sealed class LibraryPhoto
    {
        public string Path { get; init; } = ""; public long Length { get; set; } public long LastWriteUtcTicks { get; set; }
        public string Format { get; set; } = ""; public int Width { get; set; } public int Height { get; set; }
        public DateTime Date { get; set; } public string DateSource { get; set; } = ""; public string Camera { get; set; } = ""; public string Gps { get; set; } = "";
        public bool IsScreenshot { get; set; } public string Sha256 { get; set; } = ""; public ulong? DifferenceFingerprint { get; set; }
    }
    private sealed record AnalyzeResult(List<LibraryPhoto> Photos, int Reused, int Refreshed);
    private sealed record PhotoGroup(string Title, int Count, List<LibraryPhoto> Photos);
    private sealed record PhotoRow(string Name, string Path, string Details);
    private sealed record CopyResult(int Copied, int Failures, int GroupCount, string RootPath, string FirstError);
    private sealed class DisjointSet
    {
        private readonly int[] _parent; private readonly byte[] _rank;
        public DisjointSet(int count) { _parent = Enumerable.Range(0, count).ToArray(); _rank = new byte[count]; }
        public int Find(int value) { if (_parent[value] != value) _parent[value] = Find(_parent[value]); return _parent[value]; }
        public void Join(int a, int b) { a = Find(a); b = Find(b); if (a == b) return; if (_rank[a] < _rank[b]) _parent[a] = b; else { _parent[b] = a; if (_rank[a] == _rank[b]) _rank[a]++; } }
    }
}
