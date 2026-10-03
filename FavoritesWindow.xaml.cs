using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhotoKeepKill;

public partial class FavoritesWindow : Window
{
    private readonly ObservableCollection<FavoriteItem> _items = new();
    private readonly Func<string, bool> _removeFavorite;
    private string? _destinationFolder;
    private readonly Func<string, bool> _setDestinationFolder;

    public FavoritesWindow(System.Collections.Generic.IEnumerable<string> favorites, Func<string, bool> removeFavorite,
        string? destinationFolder, Func<string, bool> setDestinationFolder)
    {
        InitializeComponent();
        _removeFavorite = removeFavorite;
        _destinationFolder = destinationFolder;
        _setDestinationFolder = setDestinationFolder;
        DestinationText.Text = string.IsNullOrWhiteSpace(destinationFolder)
            ? "Favorites destination not chosen yet. Press F while viewing a photo to choose one."
            : $"Favorites copies are saved in: {destinationFolder}";
        foreach (var path in favorites.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            _items.Add(CreateItem(path));
        FavoritesList.ItemsSource = _items;
        UpdateUi();
    }

    private void FavoritesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();

    private void FavoritesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ShowLocation();

    private void ShowLocation_Click(object sender, RoutedEventArgs e) => ShowLocation();

    private void OpenDestination_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_destinationFolder))
        {
            MessageBox.Show(this, "Choose a destination with Change folder, or press F while viewing a photo to choose one.", "Favorites folder not set", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            if (!Directory.Exists(_destinationFolder)) Directory.CreateDirectory(_destinationFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_destinationFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, $"Keeply couldn't open the Favorites folder.\n\n{ex.Message}", "Could not open folder", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ChangeDestination_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a Favorites folder, separate from the Album folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = _destinationFolder ?? ""
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var selected = Path.GetFullPath(dialog.SelectedPath);
        if (!_setDestinationFolder(selected)) return;
        _destinationFolder = selected;
        DestinationText.Text = $"Favorites copies are saved in: {selected}";
        UpdateUi();
    }

    private void ShowLocation()
    {
        if (FavoritesList.SelectedItem is not FavoriteItem item) return;

        try
        {
            if (File.Exists(item.Path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
            else if (Directory.Exists(item.FolderPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.FolderPath}\"") { UseShellExecute = true });
            else
                MessageBox.Show(this, "The photo and its original folder are currently unavailable. Reconnect the drive or restore the folder to find it.", "Favorite unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Keeply couldn't open the photo's folder.\n\n{ex.Message}", "Could not open folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not FavoriteItem item || !_removeFavorite(item.Path)) return;
        _items.Remove(item);
        UpdateUi();
    }

    private void UpdateUi()
    {
        var selected = FavoritesList.SelectedItem is FavoriteItem;
        ShowLocationButton.IsEnabled = RemoveButton.IsEnabled = selected;
        OpenDestinationButton.IsEnabled = !string.IsNullOrWhiteSpace(_destinationFolder);
        CountText.Text = _items.Count == 0
            ? "No favorites yet. Press F while viewing a photo to copy it into the Favorites folder and add it to this list."
            : $"{_items.Count:N0} favorite(s) in Keeply. Original photos stay in their original folders.";
    }

    private static FavoriteItem CreateItem(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath) ?? "";
        return new FavoriteItem(fullPath, Path.GetFileName(fullPath), folder, File.Exists(fullPath) ? "Available" : "Not found");
    }

    private sealed record FavoriteItem(string Path, string FileName, string FolderPath, string Availability);
}
