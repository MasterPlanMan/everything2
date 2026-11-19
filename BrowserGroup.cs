using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Everything2;

public sealed record BrowserTarget(string Name, string Path);

public sealed class BrowserGroup(string sessionPath)
{
    private static readonly HashSet<string> Names = new(["msedge", "waterfox", "chrome", "brave"], StringComparer.OrdinalIgnoreCase);
    public static BrowserGroup Default { get; } = new(Path.Combine(AppContext.BaseDirectory, "data", "browser-session.json"));

    public void SaveTargets(IEnumerable<BrowserTarget> targets)
    {
        var valid = targets.Where(IsValid).DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (valid.Length == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
        File.WriteAllText(sessionPath + ".tmp", JsonSerializer.Serialize(valid));
        File.Move(sessionPath + ".tmp", sessionPath, true);
    }
    public BrowserTarget[] LoadTargets()
    {
        if (!File.Exists(sessionPath)) return [];
        return (JsonSerializer.Deserialize<BrowserTarget[]>(File.ReadAllText(sessionPath)) ?? []).Where(t => t != null && IsValid(t)).DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static bool IsValid(BrowserTarget target) => !string.IsNullOrWhiteSpace(target.Name) && !string.IsNullOrWhiteSpace(target.Path) && Names.Contains(target.Name) && Path.IsPathFullyQualified(target.Path) && Path.GetFileName(target.Path).Equals(target.Name + ".exe", StringComparison.OrdinalIgnoreCase);

    public static ProcessStartInfo StartInfo(BrowserTarget target)
    {
        if (!IsValid(target)) throw new ArgumentException("This item is not one of the configured browsers.");
        var info = new ProcessStartInfo(target.Path) { UseShellExecute = true };
        if (!target.Name.Equals("waterfox", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add("--restore-last-session");
        return info;
    }
    public void Close()
    {
        var processes = new Dictionary<uint, BrowserTarget>();
        foreach (var name in Names)
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is not { } path) continue;
                        processes[(uint)process.Id] = new(name, path);
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
                }
        var windows = new List<nint>();
        var targets = new List<BrowserTarget>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var id);
            if (processes.TryGetValue(id, out var target) && IsWindowVisible(window) && GetWindow(window, 4) == 0)
            {
                windows.Add(window); targets.Add(target);
            }
            return true;
        }, 0);
        if (windows.Count == 0) throw new InvalidOperationException("No open Edge, Waterfox, Chrome or Brave windows were found.");
        // Persist reopening information before asking any browser window to close.
        SaveTargets(targets);
        var failed = 0;
        foreach (var window in windows) if (!PostMessage(window, 0x0010, 0, 0)) failed++;
        if (failed > 0) throw new InvalidOperationException("Some browsers couldn't be asked to close. Their paths are saved for reopening. Close those windows manually.");
    }
    public void Reopen()
    {
        var targets = LoadTargets();
        if (targets.Length == 0) throw new InvalidOperationException("Use Close browsers first so Everything2 can remember which browser apps to reopen.");
        var failed = new List<string>();
        foreach (var target in targets)
        {
            try { Process.Start(StartInfo(target)); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { failed.Add(target.Name); }
        }
        if (failed.Count > 0) throw new InvalidOperationException("Couldn't reopen: " + string.Join(", ", failed));
    }

    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
