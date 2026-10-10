using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;

namespace PhotoKeepKill;

public partial class DuplicateFinderWindow : Window
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    private readonly LocalLicenseStore _licenseStore;
    private readonly Action _showLicense;
    private readonly ObservableCollection<DuplicateGroup> _groups = new();
    private readonly Stack<List<string>> _trashUndo = new();
    private CancellationTokenSource? _scanCancellation;
    private string? _rootFolder;
    private bool _busy;

    internal DuplicateFinderWindow(LocalLicenseStore licenseStore, Action showLicense)
    {
        InitializeComponent();
        _licenseStore = licenseStore;
        _showLicense = showLicense;
        GroupsList.ItemsSource = _groups;
        UpdateUndoButton();
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder to scan for duplicate photos",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = _rootFolder ?? ""
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) SetFolder(dialog.SelectedPath);
    }

    private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var folder = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0]);
        if (folder is not null) SetFolder(folder);
    }

    private void SetFolder(string folder)
    {
        if (_busy) return;
        _rootFolder = Path.GetFullPath(folder);
        ScanStatus.Text = $"Ready to scan { _rootFolder }.";
        ReviewEmptyText.Text = "Press Scan to compare photos in this folder.";
        _groups.Clear();
        PhotosItems.ItemsSource = null;
        SelectionText.Text = "Select a group to compare its photos.";
        UpdateActionButtons();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _scanCancellation?.Cancel();
            return;
        }
        if (_rootFolder is null || !Directory.Exists(_rootFolder))
        {
            ChooseFolder_Click(sender, e);
            if (_rootFolder is null) return;
        }

        var includeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
        var root = _rootFolder!;
        _busy = true;
        ScanButton.Content = "Cancel scan";
        IncludeSubfoldersCheck.IsEnabled = false;
        ChooseFolder_ClickButtonEnabled(false);
        _scanCancellation = new CancellationTokenSource();
        ScanProgress.Value = 0;
        _groups.Clear();
        PhotosItems.ItemsSource = null;
        ReviewEmptyPanel.Visibility = Visibility.Visible;
        ReviewEmptyText.Text = "Scanning stays on this PC…";

        try
        {
            var progress = new Progress<ScanProgressInfo>(info =>
            {
                ScanStatus.Text = info.Message;
                ScanProgress.Value = info.Percent;
            });
            var result = await Task.Run(() => ScanFolder(root, includeSubfolders, progress, _scanCancellation.Token));
            if (!_licenseStore.IsLicensed)
            {
                if (result.Files.Count > 100)
                {
                    ScanStatus.Text = $"This scan found {result.Files.Count:N0} photos. Free use allows up to 100 photos in a folder; nothing was changed.";
                    MessageBox.Show(this, $"This scan found {result.Files.Count:N0} supported photos. Free use allows up to 100 photos per folder. Nothing was changed. Would you like to activate or purchase a lifetime license?", "Lifetime license needed", MessageBoxButton.OK, MessageBoxImage.Information);
                    _showLicense();
                    return;
                }
                if (result.Files.Count > 0)
                {
                    var folderHash = LocalLicenseStore.HashFolder(root);
                    var known = _licenseStore.FolderHashes.Contains(folderHash);
                    if (!known && _licenseStore.FolderHashes.Count >= 3)
                    {
                        ScanStatus.Text = "Free folder allowance used. Nothing was changed.";
                        MessageBox.Show(this, "Free use includes 3 unique folders on this PC. Reopening an allowed folder is free. Nothing was changed. Would you like to activate or purchase a lifetime license?", "Lifetime license needed", MessageBoxButton.OK, MessageBoxImage.Information);
                        _showLicense();
                        return;
                    }
                    if (!known) _licenseStore.RememberFolder(folderHash);
                }
            }

            foreach (var group in result.Groups) _groups.Add(group);
            var visuallyUnreadable = result.VisualFailures.Count;
            ScanStatus.Text = $"Scanned {result.Files.Count:N0} photos. Found {_groups.Count:N0} groups ({result.Groups.Count(g => g.Kind == DuplicateKind.Exact)} exact, {result.Groups.Count(g => g.Kind == DuplicateKind.Similar)} similar). Reused {result.Reused:N0} cached fingerprints." +
                (visuallyUnreadable > 0 ? $" {visuallyUnreadable:N0} photo(s) could not be compared visually; exact matching was still checked." : " All comparison stayed on this PC.");
            ReviewEmptyPanel.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ReviewEmptyText.Text = _groups.Count == 0 ? "No duplicate groups found." : "Choose a group to compare photos side by side.";
            ScanProgress.Value = 100;
            if (visuallyUnreadable > 0)
                MessageBox.Show(this, $"{visuallyUnreadable:N0} photo(s) could not be decoded for visual comparison. Their file names are listed below. Exact duplicate checks were still performed where the files could be read.", "Some photos could not be compared", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            ScanStatus.Text = "Scan canceled. No photos were changed.";
            ReviewEmptyText.Text = "Scan canceled. Choose Scan to try again.";
            ReviewEmptyPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Scan failed: {ex.Message}";
            ReviewEmptyText.Text = "The scan could not be completed.";
            ReviewEmptyPanel.Visibility = Visibility.Visible;
            MessageBox.Show(this, $"Keeply couldn't complete the duplicate scan. No photos were changed.\n\n{ex.Message}", "Duplicate scan failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            _busy = false;
            ScanButton.Content = "Scan";
            IncludeSubfoldersCheck.IsEnabled = true;
            ChooseFolder_ClickButtonEnabled(true);
            UpdateActionButtons();
        }
    }

    private void ChooseFolder_ClickButtonEnabled(bool enabled)
    {
        if (Content is Grid root && root.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetRow(g) == 0) is Grid header && header.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Content, "Choose folder")) is Button choose)
            choose.IsEnabled = enabled;
        ScanButton.IsEnabled = true;
    }

    private static ScanResult ScanFolder(string root, bool includeSubfolders, IProgress<ScanProgressInfo> progress, CancellationToken token)
    {
        var files = EnumeratePhotoPaths(root, includeSubfolders, token);
        var cache = RepeatWorkCache.Open($"duplicates|{Path.GetFullPath(root)}|{includeSubfolders}");
        var analyzed = new List<PhotoAnalysis>(files.Count);
        var visualFailures = new List<string>();
        var reused = 0;
        for (var i = 0; i < files.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var path = files[i];
            if (cache.TryGetCurrentSource(path, out var cached) && !string.IsNullOrWhiteSpace(cached.Sha256))
            {
                analyzed.Add(new PhotoAnalysis(path, cached.Sha256, cached.DifferenceFingerprint));
                if (!cached.DifferenceFingerprint.HasValue) visualFailures.Add($"{Path.GetFileName(path)} — previously unavailable for visual comparison");
                reused++;
            }
            else
            {
                string sha256;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 64, FileOptions.SequentialScan))
                    sha256 = Convert.ToHexString(SHA256.HashData(stream));
                ulong? fingerprint = null;
                string? visualFailure = null;
                try { fingerprint = CreateDifferenceHash(path); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    visualFailure = $"{Path.GetFileName(path)} — {ex.Message}";
                    visualFailures.Add(visualFailure);
                }
                analyzed.Add(new PhotoAnalysis(path, sha256, fingerprint));
                var entry = RepeatWorkCache.SourceEntry(path);
                entry.Sha256 = sha256;
                entry.DifferenceFingerprint = fingerprint;
                cache.Set(entry);
            }
            if (i % 8 == 0 || i == files.Count - 1)
                progress.Report(new ScanProgressInfo(files.Count == 0 ? 35 : (int)((i + 1) * 35d / files.Count), $"Checking {i + 1:N0} / {files.Count:N0} photos · reused {reused:N0}…"));
        }

        token.ThrowIfCancellationRequested();
        cache.Save();
        var groups = new List<DuplicateGroup>();
        var exactFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicateSet in analyzed.GroupBy(item => item.Sha256, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            var members = duplicateSet.Select(item => item.Path).ToList();
            foreach (var member in members) exactFiles.Add(member);
            groups.Add(new DuplicateGroup(DuplicateKind.Exact, members));
        }

        var similar = analyzed.Where(item => item.Fingerprint.HasValue && !exactFiles.Contains(item.Path)).ToList();
        var union = new DisjointSet(similar.Count);
        var comparisons = (long)similar.Count * (similar.Count - 1) / 2;
        long compared = 0;
        for (var i = 0; i < similar.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            for (var j = i + 1; j < similar.Count; j++)
            {
                if (similar[i].Sha256 != similar[j].Sha256 && BitOperations.PopCount(similar[i].Fingerprint!.Value ^ similar[j].Fingerprint!.Value) <= 7)
                    union.Join(i, j);
                compared++;
            }
            if (i % 12 == 0 || i == similar.Count - 1)
            {
                var percent = comparisons == 0 ? 100 : 35 + (int)(compared * 64 / comparisons);
                progress.Report(new ScanProgressInfo(percent, $"Comparing visual similarities {compared:N0} / {comparisons:N0}…"));
            }
        }
        foreach (var indexes in Enumerable.Range(0, similar.Count).GroupBy(union.Find).Where(group => group.Count() > 1))
            groups.Add(new DuplicateGroup(DuplicateKind.Similar, indexes.Select(index => similar[index].Path).ToList()));

        var orderedGroups = groups.OrderByDescending(group => group.Kind == DuplicateKind.Exact).ThenBy(group => group.Photos.Count).ToList();
        for (var i = 0; i < orderedGroups.Count; i++) orderedGroups[i].Number = i + 1;
        return new ScanResult(files, orderedGroups, visualFailures, reused);
    }

    private static List<string> EnumeratePhotoPaths(string root, bool recursive, CancellationToken token)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var path in Directory.EnumerateFiles(current, "*", System.IO.SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                if (Supported.Contains(Path.GetExtension(path))) result.Add(path);
            }
            if (!recursive) continue;
            foreach (var directory in Directory.EnumerateDirectories(current, "*", System.IO.SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0) pending.Push(directory);
            }
        }
        return result.OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal).ToList();
    }

    private static ulong CreateDifferenceHash(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("The image has no readable frame.");
        var source = decoder.Frames[0];
        var scaled = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(9d / source.PixelWidth, 8d / source.PixelHeight));
        var gray = new FormatConvertedBitmap(scaled, System.Windows.Media.PixelFormats.Gray8, null, 0);
        var pixels = new byte[8 * 9];
        gray.CopyPixels(pixels, 9, 0);
        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++, bit++)
                if (pixels[y * 9 + x] > pixels[y * 9 + x + 1]) hash |= 1UL << bit;
        return hash;
    }

    private async void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsList.SelectedItem is not DuplicateGroup group)
        {
            PhotosItems.ItemsSource = null;
            ReviewEmptyPanel.Visibility = Visibility.Visible;
            ReviewEmptyText.Text = "Choose a group to compare photos side by side.";
            SelectionText.Text = "Select a group to compare its photos.";
            UpdateActionButtons();
            return;
        }

        ReviewEmptyPanel.Visibility = Visibility.Collapsed;
        SelectionText.Text = group.Kind == DuplicateKind.Exact
            ? "Exact matches share identical file contents. Select only the copies you want to remove."
            : "These photos look similar. Compare carefully and select only the ones you want to remove.";
        var photoModels = group.Photos.Select(path => new PhotoCard(path, group.Kind == DuplicateKind.Exact ? "Exact file match" : "Visually similar photo")).ToList();
        foreach (var photo in photoModels) photo.PropertyChanged += PhotoCard_PropertyChanged;
        PhotosItems.ItemsSource = photoModels;
        UpdateSelectionCount(photoModels);
        var selectedGroup = group;
        var previews = photoModels.Select(async card =>
        {
            try { card.Preview = await Task.Run(() => DecodePreview(card.Path)); }
            catch (Exception ex) { card.Details = $"Preview unavailable · {ex.Message}"; }
        }).ToArray();
        await Task.WhenAll(previews);
        if (!ReferenceEquals(GroupsList.SelectedItem, selectedGroup)) return;
    }

    private void PhotoCard_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoCard.IsSelected) && PhotosItems.ItemsSource is IEnumerable<PhotoCard> cards)
            UpdateSelectionCount(cards);
    }

    private void UpdateSelectionCount(IEnumerable<PhotoCard> cards)
    {
        var list = cards.ToList();
        var count = list.Count(photo => photo.IsSelected);
        SelectionText.Text = $"{count} selected · {list.Count} photos in this group";
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        var cards = PhotosItems.ItemsSource as IEnumerable<PhotoCard>;
        var list = cards?.ToList() ?? new List<PhotoCard>();
        var selectedCount = list.Count(photo => photo.IsSelected);
        TrashSelectedButton.IsEnabled = !_busy && selectedCount > 0;
        SelectAllButton.IsEnabled = !_busy && list.Count > 0;
        SelectAllButton.Content = list.Count > 0 && selectedCount == list.Count ? "Clear selection" : "Select all";
        UpdateUndoButton();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (PhotosItems.ItemsSource is not IEnumerable<PhotoCard> source) return;
        var cards = source.ToList();
        var select = cards.Any(photo => !photo.IsSelected);
        foreach (var card in cards) card.IsSelected = select;
        UpdateSelectionCount(cards);
    }

    private async void TrashSelected_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsList.SelectedItem is not DuplicateGroup group || PhotosItems.ItemsSource is not IEnumerable<PhotoCard> source) return;
        var selectedPaths = source.Where(photo => photo.IsSelected).Select(photo => photo.Path).ToList();
        if (selectedPaths.Count == 0) return;
        await MovePhotosToRecycleBinAsync(group, selectedPaths);
    }

    private async void GroupTrash_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_busy || sender is not Button { DataContext: DuplicateGroup group }) return;
        var selected = group.Photos.ToList();
        await MovePhotosToRecycleBinAsync(group, selected);
    }

    private async Task MovePhotosToRecycleBinAsync(DuplicateGroup group, List<string> selectedPaths)
    {
        if (selectedPaths.Count == 0 || _busy) return;
        var wholeGroup = selectedPaths.Count == group.Photos.Count;
        var actionDescription = wholeGroup ? "all photos in this match group" : $"{selectedPaths.Count:N0} selected photo(s)";
        var confirm = MessageBox.Show(this, $"Move {actionDescription} ({selectedPaths.Count:N0}) to the Windows Recycle Bin? You can undo this action from this window.",
            wholeGroup ? "Move entire group to Recycle Bin" : "Move selected photos to Recycle Bin", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _busy = true;
        UpdateActionButtons();
        var moved = new List<string>();
        var oldSelectedIndex = GroupsList.SelectedIndex;
        var wasSelectedGroup = ReferenceEquals(GroupsList.SelectedItem, group);
        try
        {
            foreach (var path in selectedPaths)
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                moved.Add(path);
            }
            if (moved.Count > 0) _trashUndo.Push(moved);
            foreach (var path in selectedPaths) group.Photos.Remove(path);
            if (group.Photos.Count < 2)
            {
                _groups.Remove(group);
                if (wasSelectedGroup && _groups.Count > 0) GroupsList.SelectedIndex = Math.Min(oldSelectedIndex, _groups.Count - 1);
                else if (_groups.Count == 0)
                {
                    GroupsList.SelectedIndex = -1;
                    ReviewEmptyPanel.Visibility = Visibility.Visible;
                    ReviewEmptyText.Text = "No duplicate groups left to review.";
                    PhotosItems.ItemsSource = null;
                }
            }
            else if (wasSelectedGroup)
            {
                GroupsList.SelectedIndex = -1;
                GroupsList.SelectedItem = group;
            }
            ScanStatus.Text = $"Moved {moved.Count:N0} photo(s) to the Recycle Bin. You chose every item moved.";
            UpdateUndoButton();
        }
        catch (Exception ex)
        {
            if (moved.Count > 0) _trashUndo.Push(moved);
            ScanStatus.Text = $"Moved {moved.Count:N0} photo(s); one selected photo could not be moved.";
            MessageBox.Show(this, $"Keeply moved {moved.Count:N0} photo(s) before it hit an error. The remaining selected photo was not skipped silently.\n\n{ex.Message}\n\nUse Undo removal to restore the photos already moved.", "Some photos could not be moved", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (moved.Count > 0)
            {
                var anchor = group.Photos.FirstOrDefault(path => !moved.Contains(path, StringComparer.OrdinalIgnoreCase));
                await RescanAfterUndoAsync(anchor);
            }
            UpdateUndoButton();
        }
        finally
        {
            _busy = false;
            UpdateActionButtons();
        }
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _trashUndo.Count == 0) return;
        _busy = true;
        UpdateActionButtons();
        var paths = _trashUndo.Peek();
        var restored = new List<string>();
        try
        {
            foreach (var path in paths.ToList())
            {
                RecycleBinRestorer.Restore(path);
                restored.Add(path);
            }
            _trashUndo.Pop();
            ScanStatus.Text = $"Restored {restored.Count:N0} photo(s) to their original folders.";
            await RescanAfterUndoAsync(restored.FirstOrDefault());
        }
        catch (Exception ex)
        {
            foreach (var path in restored) paths.Remove(path);
            if (paths.Count == 0) _trashUndo.Pop();
            ScanStatus.Text = $"Restored {restored.Count:N0} photo(s); some still need attention.";
            MessageBox.Show(this, $"Keeply restored {restored.Count:N0} photo(s). It couldn't restore the rest. Those items remain available in Undo removal if you resolve the issue.\n\n{ex.Message}", "Could not restore every photo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            UpdateActionButtons();
        }
    }

    private async Task RescanAfterUndoAsync(string? photoToReselect = null)
    {
        if (_rootFolder is null || !Directory.Exists(_rootFolder)) return;
        try
        {
            // Capture WPF state on the UI thread before moving scan work to a worker.
            var rootFolder = _rootFolder;
            var includeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
            var progress = new Progress<ScanProgressInfo>(_ => { });
            var result = await Task.Run(() => ScanFolder(rootFolder, includeSubfolders, progress, CancellationToken.None));
            _groups.Clear();
            foreach (var group in result.Groups) _groups.Add(group);
            var restoredGroup = -1;
            if (photoToReselect is not null)
                for (var i = 0; i < _groups.Count; i++)
                    if (_groups[i].Photos.Contains(photoToReselect, StringComparer.OrdinalIgnoreCase)) { restoredGroup = i; break; }
            GroupsList.SelectedIndex = _groups.Count == 0 ? -1 : restoredGroup >= 0 ? restoredGroup : 0;
            if (_groups.Count == 0)
            {
                PhotosItems.ItemsSource = null;
                ReviewEmptyPanel.Visibility = Visibility.Visible;
                ReviewEmptyText.Text = "No duplicate groups found after restore.";
            }
        }
        catch (Exception ex) { ScanStatus.Text = $"Photos restored, but refreshing groups failed: {ex.Message}"; }
    }

    private static BitmapSource DecodePreview(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 640;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void UpdateUndoButton() => UndoButton.IsEnabled = !_busy && _trashUndo.Count > 0;

    private enum DuplicateKind { Exact, Similar }
    private sealed record PhotoAnalysis(string Path, string Sha256, ulong? Fingerprint);
    private sealed record ScanProgressInfo(int Percent, string Message);
    private sealed record ScanResult(List<string> Files, List<DuplicateGroup> Groups, List<string> VisualFailures, int Reused);

    private sealed class DuplicateGroup
    {
        public DuplicateGroup(DuplicateKind kind, List<string> photos) { Kind = kind; Photos = photos; }
        public DuplicateKind Kind { get; }
        public List<string> Photos { get; }
        public int Number { get; set; }
        public string Title => $"{(Kind == DuplicateKind.Exact ? "Exact copies" : "Similar photos")} · Group {Number}";
        public string Subtitle => $"{Photos.Count} photos";
    }

    private sealed class PhotoCard : INotifyPropertyChanged
    {
        private BitmapSource? _preview;
        private string _details;
        private bool _isSelected;
        public PhotoCard(string path, string matchType)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            var info = new FileInfo(path);
            _details = $"{matchType} · {FormatBytes(info.Length)}";
        }
        public string Path { get; }
        public string Name { get; }
        public BitmapSource? Preview { get => _preview; set { _preview = value; OnPropertyChanged(); } }
        public string Details { get => _details; set { _details = value; OnPropertyChanged(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        private static string FormatBytes(long size) => size switch { >= 1_073_741_824 => $"{size / 1_073_741_824d:0.0} GB", >= 1_048_576 => $"{size / 1_048_576d:0.0} MB", _ => $"{size / 1024d:0} KB" };
    }

    private sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly byte[] _rank;
        public DisjointSet(int count) { _parent = Enumerable.Range(0, count).ToArray(); _rank = new byte[count]; }
        public int Find(int value) { if (_parent[value] != value) _parent[value] = Find(_parent[value]); return _parent[value]; }
        public void Join(int first, int second)
        {
            var a = Find(first); var b = Find(second);
            if (a == b) return;
            if (_rank[a] < _rank[b]) _parent[a] = b;
            else { _parent[b] = a; if (_rank[a] == _rank[b]) _rank[a]++; }
        }
    }
}
