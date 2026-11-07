using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SystemCockpit.Services;

public sealed record CleanupCandidate(string Path, long Bytes, DateTime LastWriteUtc) : INotifyPropertyChanged
{
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string SizeDisplay => $"{Bytes / 1048576d:0.00} MiB";
}
public sealed record CleanupScan(List<CleanupCandidate> Files, List<string> Issues);
public sealed record CleanupResult(int Deleted, long Bytes, List<string> Issues, bool Cancelled);

public sealed class TempCleaner
{
    public string Root { get; }
    private readonly Dictionary<string, CleanupCandidate> _preview = new(StringComparer.OrdinalIgnoreCase);
    public TempCleaner(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Root.Equals(Path.GetPathRoot(Root), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Laufwerkswurzel ist kein Cleaner-Ziel.", nameof(root));
    }

    public CleanupScan Scan(CancellationToken token)
    {
        _preview.Clear();
        var files = new List<CleanupCandidate>();
        var issues = new List<string>();
        var queue = new Stack<(string Path, int Depth)>();
        queue.Push((Root, 0));
        var visited = 0;
        while (queue.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (visited >= 10000)
            {
                issues.Add("Scanlimit von 10000 Eintraegen erreicht. Vorschau ist unvollstaendig.");
                break;
            }
            try
            {
                VerifyParents(directory.Path);
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (++visited > 10000)
                    {
                        issues.Add("Scanlimit von 10000 Eintraegen erreicht. Vorschau ist unvollstaendig.");
                        break;
                    }
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.ReadOnly)) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (directory.Depth < 32) queue.Push((entry, directory.Depth + 1));
                        else issues.Add($"Tiefe > 32 uebersprungen: {entry}");
                        continue;
                    }
                    var file = new FileInfo(entry);
                    if (file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7)) continue;
                    var candidate = new CleanupCandidate(file.FullName, file.Length, file.LastWriteTimeUtc);
                    files.Add(candidate);
                    _preview.Add(candidate.Path, candidate);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add($"{directory.Path}: {ex.Message}");
            }
        }
        return new(files, issues);
    }

    public CleanupResult DeleteSelected(IEnumerable<CleanupCandidate> selected, CancellationToken token)
    {
        var deleted = 0;
        long bytes = 0;
        var issues = new List<string>();
        foreach (var candidate in selected.DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (token.IsCancellationRequested) return new(deleted, bytes, issues, true);
            try
            {
                ActionJournal.Append("cleanup-attempt", new { candidate.Path, candidate.Bytes });
                if (!_preview.TryGetValue(candidate.Path, out var preview) || preview != candidate)
                    throw new IOException("Datei gehoert nicht zur aktuellen Vorschau.");
                VerifyParents(Path.GetDirectoryName(candidate.Path)!);
                DeleteVerified(candidate);
                _preview.Remove(candidate.Path);
                deleted++;
                bytes += candidate.Bytes;
                ActionJournal.Append("cleanup-deleted", new { candidate.Path, candidate.Bytes });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                issues.Add($"{candidate.Path}: {ex.Message}");
                try { ActionJournal.Append("cleanup-error", new { candidate.Path, error = ex.Message }); }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException)
                {
                    issues.Add($"Aktionsprotokoll nicht schreibbar; Bereinigung gestoppt: {journalError.Message}");
                    return new(deleted, bytes, issues, true);
                }
            }
        }
        return new(deleted, bytes, issues, false);
    }

    private void VerifyParents(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!IsWithinRoot(full)) throw new IOException("Pfad liegt ausserhalb des freigegebenen Temp-Ordners.");
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Verknuepfung/Junction wird nicht verfolgt: {current.FullName}");
    }

    private bool IsWithinRoot(string path) => path.Equals(Root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void DeleteVerified(CleanupCandidate candidate)
    {
        using var handle = CreateFile(candidate.Path, 0x10000 | 0x80, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
        if (length == 0 || length >= path.Capacity) throw new IOException("Dateipfad konnte nicht verifiziert werden.");
        var resolved = path.ToString();
        if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        var size = ((long)info.SizeHigh << 32) | info.SizeLow;
        var writeTime = DateTime.FromFileTimeUtc(((long)info.WriteHigh << 32) | info.WriteLow);
        if (!IsWithinRoot(resolved) || !resolved.Equals(candidate.Path, StringComparison.OrdinalIgnoreCase) ||
            (info.Attributes & (0x400u | 0x10u | 0x1u | 0x4u)) != 0 ||
            info.Links != 1 || size != candidate.Bytes || writeTime != candidate.LastWriteUtc ||
            writeTime >= DateTime.UtcNow.AddDays(-7))
            throw new IOException("Datei wurde geaendert, verknuepft oder ist nicht mehr loeschbar.");
        var disposition = new Disposition { Delete = true };
        if (!SetFileInformationByHandle(handle, 4, ref disposition, (uint)Marshal.SizeOf<Disposition>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreateLow, CreateHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Disposition { [MarshalAs(UnmanagedType.Bool)] public bool Delete; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int type, ref Disposition disposition, uint size);
}
