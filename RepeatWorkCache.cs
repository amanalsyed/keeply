using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoKeepKill;

/// <summary>Small per-operation cache for repeat local media scans and generated copies.</summary>
internal sealed class RepeatWorkCache
{
    private const int CurrentVersion = 1;
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoKeepKill", "WorkCache");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _scope;
    private readonly Dictionary<string, RepeatWorkEntry> _entries;

    private RepeatWorkCache(string scope, Dictionary<string, RepeatWorkEntry> entries)
    {
        _scope = scope;
        _entries = entries;
    }

    public static RepeatWorkCache Open(string scope)
    {
        try
        {
            var path = CachePath(scope);
            if (!File.Exists(path)) return new RepeatWorkCache(scope, new(StringComparer.OrdinalIgnoreCase));
            var document = JsonSerializer.Deserialize<RepeatWorkDocument>(File.ReadAllText(path));
            if (document is null || document.Version != CurrentVersion || !StringComparer.Ordinal.Equals(document.Scope, scope))
                return new RepeatWorkCache(scope, new(StringComparer.OrdinalIgnoreCase));
            var entries = document.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.SourcePath))
                .GroupBy(entry => entry.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            return new RepeatWorkCache(scope, entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return new RepeatWorkCache(scope, new(StringComparer.OrdinalIgnoreCase));
        }
    }

    public bool TryGetCurrentSource(string path, out RepeatWorkEntry entry)
    {
        entry = null!;
        if (!_entries.TryGetValue(path, out var cached)) return false;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != cached.SourceLength || info.LastWriteTimeUtc.Ticks != cached.SourceLastWriteUtcTicks)
                return false;
            entry = cached;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public bool TryGetCompletedOutput(string sourcePath, out RepeatWorkEntry entry)
    {
        if (TryGetCurrentSource(sourcePath, out entry) && !string.IsNullOrWhiteSpace(entry.OutputPath))
        {
            try
            {
                var output = new FileInfo(entry.OutputPath);
                if (output.Exists && output.Length == entry.OutputLength && output.LastWriteTimeUtc.Ticks == entry.OutputLastWriteUtcTicks)
                    return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        entry = null!;
        return false;
    }

    public void Set(RepeatWorkEntry entry) => _entries[entry.SourcePath] = entry;

    public void Save()
    {
        Directory.CreateDirectory(CacheDirectory);
        var path = CachePath(_scope);
        var temporaryPath = path + ".tmp";
        var document = new RepeatWorkDocument { Version = CurrentVersion, Scope = _scope, Entries = _entries.Values.ToList() };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    public static RepeatWorkEntry SourceEntry(string path)
    {
        var info = new FileInfo(path);
        return new RepeatWorkEntry { SourcePath = Path.GetFullPath(path), SourceLength = info.Length, SourceLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks };
    }

    private static string CachePath(string scope)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
        return Path.Combine(CacheDirectory, key + ".json");
    }
}

internal sealed class RepeatWorkDocument
{
    public int Version { get; set; }
    public string Scope { get; set; } = "";
    public List<RepeatWorkEntry> Entries { get; set; } = new();
}

internal sealed class RepeatWorkEntry
{
    public string SourcePath { get; set; } = "";
    public long SourceLength { get; set; }
    public long SourceLastWriteUtcTicks { get; set; }
    public string OutputPath { get; set; } = "";
    public long OutputLength { get; set; }
    public long OutputLastWriteUtcTicks { get; set; }
    public string Result { get; set; } = "";
    public string Savings { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool WasCompressed { get; set; }
    public bool WasConverted { get; set; }
    public string Sha256 { get; set; } = "";
    public ulong? DifferenceFingerprint { get; set; }
}
