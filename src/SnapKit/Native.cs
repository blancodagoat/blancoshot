using System.Runtime.InteropServices;

namespace SnapKit;

/// <summary>All Win32 interop for the app. No third-party dependencies.</summary>
internal static class Native
{
    // ---- messages -------------------------------------------------------
    public const int WM_HOTKEY = 0x0312;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_DPICHANGED = 0x02E0;

    /// <summary>Posted to the hotkey window by the single-instance watcher.</summary>
    public const int WM_APP_SHOW_SETTINGS = 0x0400 + 17;

    public static readonly IntPtr HWND_MESSAGE = new(-3);

    // ---- hotkey modifiers ----------------------------------------------
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int VK_SNAPSHOT = 0x2C;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // ---- window positioning --------------------------------------------
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>True while the Windows key is physically held down.</summary>
    public static bool IsWinKeyDown() =>
        (GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0;

    /// <summary>Best-effort dark title bar. Attribute 20 on current builds, 19 on older Win10.</summary>
    public static void TryUseDarkTitleBar(IntPtr hWnd)
    {
        int on = 1;
        if (DwmSetWindowAttribute(hWnd, 20, ref on, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hWnd, 19, ref on, sizeof(int));
        }
    }
}
