using System.Runtime.InteropServices;
using System.Drawing.Imaging;

namespace Everything2;

// Shell work runs off the UI thread and only visible rows request icons.
internal sealed class ResultIcons : IDisposable
{
    private readonly Control owner;
    private readonly Dictionary<string, int> indices = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim workers = new(4);
    private readonly bool large;
    private volatile bool disposed;
    public ImageList Images { get; } = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = SystemInformation.SmallIconSize };
    public event Action? Updated;

    public ResultIcons(Control owner, bool large = false)
    {
        this.owner = owner;
        this.large = large;
        if (large) Images.ImageSize = new Size(32, 32);
        using var fallback = SystemIcons.Application.ToBitmap();
        Images.Images.Add(fallback);
        // Force native storage before disposing the source bitmap.
        _ = Images.Handle;
    }

    public int GetIndex(Entry entry)
    {
        if (disposed) return 0;
        var extension = Path.GetExtension(entry.Path);
        var specificIcon = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || LauncherCatalog.IsAppShortcut(entry.Path) || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
        var key = entry.IsDirectory ? "folder" : specificIcon ? entry.Path + "|" + entry.ModifiedUtc?.Ticks : "ext:" + extension;
        if (indices.TryGetValue(key, out var index)) return index;
        // Bound the native image cache for long-running tray sessions.
        if (indices.Count >= 4096) return 0;
        indices[key] = 0;
        _ = Load(key, entry);
        return 0;
    }

    private async Task Load(string key, Entry entry)
    {
        try
        {
            await workers.WaitAsync().ConfigureAwait(false);
            byte[]? png;
            try { png = disposed ? null : await Task.Run(() => ReadIcon(entry, large)).ConfigureAwait(false); }
            finally { workers.Release(); }
            if (png == null || disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
            owner.BeginInvoke(() =>
            {
                if (disposed || owner.IsDisposed) return;
                using var stream = new MemoryStream(png);
                using var bitmap = Image.FromStream(stream);
                Images.Images.Add(bitmap);
                indices[key] = Images.Images.Count - 1;
                Updated?.Invoke();
                owner.Invalidate();
            });
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or ExternalException) { }
    }

    internal static byte[]? ReadIcon(Entry entry, bool large = false)
    {
        var initialized = CoInitializeEx(0, 0) >= 0;
        var info = new ShellFileInfo();
        try
        {
            const uint icon = 0x100, smallIcon = 0x1, useAttributes = 0x10;
            var flags = icon | (large ? 0 : smallIcon);
            // File-type and folder icons need no disk access; EXEs/shortcuts use their own icon.
            if (entry.IsDirectory || (!LauncherCatalog.IsAppShortcut(entry.Path) && !Path.GetExtension(entry.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(entry.Path).Equals(".ico", StringComparison.OrdinalIgnoreCase))) flags |= useAttributes;
            if (SHGetFileInfo(entry.Path, entry.IsDirectory ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags) == 0 || info.Icon == 0) return null;
            using var shellIcon = Icon.FromHandle(info.Icon);
            using var bitmap = shellIcon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            if (info.Icon != 0) DestroyIcon(info.Icon);
            if (initialized) CoUninitialize();
        }
    }

    public void Dispose() { disposed = true; Images.Dispose(); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
