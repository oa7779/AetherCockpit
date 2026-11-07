using System.IO;

namespace SystemCockpit.Services;

public sealed record LargeFile(string Path, long Bytes, DateTime ModifiedUtc)
{
    public double MiB => Bytes / 1048576d;
}
public sealed record StorageScan(IReadOnlyList<LargeFile> Files, int Inspected, bool Limited,
    IReadOnlyList<string> Issues, int IssueCount);

public static class StorageAnalysis
{
    public const int MaxEntries = 100000;
    public const int MaxResults = 1000;
    public const int MaxDepth = 32;

    public static StorageScan Scan(string root, long minimumBytes, CancellationToken token)
    {
        if (minimumBytes < 0 || !Path.IsPathFullyQualified(root) || !Directory.Exists(root))
            throw new ArgumentException("Vorhandenen absoluten Ordner und nichtnegative Mindestgroesse waehlen.");
        var fullRoot = Path.GetFullPath(root);
        for (var parent = new DirectoryInfo(fullRoot); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Ordnerlinks/Junctions werden nicht analysiert: " + parent.FullName);
        var queue = new Stack<(string Path, int Depth)>();
        queue.Push((fullRoot, 0));
        var results = new PriorityQueue<LargeFile, long>();
        var issues = new List<string>();
        var issueCount = 0;
        var visited = 0;
        var limited = false;
        while (queue.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(folder.Path) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var path in Directory.EnumerateFileSystemEntries(folder.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (visited >= MaxEntries) { limited = true; break; }
                    visited++;
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (folder.Depth < MaxDepth) queue.Push((path, folder.Depth + 1));
                            else { limited = true; Issue("Tiefenlimit erreicht: " + path); }
                            continue;
                        }
                        var file = new FileInfo(path);
                        var length = file.Length;
                        if (length < minimumBytes) continue;
                        results.Enqueue(new(path, length, file.LastWriteTimeUtc), length);
                        if (results.Count > MaxResults) { results.Dequeue(); limited = true; }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { Issue(path + ": " + ex.Message); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Issue(folder.Path + ": " + ex.Message); }
            if (visited >= MaxEntries) { limited = true; break; }
        }
        return new(results.UnorderedItems.Select(p => p.Element).OrderByDescending(f => f.Bytes)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(), visited, limited, issues, issueCount);

        void Issue(string text)
        {
            issueCount++;
            if (issues.Count < 50) issues.Add(text);
        }
    }
}
