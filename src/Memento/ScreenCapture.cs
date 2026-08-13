using System.Drawing.Imaging;

namespace Memento;

/// <summary>
/// Capture geometry. Under PerMonitorV2 the values returned by
/// <see cref="SystemInformation.VirtualScreen"/> and <see cref="Screen.Bounds"/> are already
/// physical pixels, so no scaling factor is ever applied on top of them.
/// </summary>
internal static class ScreenCapture
{
    public static Rectangle VirtualBounds => SystemInformation.VirtualScreen;

    public static Bitmap CaptureRect(Rectangle bounds)
    {
        // 24bpp on purpose: games write garbage into the desktop surface's alpha
        // channel (hair and edge shaders especially), CopyFromScreen copies it
        // verbatim, and a 32bpp PNG carries it out of the app — invisible in most
        // local viewers, white speckles wherever Discord composites the "holes".
        // A formatless capture bitmap plus a 24bpp target flattens it at the source,
        // so the file, the clipboard, and the overlay all get opaque pixels.
        var bitmap = new Bitmap(
            Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format24bppRgb);

        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static Bitmap CaptureVirtualDesktop() => CaptureRect(VirtualBounds);

    /// <summary>
    /// The display holding the foreground window — deliberately not the display under the
    /// cursor, which is often somewhere else entirely. Falls back to the primary display.
    /// </summary>
    public static Rectangle ActiveDisplayBounds()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return Screen.PrimaryScreen?.Bounds ?? VirtualBounds;
        }

        var monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (Native.GetMonitorInfoW(monitor, ref info))
            {
                var rect = info.rcMonitor.ToRectangle();
                var match = Screen.AllScreens.FirstOrDefault(s => s.Bounds == rect);
                return match?.Bounds ?? rect;
            }
        }

        return Screen.FromHandle(hwnd).Bounds;
    }
}
