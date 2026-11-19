using System.Text.Json;
using System.Diagnostics;

namespace Everything2;

public sealed record SavedSearch(string Name, string Text, int Kind = 0, string? Drive = null, int SizeFilter = 0, int DateFilter = 0, bool Launcher = false)
{
    public override string ToString() => Name;
}

public sealed class UserLibrary
{
    private sealed record Data(SavedSearch[] Searches, string[] Pins, string[]? ApprovedLaunches = null);
    private readonly string path;
    private Data data = new([], []);
    public IReadOnlyList<SavedSearch> Searches => data.Searches;
    public IReadOnlyList<string> Pins => data.Pins;
    public string? LoadWarning { get; }

    public UserLibrary(string path)
    {
        this.path = path;
        try
        {
            if (!File.Exists(path)) return;
            var loaded = JsonSerializer.Deserialize<Data>(File.ReadAllText(path));
            if (loaded?.Searches == null || loaded.Pins == null || loaded.Searches.Any(s => s == null || string.IsNullOrWhiteSpace(s.Name) || s.Text == null) || loaded.Pins.Any(string.IsNullOrWhiteSpace))
                throw new JsonException("Invalid saved library.");
            if (loaded.ApprovedLaunches?.Any(string.IsNullOrWhiteSpace) == true) throw new JsonException("Invalid launch approvals.");
            data = loaded;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { LoadWarning = "Couldn't load saved searches and launcher pins: " + e.Message; }
    }

    private void Commit(Data next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
        data = next;
    }
    public void Save(SavedSearch search)
    {
        if (string.IsNullOrWhiteSpace(search.Name)) throw new ArgumentException("Enter a name for the saved search.");
        search = search with { Name = search.Name.Trim() };
        Commit(data with { Searches = data.Searches.Where(s => !s.Name.Equals(search.Name, StringComparison.OrdinalIgnoreCase)).Append(search).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray() });
    }
    public void Delete(string name) => Commit(data with { Searches = data.Searches.Where(s => !s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray() });
    public bool IsPinned(string path) => data.Pins.Contains(path, StringComparer.OrdinalIgnoreCase);
    public void TogglePin(string path) => Commit(data with { Pins = IsPinned(path) ? data.Pins.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray() : [.. data.Pins, path] });
    public bool IsLaunchApproved(string path) => data.ApprovedLaunches?.Contains(path, StringComparer.OrdinalIgnoreCase) == true;
    public bool NeedsLaunchConfirmation(Entry entry) => !entry.IsDirectory && (LauncherCatalog.IsRunnable(entry.Path) || LauncherCatalog.IsAppShortcut(entry.Path)) && !IsLaunchApproved(entry.Path);
    public void ApproveLaunch(string path)
    {
        if (!IsLaunchApproved(path)) Commit(data with { ApprovedLaunches = [.. data.ApprovedLaunches ?? [], path] });
    }
    public void ForgetLaunchApproval(string path) => Commit(data with { ApprovedLaunches = (data.ApprovedLaunches ?? []).Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray() });
}

public static class LauncherCatalog
{
    public static Entry[] Load(IEnumerable<string> pins, Entry[]? indexed = null, CancellationToken token = default)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (indexed != null)
            for (var i = 0; i < indexed.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var entry = indexed[i];
                if (!entry.IsDirectory && IsRunnable(entry.Path)) entries[entry.Path] = entry;
            }
        var paths = new HashSet<string>(pins, StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var path in Directory.EnumerateFiles(root, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (IsAppShortcut(path)) paths.Add(path);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            if (!entries.ContainsKey(path)) entries[path] = FileIndex.ReadEntry(Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path)) ?? new Entry(path, false);
        }
        return entries.Values.ToArray();
    }
    public static bool IsPython(string path) => Path.GetExtension(path).Equals(".py", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".pyw", StringComparison.OrdinalIgnoreCase);
    public static bool IsRunnable(string path) => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || IsPython(path);
    public static bool IsAppShortcut(string path) => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);

    public static ProcessStartInfo StartInfo(Entry entry, string? python = null)
    {
        if (entry.IsDirectory || !IsPython(entry.Path)) return new(entry.Path) { UseShellExecute = true, WorkingDirectory = entry.Folder };
        if (python == null)
        {
            var candidates = Path.GetExtension(entry.Path).Equals(".pyw", StringComparison.OrdinalIgnoreCase) ? new[] { "pyw.exe", "pythonw.exe", "py.exe", "python.exe", "python3.exe" } : new[] { "py.exe", "python.exe", "python3.exe" };
            python = candidates.SelectMany(name => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(folder => Path.Combine(folder.Trim().Trim('"'), name))).FirstOrDefault(File.Exists);
            if (python == null) throw new InvalidOperationException("Install Python and add py.exe or python.exe to PATH to run Python scripts.");
        }
        var info = new ProcessStartInfo(python) { UseShellExecute = true, WorkingDirectory = entry.Folder };
        info.ArgumentList.Add(entry.Path);
        return info;
    }
}
