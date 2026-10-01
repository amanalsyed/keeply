using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PhotoKeepKill;

internal sealed class QuickFolderStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoKeepKill", "quick-folders.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Dictionary<int, string> Load()
    {
        if (!File.Exists(StorePath)) return new Dictionary<int, string>();
        var loaded = JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(StorePath))
            ?? throw new InvalidDataException("The saved Quick Folder assignments are empty or invalid.");
        return loaded
            .Where(pair => pair.Key is >= 1 and <= 9 && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => Path.GetFullPath(pair.Value));
    }

    public void Save(IReadOnlyDictionary<int, string> assignments)
    {
        var normalized = assignments
            .Where(pair => pair.Key is >= 1 and <= 9 && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => Path.GetFullPath(pair.Value));
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temp, StorePath, true);
    }
}
