using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace PhotoKeepKill;

internal static class RecycleBinRestorer
{
    public static void Restore(string originalPath)
    {
        var expectedPath = Path.GetFullPath(originalPath);
        if (!Directory.Exists(Path.GetDirectoryName(expectedPath)))
            throw new DirectoryNotFoundException("The photo's original folder is unavailable. Reconnect the drive or restore the folder, then try Undo again.");

        var recycleItem = FindRecycleItem(expectedPath);
        if (recycleItem is null)
            throw new FileNotFoundException("The matching photo is no longer in this drive's Recycle Bin. Check the Recycle Bin and original folder.", expectedPath);

        RestoreWithShell(expectedPath, recycleItem.Value.DataPath);
    }

    private static RecycleItem? FindRecycleItem(string expectedPath)
    {
        var volumeRoot = Path.GetPathRoot(expectedPath);
        if (string.IsNullOrWhiteSpace(volumeRoot)) return null;

        var recycleRoot = Path.Combine(volumeRoot, "$Recycle.Bin");
        if (!Directory.Exists(recycleRoot)) return null;
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
            throw new InvalidOperationException("Could not identify the current Windows account to inspect its Recycle Bin.");
        var userBin = Path.Combine(recycleRoot, sid);
        if (!Directory.Exists(userBin)) return null;

        var metadataFound = false;
        try
        {
            foreach (var metadataPath in Directory.EnumerateFiles(userBin, "$I*", SearchOption.TopDirectoryOnly))
            {
                string? original;
                try { original = ReadOriginalPath(metadataPath); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                if (original is null || !PathEquals(original, expectedPath)) continue;
                metadataFound = true;

                var metadataName = Path.GetFileName(metadataPath);
                if (metadataName.Length < 3) continue;
                var dataPath = Path.Combine(userBin, "$R" + metadataName[2..]);
                if (File.Exists(dataPath)) return new RecycleItem(dataPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException("Windows denied access to this drive's Recycle Bin. You can restore the photo through File Explorer, or check the drive's permissions.", ex);
        }
        catch (IOException ex)
        {
            throw new IOException("Could not read this drive's Recycle Bin metadata. Check that the drive is available and try Undo again.", ex);
        }

        if (metadataFound)
            throw new FileNotFoundException("Windows has a Recycle Bin record for this photo, but its photo data is missing. Check the Recycle Bin.", expectedPath);

        return null;
    }

    // Windows 10 and later use $I version 2: 24-byte header, a character count,
    // then a UTF-16 original path. Version 1 is also accepted for older bins.
    private static string? ReadOriginalPath(string metadataPath)
    {
        var bytes = File.ReadAllBytes(metadataPath);
        if (bytes.Length < 24) return null;

        var version = BitConverter.ToUInt64(bytes, 0);
        if (version == 2)
        {
            if (bytes.Length < 28) return null;
            var characterCount = BitConverter.ToUInt32(bytes, 24);
            if (characterCount == 0 || characterCount > (bytes.Length - 28) / 2) return null;
            var value = Encoding.Unicode.GetString(bytes, 28, checked((int)characterCount * 2));
            return value.TrimEnd('\0');
        }

        if (version != 1) return null;

        // Version 1 stores a NUL-terminated UTF-16LE path starting at offset 24.
        var pathBytes = bytes.AsSpan(24);
        for (var i = 0; i + 1 < pathBytes.Length; i += 2)
        {
            if (pathBytes[i] == 0 && pathBytes[i + 1] == 0)
                return Encoding.Unicode.GetString(pathBytes[..i]);
        }
        return Encoding.Unicode.GetString(pathBytes).TrimEnd('\0');
    }

    private static void RestoreWithShell(string expectedPath, string dataPath)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) throw new InvalidOperationException("The Windows Shell is unavailable. Restore the photo through File Explorer.");

        object? shell = null;
        object? recycleBin = null;
        object? items = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) throw new InvalidOperationException("Could not start the Windows Shell.");
            dynamic shellDynamic = shell;
            recycleBin = shellDynamic.Namespace(10);
            if (recycleBin is null) throw new InvalidOperationException("Could not open the Windows Recycle Bin.");
            dynamic binDynamic = recycleBin;
            items = binDynamic.Items();

            foreach (var item in (IEnumerable)items)
            {
                try
                {
                    dynamic entry = item;
                    var shellPath = Convert.ToString(entry.Path) ?? "";
                    if (!PathEquals(shellPath, dataPath)) continue;

                    InvokeRestoreVerb(entry);
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        if (File.Exists(expectedPath)) return;
                        Thread.Sleep(100);
                    }

                    throw new IOException("Windows found the correct Recycle Bin item but did not restore it to the original location. Check for a restore prompt or a file with the same name, then try again.");
                }
                finally
                {
                    if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
                }
            }

            throw new InvalidOperationException("The photo's Recycle Bin record was found, but Windows did not expose its matching item for restore. Restore it through File Explorer.");
        }
        finally
        {
            if (items is not null && Marshal.IsComObject(items)) Marshal.ReleaseComObject(items);
            if (recycleBin is not null && Marshal.IsComObject(recycleBin)) Marshal.ReleaseComObject(recycleBin);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static void InvokeRestoreVerb(dynamic entry)
    {
        object? verbs = null;
        try
        {
            verbs = entry.Verbs();
            dynamic verbCollection = verbs!;
            var count = Convert.ToInt32(verbCollection.Count);
            for (var i = 0; i < count; i++)
            {
                object? verb = null;
                try
                {
                    verb = verbCollection.Item(i);
                    dynamic shellVerb = verb!;
                    var caption = Convert.ToString(shellVerb.Name) ?? "";
                    if (caption.Replace("&", "", StringComparison.Ordinal).Trim().Equals("Restore", StringComparison.OrdinalIgnoreCase))
                    {
                        shellVerb.DoIt();
                        return;
                    }
                }
                finally
                {
                    if (verb is not null && Marshal.IsComObject(verb)) Marshal.ReleaseComObject(verb);
                }
            }

            // Restore is the first context-menu command for an item in the
            // Recycle Bin, including on localized Windows installations.
            if (count > 0)
            {
                object? firstVerb = null;
                try
                {
                    firstVerb = verbCollection.Item(0);
                    dynamic shellVerb = firstVerb!;
                    shellVerb.DoIt();
                }
                finally
                {
                    if (firstVerb is not null && Marshal.IsComObject(firstVerb)) Marshal.ReleaseComObject(firstVerb);
                }
                return;
            }

            throw new InvalidOperationException("Windows did not provide a Restore command for this Recycle Bin item.");
        }
        finally
        {
            if (verbs is not null && Marshal.IsComObject(verbs)) Marshal.ReleaseComObject(verbs);
        }
    }

    private static bool PathEquals(string candidate, string expectedPath)
    {
        try { return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(candidate), Path.GetFullPath(expectedPath)); }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    private readonly record struct RecycleItem(string DataPath);
}
