using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Everything2;

public enum DesktopActionKind { Open, Lock, Sleep, Restart, ShutDown, CloseBrowsers, ReopenBrowsers }

public sealed record DesktopAction(string Name, string Category, string Description, string Target, string Keywords = "", DesktopActionKind Kind = DesktopActionKind.Open)
{
    public Entry Entry => new($@"Actions\{Category}\{Name}", false);
    public bool RequiresConfirmation => Kind is DesktopActionKind.Restart or DesktopActionKind.ShutDown or DesktopActionKind.Sleep or DesktopActionKind.CloseBrowsers;
    public Entry IconEntry => Kind == DesktopActionKind.Open && Path.IsPathFullyQualified(Target) ? new(Target, Directory.Exists(Target)) : new(Path.Combine(Environment.SystemDirectory, "control.exe"), false);
    public ProcessStartInfo StartInfo()
    {
        var info = new ProcessStartInfo(Kind is DesktopActionKind.Restart or DesktopActionKind.ShutDown ? Path.Combine(Environment.SystemDirectory, "shutdown.exe") : Target) { UseShellExecute = true };
        if (Kind is DesktopActionKind.Restart or DesktopActionKind.ShutDown)
        {
            info.ArgumentList.Add(Kind == DesktopActionKind.Restart ? "/r" : "/s");
            info.ArgumentList.Add("/t"); info.ArgumentList.Add("0");
        }
        return info;
    }
}

public static class ActionCatalog
{
    private static string SystemTool(string name) => Path.Combine(Environment.SystemDirectory, name);
    public static IReadOnlyList<DesktopAction> All { get; } = Array.AsReadOnly(new DesktopAction[]
    {
        new("Windows Settings", "Settings", "Open Windows Settings", "ms-settings:", "preferences configuration"),
        new("Sound settings", "Settings", "Choose speakers, microphone and volume", "ms-settings:sound", "audio volume microphone speakers headphones"),
        new("Display settings", "Settings", "Manage monitors, resolution, scaling and brightness", "ms-settings:display", "screen monitor resolution brightness"),
        new("Bluetooth settings", "Settings", "Connect or manage Bluetooth devices", "ms-settings:bluetooth", "wireless devices pairing"),
        new("Network settings", "Settings", "Manage network and internet connections", "ms-settings:network", "wifi wi-fi ethernet internet"),
        new("Installed apps", "Settings", "View and uninstall installed applications", "ms-settings:appsfeatures", "programs applications uninstall remove"),
        new("Control Panel", "Settings", "Open the classic Windows Control Panel", SystemTool("control.exe"), "classic configuration"),
        new("Volume mixer", "Settings", "Mute other apps or adjust their volume while keeping Teams audible", "ms-settings:apps-volume", "teams meeting mute unmute sound audio applications"),
        new("Notifications and Do Not Disturb", "Settings", "Manage notification interruptions during meetings", "ms-settings:notifications", "teams meeting focus quiet notifications disturb"),
        new("Startup apps", "Settings", "Manage apps that start when you sign in", "ms-settings:startupapps", "boot startup performance memory"),
        new("Power settings", "Settings", "Manage power, screen-off and sleep settings", "ms-settings:powersleep", "battery energy sleep screen"),
        new("Storage cleanup", "Cleanup", "Review storage usage and Windows cleanup options before deleting files", "ms-settings:storagesense", "disk space temp temporary cleanup clean"),
        new("Disk Cleanup", "Cleanup", "Open Disk Cleanup to choose files to remove", SystemTool("cleanmgr.exe"), "disk space temp temporary cleanup clean"),
        new("Temporary files folder", "Cleanup", "Open your temporary folder to review its files", Path.GetTempPath(), "disk space temp temporary cleanup clean files"),
        new("Task Manager", "Utilities", "Inspect running processes and resource usage", SystemTool("Taskmgr.exe"), "process cpu memory performance tasks"),
        new("Calculator", "Utilities", "Open Calculator", SystemTool("calc.exe"), "calculate maths math"),
        new("Screenshot tool", "Utilities", "Open Snipping Tool to capture the screen", SystemTool("SnippingTool.exe"), "snip capture screenshot screen grab"),
        new("Close browsers", "Browsers", "Ask Edge, Waterfox, Chrome and Brave windows to close normally", "", "memory ram browsers edge waterfox chrome brave close", DesktopActionKind.CloseBrowsers),
        new("Reopen browsers", "Browsers", "Reopen the browser apps remembered by Close browsers", "", "restore session tabs browsers edge waterfox chrome brave reopen", DesktopActionKind.ReopenBrowsers),
        new("Downloads", "Folders", "Open your Downloads folder", "shell:Downloads", "download files"),
        new("Documents", "Folders", "Open your Documents folder", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "docs files"),
        new("Recycle Bin", "Folders", "Open the Recycle Bin", "shell:RecycleBinFolder", "trash deleted restore"),
        new("Lock computer", "Computer", "Lock your current Windows session", "", "lock workstation pc", DesktopActionKind.Lock),
        new("Sleep computer", "Computer", "Put this PC to sleep (confirmation required)", "", "sleep suspend standby power", DesktopActionKind.Sleep),
        new("Restart computer", "Computer", "Restart this PC (confirmation required)", "", "reboot restart power", DesktopActionKind.Restart),
        new("Shut down computer", "Computer", "Turn off this PC (confirmation required)", "", "shutdown shut down power off", DesktopActionKind.ShutDown)
    });

    public static DesktopAction? Find(Entry entry) => All.FirstOrDefault(a => a.Entry.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase));
    public static SearchResult Search(string text, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var terms = Regex.Matches(text, "(?:[^\\s\"]|\"[^\"]*\")+").Select(m => m.Value.Replace("\"", "")).ToArray();
        var entries = new List<Entry>();
        foreach (var action in All)
        {
            token.ThrowIfCancellationRequested();
            var content = $"{action.Name} {action.Category} {action.Description} {action.Keywords}";
            if (terms.All(term => term.StartsWith('!') ? !content.Contains(term[1..], StringComparison.OrdinalIgnoreCase) : content.Contains(term, StringComparison.OrdinalIgnoreCase))) entries.Add(action.Entry);
        }
        return new(entries.ToArray(), entries.Count, clock.Elapsed.TotalMilliseconds);
    }

    // Keep confirmation in the execution path so keyboard, mouse and menu activation share it.
    public static bool Run(DesktopAction action, Func<DesktopAction, bool> confirm, Action<DesktopAction>? execute = null)
    {
        if (action.RequiresConfirmation && !confirm(action)) return false;
        (execute ?? Execute)(action);
        return true;
    }
    private static void Execute(DesktopAction action)
    {
        switch (action.Kind)
        {
            case DesktopActionKind.CloseBrowsers: BrowserGroup.Default.Close(); break;
            case DesktopActionKind.ReopenBrowsers: BrowserGroup.Default.Reopen(); break;
            case DesktopActionKind.Lock:
                if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
                break;
            case DesktopActionKind.Sleep:
                if (!Application.SetSuspendState(PowerState.Suspend, false, false)) throw new InvalidOperationException("Windows couldn't put this PC to sleep.");
                break;
            default: Process.Start(action.StartInfo()); break;
        }
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
