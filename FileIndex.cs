using System.Collections.Concurrent;
using System.IO.Compression;

namespace Everything2;

public sealed class FileIndex : IDisposable
{
    private readonly string[] roots;
    private readonly string cache;
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly ConcurrentQueue<(string Path, bool Remove, bool Walk)> changes = new();
    private readonly CancellationTokenSource stop = new();
    private Dictionary<string, Entry> items = new(StringComparer.OrdinalIgnoreCase);
    private Entry[] snapshot = [];
    private int rescan = 1;
    private bool dirty;
    public Entry[] Snapshot => Volatile.Read(ref snapshot);
    public string Status { get; private set; } = "Starting…";
    public event Action? Updated;
    public FileIndex(string[] roots, string cache) { this.roots = roots; this.cache = cache; }
    public void Start() => _ = Task.Run(Work);
    public void Rebuild() => Interlocked.Exchange(ref rescan, 1);
    private bool Excluded(string path) => path.StartsWith(System.IO.Path.GetDirectoryName(cache)! + "\\", StringComparison.OrdinalIgnoreCase);
    private void Publish() { Volatile.Write(ref snapshot, items.Values.ToArray()); Updated?.Invoke(); }

    private async Task Work()
    {
        try
        {
            Load();
            Publish();
            foreach (var root in roots)
            {
                try
                {
                    var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite, InternalBufferSize = 65536 };
                    watcher.Created += (_, e) => Enqueue(e.FullPath, false);
                    watcher.Deleted += (_, e) => Enqueue(e.FullPath, true);
                    watcher.Renamed += (_, e) => { Enqueue(e.OldFullPath, true); Enqueue(e.FullPath, false); };
                    watcher.Changed += (_, e) => Enqueue(e.FullPath, false, false);
                    watcher.Error += (_, _) => Rebuild();
                    watcher.EnableRaisingEvents = true;
                    watchers.Add(watcher);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            var saved = DateTime.UtcNow;
            var nextScan = DateTime.UtcNow.AddMinutes(15);
            while (!stop.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref rescan, 0) == 1 || DateTime.UtcNow >= nextScan)
                {
                    Scan();
                    nextScan = DateTime.UtcNow.AddMinutes(15);
                    saved = DateTime.MinValue;
                }
                var updated = false;
                for (int n = 0; n < 5000 && changes.TryDequeue(out var change); n++)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if (change.Remove) RemoveTree(change.Path);
                    else if (Directory.Exists(change.Path))
                    {
                        if (ReadEntry(new DirectoryInfo(change.Path)) is { } directory) items[change.Path] = directory;
                        if (change.Walk && CanWalk(change.Path)) Walk(change.Path, items, false);
                    }
                    else if (File.Exists(change.Path) && ReadEntry(new FileInfo(change.Path)) is { } file) items[change.Path] = file;
                    updated = true;
                }
                if (updated) { dirty = true; Publish(); }
                if (dirty && DateTime.UtcNow - saved > TimeSpan.FromSeconds(60)) { Save(); saved = DateTime.UtcNow; }
                await Task.Delay(500, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Status = "Index stopped: " + e.Message; Updated?.Invoke(); }
    }
    private void Enqueue(string path, bool remove, bool walk = true)
    {
        if (Excluded(path)) return;
        if (changes.Count > 100000) { Rebuild(); return; }
        changes.Enqueue((path, remove, walk));
    }
    internal static Entry? ReadEntry(FileSystemInfo info)
    {
        try
        {
            var directory = (info.Attributes & FileAttributes.Directory) != 0;
            return new(info.FullName, directory, info is FileInfo file ? file.Length : null, info.LastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }
    private static bool CanWalk(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
    private void RemoveTree(string path)
    {
        if (items.Remove(path, out var removed) && !removed.IsDirectory) return;
        var prefix = path.TrimEnd('\\') + "\\";
        foreach (var key in items.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray()) items.Remove(key);
    }
    private void Scan()
    {
        Status = "Scanning local drives…";
        Updated?.Invoke();
        var fresh = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots) Walk(root, fresh, true);
        items = fresh;
        dirty = true;
        Status = $"Watching {watchers.Count} drive(s) · refreshed {DateTime.Now:t}";
        Publish();
    }
    private void Walk(string root, Dictionary<string, Entry> target, bool progress)
    {
        var pending = new Stack<string>(); pending.Push(root);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
        while (pending.TryPop(out var folder))
        {
            stop.Token.ThrowIfCancellationRequested();
            try
            {
                foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
                {
                    if (Excluded(info.FullName)) continue;
                    var directory = (info.Attributes & FileAttributes.Directory) != 0;
                    if (ReadEntry(info) is { } entry) target[info.FullName] = entry;
                    if (directory && (info.Attributes & FileAttributes.ReparsePoint) == 0) pending.Push(info.FullName);
                    if (progress && target.Count % 25000 == 0)
                    {
                        Status = $"Scanning · {target.Count:N0} items · {folder}";
                        // Make the first scan searchable while it is still running.
                        if (items.Count == 0) Volatile.Write(ref snapshot, target.Values.ToArray());
                        Updated?.Invoke();
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
    }
    private void Load()
    {
        try
        {
            using var file = File.OpenRead(cache);
            using var zip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(zip);
            if (reader.ReadString() != "Everything2-v2") return;
            var count = reader.ReadInt32();
            var loaded = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                var path = reader.ReadString(); var directory = reader.ReadBoolean();
                var size = reader.ReadInt64(); var ticks = reader.ReadInt64();
                if (roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase))) loaded[path] = new(path, directory, size < 0 ? null : size, ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc));
            }
            items = loaded;
            Status = "Cached index loaded · checking for changes…";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException) { Status = "Building a fresh index…"; }
    }
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);
            using (var file = File.Create(cache + ".tmp"))
            using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            using (var writer = new BinaryWriter(zip))
            {
                writer.Write("Everything2-v2"); writer.Write(items.Count);
                foreach (var entry in items.Values) { writer.Write(entry.Path); writer.Write(entry.IsDirectory); writer.Write(entry.Size ?? -1); writer.Write(entry.ModifiedUtc?.Ticks ?? 0); }
            }
            File.Move(cache + ".tmp", cache, true);
            dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = "Search ready · cache could not be saved: " + e.Message; Updated?.Invoke(); }
    }
    public void Dispose() { stop.Cancel(); foreach (var watcher in watchers) watcher.Dispose(); }
}
