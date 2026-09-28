using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoKeepKill;

internal sealed class LocalLicenseStore
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoKeepKill");
    private static readonly string StatePath = Path.Combine(Root, "account-state.json");
    private readonly object _sync = new();
    private State _state;

    public LocalLicenseStore() { Directory.CreateDirectory(Root); _state = Read(); }
    public IReadOnlyCollection<string> FolderHashes { get { lock (_sync) return _state.FolderHashes.ToArray(); } }
    public bool IsLicensed { get { lock (_sync) return _state.License is not null; } }
    public string InstallName { get { lock (_sync) return _state.InstallName; } }
    public LicenseRecord? License { get { lock (_sync) return _state.License; } }

    public static string HashFolder(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public void RememberFolder(string hash)
    {
        lock (_sync) { if (_state.FolderHashes.Contains(hash, StringComparer.Ordinal)) return; _state.FolderHashes.Add(hash); Save(); }
    }
    public void Activate(string key, string instanceId)
    {
        lock (_sync) { _state.License = new LicenseRecord(Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(key))), instanceId); Save(); }
    }
    public string? ReadLicenseKey()
    {
        lock (_sync) return _state.License is null ? null : Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(_state.License.ProtectedKey)));
    }
    public void ClearLicense() { lock (_sync) { _state.License = null; Save(); } }

    private State Read()
    {
        try { return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State(); }
        catch (FileNotFoundException) { return new State(); }
        catch (JsonException) { throw new InvalidDataException("The local usage and license file is damaged. Keep a backup before removing it: " + StatePath); }
    }
    private void Save()
    {
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, StatePath, true);
    }

    private static byte[] Protect(byte[] data) => Transform(data, true);
    private static byte[] Unprotect(byte[] data) => Transform(data, false);
    private static byte[] Transform(byte[] input, bool protect)
    {
        var source = new DataBlob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        Marshal.Copy(input, 0, source.Data, input.Length);
        try
        {
            DataBlob output;
            var ok = protect ? CryptProtectData(ref source, "Keeply license", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var bytes = new byte[output.Size]; Marshal.Copy(output.Data, bytes, 0, bytes.Length); return bytes; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(source.Data); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    public sealed class State
    {
        public List<string> FolderHashes { get; set; } = new();
        public string InstallName { get; set; } = "Keeply-" + Guid.NewGuid().ToString("N");
        public LicenseRecord? License { get; set; }
    }
    public sealed record LicenseRecord(string ProtectedKey, string InstanceId);
}
