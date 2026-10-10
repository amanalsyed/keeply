using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoKeepKill;

internal static class LibraryIndexCache
{
    private const int CurrentVersion = 1;
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoKeepKill", "LibraryIndex");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static Dictionary<string, LibraryIndexEntry> Load(string root, bool includeSubfolders)
    {
        try
        {
            var path = CachePath(root, includeSubfolders);
            if (!File.Exists(path)) return new Dictionary<string, LibraryIndexEntry>(StringComparer.OrdinalIgnoreCase);
            var cache = JsonSerializer.Deserialize<LibraryIndexDocument>(File.ReadAllText(path));
            if (cache is null || cache.Version != CurrentVersion || cache.IncludeSubfolders != includeSubfolders ||
                !PathEquals(cache.RootFolder, root))
                return new Dictionary<string, LibraryIndexEntry>(StringComparer.OrdinalIgnoreCase);
            return cache.Photos.Where(photo => !string.IsNullOrWhiteSpace(photo.Path))
                .ToDictionary(photo => photo.Path, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return new Dictionary<string, LibraryIndexEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Save(string root, bool includeSubfolders, IEnumerable<LibraryIndexEntry> photos)
    {
        Directory.CreateDirectory(CacheDirectory);
        var path = CachePath(root, includeSubfolders);
        var temporaryPath = path + ".tmp";
        var document = new LibraryIndexDocument
        {
            Version = CurrentVersion,
            RootFolder = Path.GetFullPath(root),
            IncludeSubfolders = includeSubfolders,
            Photos = photos.ToList()
        };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    private static string CachePath(string root, bool includeSubfolders)
    {
        var normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var input = normalized.ToUpperInvariant() + "|" + (includeSubfolders ? "recursive" : "top-level");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        return Path.Combine(CacheDirectory, key + ".json");
    }

    private static bool PathEquals(string first, string second)
    {
        try { return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(first), Path.GetFullPath(second)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
    }
}

internal sealed class LibraryIndexDocument
{
    public int Version { get; set; }
    public string RootFolder { get; set; } = "";
    public bool IncludeSubfolders { get; set; }
    public List<LibraryIndexEntry> Photos { get; set; } = new();
}

internal sealed class LibraryIndexEntry
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public string Format { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime Date { get; set; }
    public string DateSource { get; set; } = "";
    public string Camera { get; set; } = "";
    public string Gps { get; set; } = "";
    public bool IsScreenshot { get; set; }
    public string Sha256 { get; set; } = "";
    public ulong? DifferenceFingerprint { get; set; }
}
