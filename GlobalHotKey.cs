using System.Runtime.InteropServices;

namespace Everything2;

internal static class GlobalHotKey
{
    public const int Id = 0x452;
    public const int Message = 0x0312;
    public static bool Register(nint handle) => RegisterHotKey(handle, Id, 0x4000 | 0x0002, (uint)Keys.Space);
    public static void Unregister(nint handle) => UnregisterHotKey(handle, Id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
