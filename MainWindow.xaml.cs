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
    private int _index = -1;
    private string? _sourceFolder;
    private string? _albumFolder;
    private bool _loading;
    private bool _fullscreen;
    private bool _soundEnabled = true;
    private MotionMode _motionMode = MotionMode.Smooth;
    private readonly LocalLicenseStore _licenseStore;
    private readonly LicenseClient _licenseClient = new();
    private WindowStyle _savedStyle;
    private ResizeMode _savedResize;
    private WindowState _savedState;

    public MainWindow()
    {
        InitializeComponent();
        _licenseStore = new LocalLicenseStore();
        UpdateAlbumFolderUi();
        UpdateSessionUi();
        UpdateLicenseUi();
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder of photos", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) await LoadFolderAsync(dialog.SelectedPath);
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
            _photos.Clear(); _photos.AddRange(files); _actions.Clear(); _albumFolder = null; _sourceFolder = folder; _index = files.Count == 0 ? -1 : 0;
            FolderText.Text = folder; UpdateAlbumFolderUi(); SetStatus(files.Count == 0 ? "No supported photos in this folder." : "");
            await ShowCurrentAsync();
        }
        catch (Exception ex) { ShowError("Could not open this folder", ex); }
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
            var noFolder = _sourceFolder is null;
            var emptyFolder = !noFolder && _photos.Count == 0;
            EmptyPanel.Visibility = noFolder || emptyFolder ? Visibility.Visible : Visibility.Collapsed;
            EmptyHeading.Text = emptyFolder ? "No supported photos here." : "A lighter photo folder starts here.";
            EmptyDescription.Text = emptyFolder
                ? "Choose a folder with JPG, JPEG, PNG, WebP, BMP, GIF, or TIFF photos."
                : "Choose a folder or drop it anywhere in this window. Your photos stay on this PC.";
            EndPanel.Visibility = _sourceFolder is not null && _photos.Count > 0 && !hasPhoto ? Visibility.Visible : Visibility.Collapsed;
            PhotoImage.Visibility = hasPhoto ? Visibility.Visible : Visibility.Collapsed;
            FileNameText.Visibility = hasPhoto ? Visibility.Visible : Visibility.Collapsed;
            if (!hasPhoto)
            {
                ResetPhotoAnimation();
                PhotoImage.Source = null; FileNameText.Text = "";
                return;
            }
            var path = _photos[_index];
            FileNameText.Text = Path.GetFileName(path);
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

    private async Task ProcessAsync(ActionKind kind)
    {
        if (_loading || _index < 0 || _index >= _photos.Count) return;
        var photo = _photos[_index];
        _loading = true;
        try
        {
            string? target = null;
            if (kind == ActionKind.Trash)
            {
                FileSystem.DeleteFile(photo, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else if (kind == ActionKind.Album)
            {
                if (!EnsureAlbumFolder()) return;
                var albumTarget = UniquePath(_albumFolder!, Path.GetFileName(photo));
                target = albumTarget;
                await Task.Run(() => File.Copy(photo, albumTarget));
            }
            _actions.Push(new TriageAction(kind, photo, target, _index));
            PlayActionSound(kind);
            await AnimateActionAsync(kind);
            _index++;
            SetStatus("");
            await ShowCurrentAsync();
        }
        catch (Exception ex) { ShowError(kind == ActionKind.Trash ? "Could not move this photo to the Recycle Bin" : "Could not process this photo", ex); }
        finally { _loading = false; }
    }

    private bool EnsureAlbumFolder()
    {
        if (_albumFolder is not null && Directory.Exists(_albumFolder)) return true;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose or create an album folder", UseDescriptionForTitle = true, ShowNewFolderButton = true, SelectedPath = _albumFolder ?? "" };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
        _albumFolder = dialog.SelectedPath;
        if (!Directory.Exists(_albumFolder)) { SetStatus("The album folder no longer exists. Choose another folder."); return false; }
        UpdateAlbumFolderUi();
        return true;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (_actions.Count == 0) { SetStatus("Nothing to undo."); return; }
        _loading = true;
        var action = _actions.Peek();
        try
        {
            if (action.Kind == ActionKind.Album && action.TargetPath is not null && File.Exists(action.TargetPath)) File.Delete(action.TargetPath);
            if (action.Kind == ActionKind.Trash) RecycleBinRestorer.Restore(action.SourcePath);
            _actions.Pop(); _index = Math.Clamp(action.Index, 0, Math.Max(0, _photos.Count - 1));
            SetStatus(action.Kind switch
            {
                ActionKind.Keep => "Undo complete: photo returned to the queue.",
                ActionKind.Trash => "Undo complete: photo restored from the Recycle Bin.",
                _ => "Undo complete: album copy removed."
            });
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
        _index = nextIndex; SetStatus(""); await ShowCurrentAsync();
    }
    private async void Keep_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ActionKind.Keep);
    private async void Trash_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ActionKind.Trash);
    private async void Album_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ActionKind.Album);

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
        switch (e.Key)
        {
            case Key.K: await ProcessAsync(ActionKind.Keep); break;
            case Key.T: await ProcessAsync(ActionKind.Trash); break;
            case Key.A: await ProcessAsync(ActionKind.Album); break;
            case Key.U: Undo_Click(this, new RoutedEventArgs()); break;
            case Key.Left: await NavigateAsync(-1); break;
            case Key.Right: await NavigateAsync(1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
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
            _albumFolder = dialog.SelectedPath;
            UpdateAlbumFolderUi();
            SetStatus("Album destination changed.");
        }
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

    private string FormatActionCounts()
    {
        var kept = _actions.Count(action => action.Kind == ActionKind.Keep);
        var trashed = _actions.Count(action => action.Kind == ActionKind.Trash);
        var album = _actions.Count(action => action.Kind == ActionKind.Album);
        return $"Keep {kept}   ·   Trash {trashed}   ·   Album {album}";
    }

    private void UpdateSessionUi()
    {
        var hasPhoto = _index >= 0 && _index < _photos.Count;
        ProgressText.Text = hasPhoto
            ? $"{_index + 1:N0} / {_photos.Count:N0}"
            : _photos.Count == 0 ? "0 / 0" : $"{_photos.Count:N0} / {_photos.Count:N0}";
        QueueProgressBar.Maximum = Math.Max(1, _photos.Count);
        QueueProgressBar.Value = _photos.Count == 0 ? 0 : hasPhoto ? _index + 1 : _photos.Count;
        PreviousButton.IsEnabled = hasPhoto && _index > 0;
        NextButton.IsEnabled = hasPhoto && _index < _photos.Count - 1;
        KeepButton.IsEnabled = AlbumButton.IsEnabled = TrashButton.IsEnabled = hasPhoto;
        UndoButton.IsEnabled = _actions.Count > 0;
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
            _ => ""
        };
        ActionBadge.BorderBrush = kind switch
        {
            ActionKind.Keep => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(126, 218, 166)),
            ActionKind.Trash => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 142, 142)),
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

    private enum ActionKind { Keep, Trash, Album }
    private enum MotionMode { Smooth, Standard, Reduced }
    private sealed record TriageAction(ActionKind Kind, string SourcePath, string? TargetPath, int Index);
}
