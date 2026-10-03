using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoKeepKill;

internal sealed class ResumeSessionStore
{
    private static readonly string SessionsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoKeepKill", "Sessions");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ResumeSession? Load(string folderPath)
    {
        var path = SessionPath(folderPath);
        if (!File.Exists(path)) return null;
        var session = JsonSerializer.Deserialize<ResumeSession>(File.ReadAllText(path));
        if (session is null || session.Version != 1 || !PathEquals(session.FolderPath, folderPath))
            throw new InvalidDataException("Keeply found a saved sorting session it could not read. Your photos are unchanged. You can start a fresh session for this folder.");
        session.Actions = ReadActionLog(folderPath);
        return session;
    }

    public ResumeSession? LoadMostRecent()
    {
        if (!Directory.Exists(SessionsDirectory)) return null;
        foreach (var path in Directory.EnumerateFiles(SessionsDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<ResumeSession>(File.ReadAllText(path));
                var session = metadata is null ? null : Load(metadata.FolderPath);
                if (session is not null && session.Version == 1 && Directory.Exists(session.FolderPath) &&
                    (session.Actions?.Count > 0 || session.CurrentIndex > 0 || !string.IsNullOrWhiteSpace(session.AlbumFolder)))
                    return session;
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        return null;
    }

    public void Save(ResumeSession session)
    {
        Directory.CreateDirectory(SessionsDirectory);
        var path = SessionPath(session.FolderPath);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(session, JsonOptions));
        File.Move(temp, path, true);
    }

    public void RecordAction(string folderPath, SavedTriageAction action) => AppendJournal(folderPath, new JournalEntry { Type = "action", Action = action });

    public void RecordUndo(string folderPath, string sourcePath) => AppendJournal(folderPath, new JournalEntry { Type = "undo", SourcePath = sourcePath });

    public void Delete(string folderPath)
    {
        var path = SessionPath(folderPath);
        if (File.Exists(path)) File.Delete(path);
        var journal = JournalPath(folderPath);
        if (File.Exists(journal)) File.Delete(journal);
    }

    private List<SavedTriageAction> ReadActionLog(string folderPath)
    {
        var journal = JournalPath(folderPath);
        var actions = new List<SavedTriageAction>();
        if (!File.Exists(journal)) return actions;
        foreach (var line in File.ReadLines(journal))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<JournalEntry>(line) ?? throw new InvalidDataException("A saved sorting-history record is empty.");
            if (entry.Type == "action" && entry.Action is not null) actions.Add(entry.Action);
            else if (entry.Type == "undo" && entry.SourcePath is not null)
            {
                var last = actions.FindLastIndex(action => StringComparer.OrdinalIgnoreCase.Equals(action.SourcePath, entry.SourcePath));
                if (last >= 0) actions.RemoveAt(last);
            }
            else throw new InvalidDataException("Keeply found an invalid saved sorting-history record.");
        }
        return actions;
    }

    private void AppendJournal(string folderPath, JournalEntry entry)
    {
        Directory.CreateDirectory(SessionsDirectory);
        using var stream = new FileStream(JournalPath(folderPath), FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(JsonSerializer.Serialize(entry));
        writer.Flush();
        stream.Flush(true);
    }

    private static string SessionPath(string folderPath) => Path.Combine(SessionsDirectory, LocalLicenseStore.HashFolder(folderPath) + ".json");
    private static string JournalPath(string folderPath) => Path.Combine(SessionsDirectory, LocalLicenseStore.HashFolder(folderPath) + ".jsonl");

    private static bool PathEquals(string first, string second)
    {
        try { return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(first), Path.GetFullPath(second)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
    }
}

internal sealed class ResumeSession
{
    public int Version { get; set; } = 1;
    public string FolderPath { get; set; } = "";
    public string? CurrentPhotoPath { get; set; }
    public int CurrentIndex { get; set; }
    public string? AlbumFolder { get; set; }
    public int QueueCount { get; set; }
    public string QueueFingerprint { get; set; } = "";
    [JsonIgnore]
    public List<SavedTriageAction> Actions { get; set; } = new();
}

internal sealed class SavedTriageAction
{
    public int Kind { get; set; }
    public string SourcePath { get; set; } = "";
    public string? TargetPath { get; set; }
    public int Index { get; set; }
    public bool WasFavorite { get; set; }
}

internal sealed class JournalEntry
{
    public string Type { get; set; } = "";
    public SavedTriageAction? Action { get; set; }
    public string? SourcePath { get; set; }
}
