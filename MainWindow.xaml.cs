using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Media.Animation;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;

namespace PhotoKeepKill;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };
    private readonly List<string> _photos = [];
    private readonly Stack<TriageAction> _actions = new();
    private readonly Stack<UndoEntry> _undoHistory = new();
    private readonly Dictionary<string, TriageAction> _completedActions = new(StringComparer.OrdinalIgnoreCase);
    private int _index = -1;
    private string? _sourceFolder;
    private string? _albumFolder;
    private string _queueFingerprint = "";
    private bool _loading;
    private bool _fullscreen;
    private bool _soundEnabled = true;
    private MotionMode _motionMode = MotionMode.Smooth;
    private readonly LocalLicenseStore _licenseStore;
    private readonly LicenseClient _licenseClient = new();
    private readonly ResumeSessionStore _resumeStore = new();
    private readonly QuickFolderStore _quickFolderStore = new();
    private readonly FavoritesStore _favoritesStore = new();
    private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private string? _favoriteFolder;
    private bool _favoritesStoreReady;
    private Dictionary<int, string> _quickFolders = new();
    private WindowStyle _savedStyle;
    private ResizeMode _savedResize;
    private WindowState _savedState;
    private bool _startupResumeChecked;

    public MainWindow()
    {
        InitializeComponent();
        _licenseStore = new LocalLicenseStore();
        UpdateAlbumFolderUi();
        UpdateFavoriteUi();
        UpdateSessionUi();
        UpdateLicenseUi();
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder of photos", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) await LoadFolderAsync(dialog.SelectedPath);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupResumeChecked) return;
        _startupResumeChecked = true;
        try
        {
            _favorites = _favoritesStore.Load();
            _favoriteFolder = _favoritesStore.LoadDestination();
            _favoritesStoreReady = true;
            UpdateFavoriteUi();
            UpdateSessionUi();
        }
        catch (Exception ex)
        {
            FavoritesButton.IsEnabled = FavoriteToggleButton.IsEnabled = false;
            ShowError("Could not load Favorites saved on this PC", ex);
        }
        try
        {
            _quickFolders = _quickFolderStore.Load();
            UpdateQuickFoldersUi();
        }
        catch (Exception ex) { ShowError("Could not load saved Quick Folder shortcuts", ex); }
        try
        {
            var recent = _resumeStore.LoadMostRecent();
            if (recent is not null) await LoadFolderAsync(recent.FolderPath);
        }
        catch (Exception ex) { ShowError("Could not check for saved sorting progress", ex); }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (PersistSession()) return;
        var closeAnyway = MessageBox.Show(this,
            "Keeply couldn't save this sorting session. If you close now, your latest progress may not be available next time. Close anyway?",
            "Progress not saved", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        e.Cancel = closeAnyway != MessageBoxResult.Yes;
    }

    private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            var folder = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0]);
            if (folder is not null) await LoadFolderAsync(folder);
        }
    }

    private async Task LoadFolderAsync(string folder)
    {
        try
        {
            if (_sourceFolder is not null && !PersistSession()) return;
            folder = Path.GetFullPath(folder);
            var files = Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.TopDirectoryOnly)
                .Where(p => Supported.Contains(Path.GetExtension(p)))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => Path.GetFileName(p), StringComparer.Ordinal).ToList();
            if (!_licenseStore.IsLicensed && files.Count > 100)
            {
                SetStatus("Free folders allow up to 100 supported photos. Get a lifetime license to open this folder.");
                PromptForLicense($"This folder contains {files.Count:N0} supported photos. Free use allows up to 100 photos per folder. Your current folder was left unchanged. Would you like to buy or enter a lifetime license?");
                return;
            }
            if (!_licenseStore.IsLicensed && files.Count > 0)
            {
                var hash = LocalLicenseStore.HashFolder(folder);
                var known = _licenseStore.FolderHashes.Contains(hash);
                if (!known && _licenseStore.FolderHashes.Count >= 3)
                {
                    SetStatus("Free folder allowance used. Get a lifetime license to open another folder.");
                    PromptForLicense("Free use includes 3 unique folders on this PC. Reopening an allowed folder is free. Your current folder was left unchanged. Would you like to buy or enter a lifetime license?");
                    return;
                }
                if (!known) { _licenseStore.RememberFolder(hash); UpdateLicenseUi(); }
            }

            ResumeSession? saved;
            try { saved = _resumeStore.Load(folder); }
            catch (Exception ex)
            {
                var fresh = MessageBox.Show(this,
                    $"Keeply couldn't read the saved progress for this folder. Your photos are unchanged. Start a fresh sorting session?\n\n{ex.Message}",
                    "Saved progress unavailable", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (fresh != MessageBoxResult.Yes) return;
                _resumeStore.Delete(folder);
                saved = null;
            }
            if (saved is not null && saved.Actions.Any(action => !Enum.IsDefined(typeof(ActionKind), action.Kind)))
            {
                var fresh = MessageBox.Show(this,
                    "The saved session contains an unsupported action. Your photos are unchanged. Start a fresh sorting session for this folder?",
                    "Saved progress unavailable", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (fresh != MessageBoxResult.Yes) return;
                _resumeStore.Delete(folder);
                saved = null;
            }
            var choice = saved is not null && (saved.Actions.Count > 0 || saved.CurrentIndex > 0)
                ? ShowResumePrompt(saved)
                : ResumeChoice.Resume;
            if (choice == ResumeChoice.Cancel) { SetStatus("Kept your current sorting session open."); return; }

            _photos.Clear();
            _actions.Clear();
            _undoHistory.Clear();
            _completedActions.Clear();
            _albumFolder = null;
            _sourceFolder = folder;
            if (saved is not null && choice == ResumeChoice.Resume)
            {
                var queue = files.Concat(saved.Actions.Select(action => action.SourcePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .ToList();
                var currentPhoto = saved.CurrentPhotoPath;
                var index = currentPhoto is null ? -1 : queue.FindIndex(path => StringComparer.OrdinalIgnoreCase.Equals(path, currentPhoto));
                if (index < 0) index = Math.Clamp(saved.CurrentIndex, 0, queue.Count);
                _photos.AddRange(queue);
                _queueFingerprint = ComputeQueueFingerprint(queue);
                foreach (var action in saved.Actions)
                {
                    var restoredAction = new TriageAction((ActionKind)action.Kind, action.SourcePath, action.TargetPath, Math.Max(0, action.Index), action.WasFavorite);
                    _actions.Push(restoredAction);
                    _undoHistory.Push(UndoEntry.ForTriage(restoredAction));
                    _completedActions[restoredAction.SourcePath] = restoredAction;
                }
                _albumFolder = saved.AlbumFolder;
                if (queue.Count == 0) _index = -1;
                else if (index >= queue.Count) _index = FindNextPendingIndex(0);
                else _index = _completedActions.ContainsKey(queue[Math.Max(0, index)])
                    ? FindNextPendingIndex(Math.Max(0, index) + 1)
                    : Math.Clamp(index, 0, queue.Count - 1);
                var queueChanged = !string.IsNullOrWhiteSpace(saved.QueueFingerprint) &&
                    !StringComparer.Ordinal.Equals(saved.QueueFingerprint, _queueFingerprint);
                SetStatus($"Resumed: {_actions.Count:N0} of {_photos.Count:N0} completed" +
                    (queueChanged ? " · folder contents changed since the last session" : ""));
            }
            else
            {
                if (saved is not null) _resumeStore.Delete(folder);
                _photos.AddRange(files);
                _queueFingerprint = ComputeQueueFingerprint(_photos);
                _index = files.Count == 0 ? -1 : 0;
                SetStatus(files.Count == 0 ? "No supported photos in this folder." : "Started a fresh sorting session.");
            }
            FolderText.Text = folder; UpdateAlbumFolderUi();
            PersistSession();
            await ShowCurrentAsync();
        }
        catch (Exception ex) { ShowError("Could not open this folder", ex); }
    }

    private ResumeChoice ShowResumePrompt(ResumeSession session)
    {
        var total = session.QueueCount;
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(session.FolderPath));
        var dialog = new Window
        {
            Owner = this, Title = "Continue sorting?", Width = 460, Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Pick up where you left off", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"{folderName}\n{session.Actions.Count:N0} of {total:N0} photos completed. Your progress is saved on this PC.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var resume = new System.Windows.Controls.Button { Content = "Resume", MinWidth = 95, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var restart = new System.Windows.Controls.Button { Content = "Start over", MinWidth = 95, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        buttons.Children.Add(resume); buttons.Children.Add(restart); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
        var result = ResumeChoice.Cancel;
        resume.Click += (_, _) => { result = ResumeChoice.Resume; dialog.Close(); };
        restart.Click += (_, _) => { result = ResumeChoice.StartOver; dialog.Close(); };
        dialog.ShowDialog();
        return result;
    }

    private async Task ShowCurrentAsync()
    {
        _loading = true;
        ActionBadge.BeginAnimation(OpacityProperty, null);
        ActionBadge.Opacity = 0;
        ActionBadge.Visibility = Visibility.Collapsed;
        try
        {
            var hasPhoto = _index >= 0 && _index < _photos.Count;
            UpdateSessionUi();
            UpdateFavoriteUi();
            var noFolder = _sourceFolder is null;
            var emptyFolder = !noFolder && _photos.Count == 0 && _actions.Count == 0;
            EmptyPanel.Visibility = noFolder || emptyFolder ? Visibility.Visible : Visibility.Collapsed;
            EmptyHeading.Text = emptyFolder ? "No supported photos here." : "A lighter photo folder starts here.";
            EmptyDescription.Text = emptyFolder
                ? "Choose a folder with JPG, JPEG, PNG, WebP, BMP, GIF, or TIFF photos."
                : "Choose a folder or drop it anywhere in this window. Your photos stay on this PC.";
            EndPanel.Visibility = _sourceFolder is not null && _photos.Count > 0 && !hasPhoto ? Visibility.Visible : Visibility.Collapsed;
            var photoExists = hasPhoto && File.Exists(_photos[_index]);
            PhotoImage.Visibility = photoExists ? Visibility.Visible : Visibility.Collapsed;
            UnavailablePanel.Visibility = hasPhoto && !photoExists ? Visibility.Visible : Visibility.Collapsed;
            FileNameText.Visibility = hasPhoto ? Visibility.Visible : Visibility.Collapsed;
            if (!hasPhoto)
            {
                ResetPhotoAnimation();
                PhotoImage.Source = null; FileNameText.Text = "";
                return;
            }
            var path = _photos[_index];
            FileNameText.Text = Path.GetFileName(path);
            if (!photoExists)
            {
                ResetPhotoAnimation();
                PhotoImage.Source = null;
                UnavailableDescription.Text = _completedActions.TryGetValue(path, out var completed) && completed.Kind == ActionKind.Trash
                    ? "This photo is in the Windows Recycle Bin. Use U to undo the latest action."
                    : "The file is missing from this folder. Check the folder or reopen it after restoring the file.";
                SetStatus(UnavailableDescription.Text);
                UpdateSessionUi();
                return;
            }
            var nextPhoto = await DecodePhotoAsync(path);
            PhotoImage.BeginAnimation(OpacityProperty, null);
            PhotoImage.RenderTransform = System.Windows.Media.Transform.Identity;
            PhotoImage.Source = nextPhoto;
            PhotoImage.Opacity = 0;
            var fadeInMs = _motionMode switch { MotionMode.Smooth => 300, MotionMode.Standard => 180, _ => 100 };
            PhotoImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeInMs))
            {
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = EasingMode.EaseOut }
            });
            if (_completedActions.TryGetValue(path, out var priorAction))
                SetStatus(priorAction.Kind switch
                {
                    ActionKind.Keep => "Already kept. U reverses the most recent action.",
                    ActionKind.Album => "Already added to the album. U reverses the most recent action.",
                    ActionKind.Favorite => "Already copied to Favorites. U reverses the most recent action.",
                    _ => "Already processed. U reverses the most recent action."
                });
        }
        catch (Exception ex)
        {
            PhotoImage.Source = null;
            SetStatus($"Could not preview {Path.GetFileName(_photos[_index])}: {ex.Message}");
        }
        finally { _loading = false; }
    }

    private static Task<BitmapImage> DecodePhotoAsync(string path) => Task.Run(() =>
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 2560;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    });

    private async Task ProcessAsync(ActionKind kind, string? quickFolderDestination = null, int? quickFolderShortcut = null)
    {
        if (_loading || _index < 0 || _index >= _photos.Count) return;
        if (kind == ActionKind.Favorite && !_favoritesStoreReady)
        {
            SetStatus("Favorites couldn't be loaded on this PC, so Keeply can't save this favorite.");
            return;
        }
        var photo = _photos[_index];
        var wasFavorite = _favorites.Contains(photo);
        if (_completedActions.ContainsKey(photo))
        {
            SetStatus("This photo was already processed. Use U to undo the most recent action first.");
            return;
        }
        if (!File.Exists(photo))
        {
            SetStatus("This photo is unavailable in its original folder. Restore it or choose another photo.");
            return;
        }
        _loading = true;
        try
        {
            string? target = null;
            if (kind == ActionKind.Trash)
            {
                FileSystem.DeleteFile(photo, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else if (kind is ActionKind.Album or ActionKind.Favorite)
            {
                string destination;
                if (kind == ActionKind.Favorite)
                {
                    if (!EnsureFavoriteFolder()) return;
                    destination = _favoriteFolder!;
                }
                else if (quickFolderDestination is not null)
                {
                    if (!Directory.Exists(quickFolderDestination))
                    {
                        SetStatus("That Quick Folder destination is unavailable. Choose a new folder for this shortcut.");
                        return;
                    }
                    destination = quickFolderDestination;
                }
                else
                {
                    if (!EnsureAlbumFolder()) return;
                    destination = _albumFolder!;
                }
                var albumTarget = UniquePath(destination, Path.GetFileName(photo));
                target = albumTarget;
                await Task.Run(() => File.Copy(photo, albumTarget));
                if (kind == ActionKind.Favorite && !SetFavoriteState(photo, true, recordUndo: false, status: "", showFeedback: false))
                {
                    File.Delete(albumTarget);
                    return;
                }
            }
            var action = new TriageAction(kind, photo, target, _index, wasFavorite);
            try { _resumeStore.RecordAction(_sourceFolder!, ToSavedAction(action)); }
            catch (Exception ex) { ShowError("Photo processed, but its resume history could not be saved", ex); }
            _actions.Push(action);
            _undoHistory.Push(UndoEntry.ForTriage(action));
            _completedActions[photo] = action;
            _index = FindNextPendingIndex(_index + 1);
            SetStatus("");
            PersistSession();
            PlayActionSound(kind);
            await AnimateActionAsync(kind);
            await ShowCurrentAsync();
            if (quickFolderShortcut.HasValue && quickFolderDestination is not null)
                SetStatus($"Copied to {DisplayFolderName(quickFolderDestination)} using shortcut {quickFolderShortcut.Value}.");
            else if (kind == ActionKind.Favorite)
                SetStatus($"Favorited and copied to {DisplayFolderName(_favoriteFolder!)}.");
        }
        catch (Exception ex) { ShowError(kind == ActionKind.Trash ? "Could not move this photo to the Recycle Bin" : "Could not process this photo", ex); }
        finally { _loading = false; }
    }

    private int FindNextPendingIndex(int startIndex)
    {
        for (var i = Math.Max(0, startIndex); i < _photos.Count; i++)
            if (!_completedActions.ContainsKey(_photos[i])) return i;
        for (var i = 0; i < Math.Min(Math.Max(0, startIndex), _photos.Count); i++)
            if (!_completedActions.ContainsKey(_photos[i])) return i;
        return _photos.Count;
    }

    private bool EnsureAlbumFolder()
    {
        if (_albumFolder is not null && Directory.Exists(_albumFolder) && !IsFavoriteFolder(_albumFolder)) return true;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose or create an album folder", UseDescriptionForTitle = true, ShowNewFolderButton = true, SelectedPath = _albumFolder ?? "" };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
        var selected = Path.GetFullPath(dialog.SelectedPath);
        if (IsFavoriteFolder(selected))
        {
            MessageBox.Show(this, "Choose a different folder from the Favorites destination used by F.", "Separate Album folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        _albumFolder = selected;
        if (!Directory.Exists(_albumFolder)) { SetStatus("The album folder no longer exists. Choose another folder."); return false; }
        UpdateAlbumFolderUi();
        return true;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (_undoHistory.Count == 0) { SetStatus("Nothing to undo."); return; }
        var undoEntry = _undoHistory.Peek();
        if (undoEntry.Kind == UndoKind.Favorite)
        {
            if (SetFavoriteState(undoEntry.Path, undoEntry.WasFavorite, recordUndo: false,
                    undoEntry.WasFavorite ? "Undo complete: photo returned to Favorites." : "Undo complete: photo removed from Favorites."))
                _undoHistory.Pop();
            UpdateSessionUi();
            return;
        }
        if (_actions.Count == 0) { SetStatus("There is no sorting action available to undo."); return; }
        _loading = true;
        var action = _actions.Peek();
        try
        {
            if (action.Kind == ActionKind.Favorite &&
                !SetFavoriteState(action.SourcePath, action.WasFavorite, recordUndo: false,
                    status: "Undo complete: favorite copy removed and photo returned to the queue.", showFeedback: false))
                return;
            if ((action.Kind is ActionKind.Album or ActionKind.Favorite) && action.TargetPath is not null && File.Exists(action.TargetPath)) File.Delete(action.TargetPath);
            if (action.Kind == ActionKind.Trash) RecycleBinRestorer.Restore(action.SourcePath);
            try { _resumeStore.RecordUndo(_sourceFolder!, action.SourcePath); }
            catch (Exception ex) { ShowError("Photo restored, but its resume history could not be updated", ex); }
            _actions.Pop();
            _undoHistory.Pop();
            _completedActions.Remove(action.SourcePath);
            _index = Math.Clamp(action.Index, 0, Math.Max(0, _photos.Count - 1));
            SetStatus(action.Kind switch
            {
                ActionKind.Keep => "Undo complete: photo returned to the queue.",
                ActionKind.Trash => "Undo complete: photo restored from the Recycle Bin.",
                ActionKind.Favorite => "Undo complete: favorite copy removed and photo returned to the queue.",
                _ => "Undo complete: album copy removed."
            });
            PersistSession();
            await ShowCurrentAsync();
        }
        catch (Exception ex) { ShowError("Could not undo the last action", ex); }
        finally { _loading = false; UpdateSessionUi(); }
    }

    private static string UniquePath(string folder, string filename)
    {
        var path = Path.Combine(folder, filename); if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(filename); var ext = Path.GetExtension(filename);
        for (var n = 1; ; n++) { path = Path.Combine(folder, $"{stem} ({n}){ext}"); if (!File.Exists(path) && !Directory.Exists(path)) return path; }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) => await NavigateAsync(-1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await NavigateAsync(1);
    private async Task NavigateAsync(int delta)
    {
        if (_loading || _photos.Count == 0) return;
        var nextIndex = _index + delta;
        if (nextIndex < 0 || nextIndex >= _photos.Count) return;
        _index = nextIndex; SetStatus(""); PersistSession(); await ShowCurrentAsync();
    }
    private async void Keep_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ActionKind.Keep);
    private async void Trash_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ActionKind.Trash);
    private async void Album_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ActionKind.Album);

    private async Task RunQuickFolderShortcutAsync(int slot)
    {
        if (_loading || _index < 0 || _index >= _photos.Count) return;
        var photo = _photos[_index];
        if (_completedActions.ContainsKey(photo))
        {
            SetStatus("This photo was already processed. Use U to undo the most recent action first.");
            return;
        }

        string destination;
        if (_quickFolders.TryGetValue(slot, out var assigned) && Directory.Exists(assigned))
        {
            destination = assigned;
        }
        else
        {
            if (_quickFolders.ContainsKey(slot))
                MessageBox.Show(this, $"The folder assigned to shortcut {slot} is unavailable. Choose a replacement folder.", "Quick Folder unavailable", MessageBoxButton.OK, MessageBoxImage.Information);

            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Choose or create the destination for shortcut {slot}",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = _quickFolders.TryGetValue(slot, out var previous) && Directory.Exists(previous) ? previous : ""
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            destination = Path.GetFullPath(dialog.SelectedPath);
            var updated = new Dictionary<int, string>(_quickFolders) { [slot] = destination };
            try { _quickFolderStore.Save(updated); }
            catch (Exception ex) { ShowError($"Could not save shortcut {slot}", ex); return; }
            _quickFolders = updated;
            UpdateQuickFoldersUi();
        }

        await ProcessAsync(ActionKind.Album, destination, slot);
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
        if (Keyboard.Modifiers == ModifierKeys.None && TryGetShortcutNumber(e.Key, out var slot))
        {
            await RunQuickFolderShortcutAsync(slot);
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.K: await ProcessAsync(ActionKind.Keep); break;
            case Key.T: await ProcessAsync(ActionKind.Trash); break;
            case Key.A: await ProcessAsync(ActionKind.Album); break;
            case Key.F: await ProcessAsync(ActionKind.Favorite); break;
            case Key.U: Undo_Click(this, new RoutedEventArgs()); break;
            case Key.Left: await NavigateAsync(-1); break;
            case Key.Right: await NavigateAsync(1); break;
            default: return;
        }
        e.Handled = true;
    }

    private static bool TryGetShortcutNumber(Key key, out int slot)
    {
        slot = key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            Key.D9 or Key.NumPad9 => 9,
            _ => 0
        };
        return slot != 0;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void DuplicateFinder_Click(object sender, RoutedEventArgs e)
    {
        var window = new DuplicateFinderWindow(_licenseStore, () => License_Click(this, new RoutedEventArgs())) { Owner = this };
        window.ShowDialog();
    }
    private void BulkCompress_Click(object sender, RoutedEventArgs e)
    {
        var window = new BulkCompressWindow { Owner = this };
        window.ShowDialog();
    }
    private void ConvertImages_Click(object sender, RoutedEventArgs e)
    {
        var window = new ImageConverterWindow { Owner = this };
        window.ShowDialog();
    }
    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        if (!_favoritesStoreReady) return;
        var window = new FavoritesWindow(_favorites, path => SetFavoriteState(path, false, recordUndo: true),
            _favoriteFolder, SaveFavoriteDestination) { Owner = this };
        window.ShowDialog();
    }
    private async void Favorite_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ActionKind.Favorite);
    private bool SetFavoriteState(string path, bool isFavorite, bool recordUndo, string? status = null, bool showFeedback = true)
    {
        if (!_favoritesStoreReady) return false;
        var normalizedPath = Path.GetFullPath(path);
        var wasFavorite = _favorites.Contains(normalizedPath);
        if (wasFavorite == isFavorite) return true;

        var updated = new HashSet<string>(_favorites, StringComparer.OrdinalIgnoreCase);
        if (isFavorite) updated.Add(normalizedPath);
        else updated.Remove(normalizedPath);
        try { _favoritesStore.Save(updated); }
        catch (Exception ex) { ShowError("Could not save Favorites on this PC", ex); return false; }

        _favorites = updated;
        if (recordUndo) _undoHistory.Push(UndoEntry.ForFavorite(normalizedPath, wasFavorite));
        UpdateFavoriteUi();
        UpdateSessionUi();
        SetStatus(status ?? (isFavorite ? "Added to Favorites on this PC. Press F again to remove it." : "Removed from Favorites."));
        if (showFeedback && _index >= 0 && _index < _photos.Count &&
            StringComparer.OrdinalIgnoreCase.Equals(_photos[_index], normalizedPath))
        {
            if (_soundEnabled) SoundEffects.PlayFavorite();
            _ = ShowFavoriteFeedbackAsync(isFavorite);
        }
        return true;
    }

    private bool EnsureFavoriteFolder()
    {
        if (_favoriteFolder is not null && Directory.Exists(_favoriteFolder) && !IsAlbumFolder(_favoriteFolder)) return true;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a Favorites folder, separate from your Album folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = _favoriteFolder ?? ""
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
        var selected = Path.GetFullPath(dialog.SelectedPath);
        if (IsAlbumFolder(selected))
        {
            MessageBox.Show(this, "Choose a different folder from the Album destination used by A.", "Separate Favorites folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!Directory.Exists(selected))
        {
            SetStatus("The Favorites folder is unavailable. Choose another folder.");
            return false;
        }
        try { _favoritesStore.SaveDestination(selected); }
        catch (Exception ex) { ShowError("Could not save the Favorites folder on this PC", ex); return false; }
        _favoriteFolder = selected;
        return true;
    }

    private bool SaveFavoriteDestination(string path)
    {
        var selected = Path.GetFullPath(path);
        if (IsAlbumFolder(selected))
        {
            MessageBox.Show(this, "Choose a different folder from the Album destination used by A.", "Separate Favorites folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        try { _favoritesStore.SaveDestination(selected); }
        catch (Exception ex) { ShowError("Could not save the Favorites folder on this PC", ex); return false; }
        _favoriteFolder = selected;
        return true;
    }

    private bool IsAlbumFolder(string path) => !string.IsNullOrWhiteSpace(_albumFolder) &&
        StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path), Path.GetFullPath(_albumFolder));
    private bool IsFavoriteFolder(string path) => !string.IsNullOrWhiteSpace(_favoriteFolder) &&
        StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path), Path.GetFullPath(_favoriteFolder));
    private async Task ShowFavoriteFeedbackAsync(bool isFavorite)
    {
        FavoriteFeedback.BeginAnimation(OpacityProperty, null);
        FavoriteFeedbackIcon.Text = isFavorite ? "★" : "☆";
        FavoriteFeedbackText.Text = isFavorite ? "Added to Favorites" : "Removed from Favorites";
        var accent = isFavorite
            ? System.Windows.Media.Color.FromRgb(139, 224, 188)
            : System.Windows.Media.Color.FromRgb(205, 211, 215);
        FavoriteFeedbackIcon.Foreground = new System.Windows.Media.SolidColorBrush(accent);
        FavoriteFeedback.BorderBrush = new System.Windows.Media.SolidColorBrush(accent);
        FavoriteFeedback.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(230, 27, 51, 41));
        FavoriteFeedback.Opacity = 0;
        FavoriteFeedback.Visibility = Visibility.Visible;
        FavoriteFeedback.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
        await Task.Delay(760);
        if (!IsLoaded) return;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(230));
        fadeOut.Completed += (_, _) => FavoriteFeedback.Visibility = Visibility.Collapsed;
        FavoriteFeedback.BeginAnimation(OpacityProperty, fadeOut);
    }
    private void SoundToggle_Click(object sender, RoutedEventArgs e)
    {
        _soundEnabled = !_soundEnabled;
        SoundToggleButton.Content = _soundEnabled ? "Sound: On" : "Sound: Off";
        SetStatus(_soundEnabled ? "Action sounds enabled." : "Action sounds muted.");
    }
    private void MotionMode_Click(object sender, RoutedEventArgs e)
    {
        _motionMode = _motionMode switch
        {
            MotionMode.Smooth => MotionMode.Standard,
            MotionMode.Standard => MotionMode.Reduced,
            _ => MotionMode.Smooth
        };
        MotionModeButton.Content = $"Motion: {_motionMode}";
        SetStatus($"Motion set to {_motionMode.ToString().ToLowerInvariant()}.");
    }
    private void ChangeAlbum_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose or create an album folder", UseDescriptionForTitle = true, ShowNewFolderButton = true, SelectedPath = _albumFolder ?? "" };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var selected = Path.GetFullPath(dialog.SelectedPath);
            if (IsFavoriteFolder(selected))
            {
                MessageBox.Show(this, "Choose a different folder from the Favorites destination used by F.", "Separate Album folder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _albumFolder = selected;
            UpdateAlbumFolderUi();
            SetStatus("Album destination changed.");
            PersistSession();
        }
    }

    private void QuickFolders_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new QuickFolderManagerWindow(_quickFolders) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var updated = dialog.Assignments.ToDictionary(pair => pair.Key, pair => pair.Value);
        try { _quickFolderStore.Save(updated); }
        catch (Exception ex) { ShowError("Could not save Quick Folder shortcuts", ex); return; }
        _quickFolders = updated;
        UpdateQuickFoldersUi();
        SetStatus("Quick Folder shortcuts saved on this PC.");
    }

    private void UpdateQuickFoldersUi()
    {
        QuickFoldersButton.Content = $"Quick folders · {_quickFolders.Count}/9";
        QuickFoldersHint.Text = "Press 1–9 to copy to the assigned folder · A uses the separate Album folder";
        QuickFoldersButton.ToolTip = string.Join(Environment.NewLine,
            Enumerable.Range(1, 9).Select(slot => _quickFolders.TryGetValue(slot, out var path)
                ? $"{slot} = {DisplayFolderName(path)}{(Directory.Exists(path) ? "" : " (unavailable)")}"
                : $"{slot} = not assigned"));
    }

    private static string DisplayFolderName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void UpdateLicenseUi()
    {
        LicenseButton.Content = _licenseStore.IsLicensed ? "Lifetime · Activated" : $"Free · {_licenseStore.FolderHashes.Count}/3 folders";
    }

    private void PromptForLicense(string message)
    {
        if (MessageBox.Show(this, message, "Lifetime license needed", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            License_Click(this, new RoutedEventArgs());
    }

    private async void License_Click(object sender, RoutedEventArgs e)
    {
        if (_licenseStore.IsLicensed)
        {
            var choice = MessageBox.Show(this, "This PC is activated for offline lifetime use. Choose Yes to deactivate online and transfer this activation, No to view the purchase page, or Cancel to close.", "Lifetime license", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
            if (choice == MessageBoxResult.No) { OpenCheckout(); return; }
            if (choice != MessageBoxResult.Yes) return;
            try { await _licenseClient.DeactivateAsync(_licenseStore.ReadLicenseKey()!, _licenseStore.License!.InstanceId); _licenseStore.ClearLicense(); UpdateLicenseUi(); SetStatus("This PC was deactivated. Free limits now apply."); }
            catch (Exception ex) { ShowError("Could not deactivate online", ex); }
            return;
        }

        var dialog = new Window { Owner = this, Title = "Activate Keeply", Width = 500, Height = 270, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Lifetime license · $6 one-time · up to 3 PCs", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 9) });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"Free includes 3 unique folders on this PC and up to 100 supported photos per folder. Used: {_licenseStore.FolderHashes.Count}/3. Activated PCs work offline indefinitely.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        var keyBox = new System.Windows.Controls.TextBox { Height = 36, FontSize = 15, Padding = new Thickness(7), ToolTip = "Paste the license key from your purchase" };
        panel.Children.Add(keyBox);
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var buy = new System.Windows.Controls.Button { Content = "Buy lifetime · $6", Margin = new Thickness(0, 0, 8, 0) };
        var close = new System.Windows.Controls.Button { Content = "Close", Margin = new Thickness(0, 0, 8, 0) };
        var activate = new System.Windows.Controls.Button { Content = "Activate" };
        buttons.Children.Add(buy); buttons.Children.Add(close); buttons.Children.Add(activate); panel.Children.Add(buttons); dialog.Content = panel;
        close.Click += (_, _) => dialog.Close(); buy.Click += (_, _) => OpenCheckout();
        activate.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(keyBox.Text)) { MessageBox.Show(dialog, "Paste your license key first.", "Activate", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            activate.IsEnabled = false;
            try
            {
                var key = keyBox.Text.Trim();
                var instanceId = await _licenseClient.ActivateAsync(key, _licenseStore.InstallName);
                _licenseStore.Activate(key, instanceId); UpdateLicenseUi(); SetStatus("Lifetime license activated. This PC can now work offline."); dialog.Close();
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Activation failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { activate.IsEnabled = true; }
        };
        dialog.ShowDialog();
    }

    private void OpenCheckout()
    {
        if (string.IsNullOrWhiteSpace(_licenseClient.CheckoutUrl))
        {
            MessageBox.Show(this, "The purchase page is not configured yet. Add your hosted Creem Checkout URL to licensing.json when your site is deployed.", "Purchase setup needed", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo(_licenseClient.CheckoutUrl) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Could not open the checkout page", ex); }
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _savedStyle = WindowStyle; _savedResize = ResizeMode; _savedState = WindowState;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; _fullscreen = true;
        }
        else { WindowStyle = _savedStyle; ResizeMode = _savedResize; WindowState = _savedState; _fullscreen = false; }
    }
    private void SetStatus(string message) => StatusText.Text = message;
    private void ShowError(string title, Exception ex) { SetStatus($"{title}: {ex.Message}"); MessageBox.Show(this, $"{title}.\n\n{ex.Message}", "Keeply", MessageBoxButton.OK, MessageBoxImage.Warning); }

    private bool PersistSession()
    {
        if (_sourceFolder is null) return true;
        try
        {
            var currentPath = _index >= 0 && _index < _photos.Count ? _photos[_index] : null;
            var session = new ResumeSession
            {
                FolderPath = _sourceFolder,
                CurrentPhotoPath = currentPath,
                CurrentIndex = Math.Clamp(_index, 0, _photos.Count),
                AlbumFolder = _albumFolder,
                QueueCount = _photos.Count,
                QueueFingerprint = _queueFingerprint
            };
            _resumeStore.Save(session);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not save sorting progress on this PC", ex);
            return false;
        }
    }

    private static SavedTriageAction ToSavedAction(TriageAction action) => new()
    {
        Kind = (int)action.Kind,
        SourcePath = action.SourcePath,
        TargetPath = action.TargetPath,
        Index = action.Index,
        WasFavorite = action.WasFavorite
    };

    private static string ComputeQueueFingerprint(IEnumerable<string> paths)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var path in paths)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant() + "\0");
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void UpdateAlbumFolderUi()
    {
        if (string.IsNullOrWhiteSpace(_albumFolder))
        {
            AlbumFolderButton.Content = "Album: Choose";
            AlbumFolderButton.ToolTip = "Choose or create the album destination";
            return;
        }
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(_albumFolder));
        AlbumFolderButton.Content = $"Album: {(string.IsNullOrEmpty(name) ? _albumFolder : name)}";
        AlbumFolderButton.ToolTip = _albumFolder;
    }

    private void UpdateFavoriteUi()
    {
        FavoritesButton.Content = $"★ Favorites · {_favorites.Count:N0}";
        FavoritesButton.IsEnabled = _favoritesStoreReady;

        var hasPhoto = _index >= 0 && _index < _photos.Count;
        FavoriteToggleButton.Visibility = hasPhoto ? Visibility.Visible : Visibility.Collapsed;
        if (!hasPhoto) return;

        var path = _photos[_index];
        var isFavorite = _favorites.Contains(path);
        FavoriteToggleButton.Content = isFavorite ? "★ Favorite & next · F" : "☆ Favorite & next · F";
        FavoriteToggleButton.IsEnabled = _favoritesStoreReady && File.Exists(path) && !_completedActions.ContainsKey(path);
        FavoriteToggleButton.ToolTip = "Copy this photo to the separate Favorites folder, save it to the Favorites list, then continue. The original stays in place.";
        FavoriteToggleButton.Background = isFavorite
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(235, 35, 72, 54))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(217, 28, 34, 37));
    }

    private string FormatActionCounts()
    {
        var kept = _actions.Count(action => action.Kind == ActionKind.Keep);
        var trashed = _actions.Count(action => action.Kind == ActionKind.Trash);
        var album = _actions.Count(action => action.Kind == ActionKind.Album);
        var favorites = _actions.Count(action => action.Kind == ActionKind.Favorite);
        return $"Keep {kept}   ·   Trash {trashed}   ·   Album {album}   ·   Favorites {favorites}";
    }

    private void UpdateSessionUi()
    {
        var hasPhoto = _index >= 0 && _index < _photos.Count;
        var total = _photos.Count;
        var current = hasPhoto ? _index + 1 : total;
        ProgressText.Text = hasPhoto ? $"{current:N0} / {total:N0}" : $"{total:N0} / {total:N0}";
        QueueProgressBar.Maximum = Math.Max(1, total);
        QueueProgressBar.Value = total == 0 ? 0 : current;
        PreviousButton.IsEnabled = _photos.Count > 0 && _index > 0;
        NextButton.IsEnabled = hasPhoto && _index < _photos.Count - 1;
        var canProcess = hasPhoto && File.Exists(_photos[_index]) && !_completedActions.ContainsKey(_photos[_index]);
        KeepButton.IsEnabled = AlbumButton.IsEnabled = TrashButton.IsEnabled = canProcess;
        FavoriteToggleButton.IsEnabled = canProcess && _favoritesStoreReady;
        UndoButton.IsEnabled = _undoHistory.Count > 0;
        ActionCountsText.Text = _sourceFolder is null ? "" : FormatActionCounts();
        FinishStatsText.Text = FormatActionCounts();
    }

    private void PlayActionSound(ActionKind kind)
    {
        if (!_soundEnabled) return;
        switch (kind)
        {
            case ActionKind.Keep: SoundEffects.PlayKeep(); break;
            case ActionKind.Trash: SoundEffects.PlayTrash(); break;
            case ActionKind.Album: SoundEffects.PlayAlbum(); break;
            case ActionKind.Favorite: SoundEffects.PlayFavorite(); break;
        }
    }

    private async Task AnimateActionAsync(ActionKind kind)
    {
        if (PhotoImage.Source is null) return;
        ActionBadgeText.Text = kind switch
        {
            ActionKind.Keep => "KEPT",
            ActionKind.Trash => "TRASHED",
            ActionKind.Album => "ADDED TO ALBUM",
            ActionKind.Favorite => "★ FAVORITED · COPIED",
            _ => ""
        };
        ActionBadge.BorderBrush = kind switch
        {
            ActionKind.Keep => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(126, 218, 166)),
            ActionKind.Trash => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 142, 142)),
            ActionKind.Favorite => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 224, 188)),
            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(137, 183, 230))
        };
        ActionBadge.Visibility = Visibility.Visible;
        ActionBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100)));

        var translation = new System.Windows.Media.TranslateTransform();
        PhotoImage.RenderTransform = translation;
        var duration = _motionMode switch
        {
            MotionMode.Smooth => TimeSpan.FromMilliseconds(620),
            MotionMode.Standard => TimeSpan.FromMilliseconds(360),
            _ => TimeSpan.FromMilliseconds(120)
        };
        var easing = new System.Windows.Media.Animation.SineEase { EasingMode = EasingMode.EaseInOut };
        var travelX = _motionMode == MotionMode.Reduced ? 0 : PhotoImage.ActualWidth + 120;
        var travelY = _motionMode == MotionMode.Reduced ? 0 : PhotoImage.ActualHeight + 120;
        var (x, y) = kind switch
        {
            ActionKind.Keep => (travelX, -18d),
            ActionKind.Trash => (-travelX, 18d),
            _ => (0d, -travelY)
        };
        translation.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(0, x, duration) { EasingFunction = easing });
        translation.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(0, y, duration) { EasingFunction = easing });
        var fadeDelay = _motionMode == MotionMode.Reduced ? TimeSpan.Zero : TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.7);
        var fadeDuration = duration - fadeDelay;
        PhotoImage.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, fadeDuration)
        {
            BeginTime = fadeDelay,
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = EasingMode.EaseIn }
        });
        await Task.Delay(duration + TimeSpan.FromMilliseconds(20));
    }

    private void ResetPhotoAnimation()
    {
        PhotoImage.BeginAnimation(OpacityProperty, null);
        PhotoImage.Opacity = 1;
        PhotoImage.RenderTransform = System.Windows.Media.Transform.Identity;
        ActionBadge.BeginAnimation(OpacityProperty, null);
        ActionBadge.Opacity = 0;
        ActionBadge.Visibility = Visibility.Collapsed;
    }

    private enum ActionKind { Keep, Trash, Album, Favorite }
    private enum UndoKind { Triage, Favorite }
    private enum ResumeChoice { Resume, StartOver, Cancel }
    private enum MotionMode { Smooth, Standard, Reduced }
    private sealed record TriageAction(ActionKind Kind, string SourcePath, string? TargetPath, int Index, bool WasFavorite = false);
    private sealed record UndoEntry(UndoKind Kind, TriageAction? TriageAction, string Path, bool WasFavorite)
    {
        public static UndoEntry ForTriage(TriageAction action) => new(UndoKind.Triage, action, action.SourcePath, false);
        public static UndoEntry ForFavorite(string path, bool wasFavorite) => new(UndoKind.Favorite, null, path, wasFavorite);
    }
}
