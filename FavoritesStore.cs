using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PhotoKeepKill;

internal sealed class FavoritesStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoKeepKill", "favorites.json");
    private static readonly string DestinationPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoKeepKill", "favorites-destination.json");

    public string? LoadDestination()
    {
        if (!File.Exists(DestinationPath)) return null;
        var path = JsonSerializer.Deserialize<string>(File.ReadAllText(DestinationPath));
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    public void SaveDestination(string path)
    {
        var normalized = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(DestinationPath)!;
        Directory.CreateDirectory(directory);
        var temp = DestinationPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(normalized));
        File.Move(temp, DestinationPath, true);
    }

    public HashSet<string> Load()
    {
        if (!File.Exists(StorePath)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath))
            ?? throw new InvalidDataException("Keeply couldn't read the saved Favorites list.");
        var favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in saved.Where(path => !string.IsNullOrWhiteSpace(path)))
            favorites.Add(Path.GetFullPath(path));
        return favorites;
    }

    public void Save(IEnumerable<string> favorites)
    {
        var normalized = favorites
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, StorePath, true);
    }
}
