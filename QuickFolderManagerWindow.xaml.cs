using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PhotoKeepKill;

public partial class QuickFolderManagerWindow : Window
{
    private readonly Dictionary<int, string> _assignments;
    private readonly Dictionary<int, TextBlock> _folderLabels = new();

    public IReadOnlyDictionary<int, string> Assignments => _assignments;

    public QuickFolderManagerWindow(IReadOnlyDictionary<int, string> assignments)
    {
        InitializeComponent();
        _assignments = assignments.ToDictionary(pair => pair.Key, pair => pair.Value);
        for (var slot = 1; slot <= 9; slot++) AddRow(slot);
    }

    private void AddRow(int slot)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 8, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var key = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(225, 239, 232)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = slot.ToString(), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(37, 97, 69)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        Grid.SetColumn(key, 0);
        row.Children.Add(key);

        var label = new TextBlock
        {
            Text = _assignments.TryGetValue(slot, out var path) ? DisplayPath(path) : "Not assigned",
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 78, 74))
        };
        label.ToolTip = _assignments.GetValueOrDefault(slot);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        _folderLabels[slot] = label;

        var choose = new Button { Content = "Choose…", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(9, 5, 9, 5) };
        choose.Click += (_, _) => ChooseFolder(slot);
        Grid.SetColumn(choose, 2);
        row.Children.Add(choose);

        var clear = new Button { Content = "Clear", MinWidth = 64, Padding = new Thickness(8, 5, 8, 5), IsEnabled = _assignments.ContainsKey(slot) };
        clear.Click += (_, _) => ClearFolder(slot, clear);
        Grid.SetColumn(clear, 3);
        row.Children.Add(clear);
        RowsPanel.Children.Add(row);
    }

    private void ChooseFolder(int slot)
    {
        var current = _assignments.GetValueOrDefault(slot);
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"Choose a destination for shortcut {slot}",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = current is not null && Directory.Exists(current) ? current : ""
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _assignments[slot] = Path.GetFullPath(dialog.SelectedPath);
        RefreshRow(slot);
    }

    private void ClearFolder(int slot, Button clear)
    {
        _assignments.Remove(slot);
        RefreshRow(slot);
        clear.IsEnabled = false;
    }

    private void RefreshRow(int slot)
    {
        var label = _folderLabels[slot];
        var assigned = _assignments.TryGetValue(slot, out var path);
        label.Text = assigned ? DisplayPath(path!) : "Not assigned";
        label.ToolTip = assigned ? path : null;
        if (RowsPanel.Children[slot - 1] is Grid row && row.Children.Count > 3 && row.Children[3] is Button clear)
            clear.IsEnabled = assigned;
    }

    private static string DisplayPath(string path)
    {
        if (!Directory.Exists(path)) return $"Unavailable: {path}";
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
