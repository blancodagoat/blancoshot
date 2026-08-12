// Live-desktop smoke: proves capture is pixel-exact on every attached display and that
// an overlay drag lands exactly where the cursor went, including across a DPI boundary
// when one exists. Run on a machine with two differently-scaled displays to cover the
// mixed-DPI checklist item; anywhere else it still verifies the geometry end to end.
//
//   dotnet run --project tests/Memento.Tests -- smoke
//
// Needs an unlocked, awake desktop; it moves the real cursor for about two seconds.

using System.Runtime.InteropServices;

namespace Memento.Tests;

internal static class SmokeTest
{
    // Saturated and far apart so a night-light tint cannot make one read as another.
    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 0, 255), Color.FromArgb(0, 255, 255),
        Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 0),
    };

    public static void Run(Action<string, bool, string?> check)
    {
        var screens = Screen.AllScreens;
        var dpis = new uint[screens.Length];
        for (int i = 0; i < screens.Length; i++)
        {
            dpis[i] = EffectiveDpi(screens[i]);
            Console.WriteLine(
                $"      {screens[i].DeviceName}  {screens[i].Bounds.Width}x{screens[i].Bounds.Height} @ {dpis[i] * 100 / 96}%");
        }

        Console.WriteLine(dpis.Distinct().Count() > 1
            ? "      mixed DPI present — this run covers the mixed-DPI verification"
            : "      single DPI — geometry exercised, but the mixed-DPI path is not");

        var restore = Cursor.Position;
        try
        {
            for (int i = 0; i < screens.Length; i++)
            {
                var name = screens[i].DeviceName.TrimStart('\\', '.');
                var color = Palette[i % Palette.Length];
                var marker = CenteredRect(screens[i].Bounds, 240, 140);

                using var form = ShowMarker(marker, color);
                if (!WaitForPixel(marker, color))
                {
                    check($"{name} marker appears", false, "never became visible; screen locked or asleep?");
                    continue;
                }

                CheckMarkerEdges(check, name, marker, color);
                DragTest(check, $"{name} drag", marker.Location,
                    new Point(marker.Right, marker.Bottom), color, marker.Size);
            }

            if (screens.Length > 1)
            {
                DragTest(check, "cross-display drag",
                    Center(screens[0].Bounds), Center(screens[1].Bounds), null, default);
            }
        }
        finally
        {
            Cursor.Position = restore;
        }
    }

    /// <summary>
    /// The four inner corners and the center must show the marker color; one pixel
    /// diagonally outside each corner must not. Any off-by-one in form placement or
    /// capture geometry on that display flips one of the probes.
    /// </summary>
    private static void CheckMarkerEdges(Action<string, bool, string?> check, string name, Rectangle marker, Color color)
    {
        using var shot = ScreenCapture.CaptureRect(Rectangle.Inflate(marker, 1, 1));
        Color At(int x, int y) => shot.GetPixel(x + 1 - marker.X, y + 1 - marker.Y);

        var inside = new[]
        {
            new Point(marker.Left, marker.Top), new Point(marker.Right - 1, marker.Top),
            new Point(marker.Left, marker.Bottom - 1), new Point(marker.Right - 1, marker.Bottom - 1),
            Center(marker),
        };
        var outside = new[]
        {
            new Point(marker.Left - 1, marker.Top - 1), new Point(marker.Right, marker.Top - 1),
            new Point(marker.Left - 1, marker.Bottom), new Point(marker.Right, marker.Bottom),
        };

        check($"{name} capture pixel-exact",
            inside.All(p => Matches(At(p.X, p.Y), color)) && outside.All(p => !Matches(At(p.X, p.Y), color)),
            $"marker at {marker} does not edge exactly where captured");
    }

    private static void DragTest(
        Action<string, bool, string?> check, string name, Point from, Point to, Color? expectColor, Size expectSize)
    {
        using var desktop = ScreenCapture.CaptureVirtualDesktop();
        using var overlay = new RegionOverlay(desktop);
        var expected = Rectangle.FromLTRB(
            Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Max(from.X, to.X), Math.Max(from.Y, to.Y));

        overlay.Shown += (_, _) =>
        {
            var handle = overlay.Handle;
            void CloseOverlay()
            {
                try { overlay.BeginInvoke(overlay.Close); } catch { /* already closed */ }
            }
            new Thread(() => Drive(handle, from, to, CloseOverlay)) { IsBackground = true }.Start();
        };

        var result = overlay.ShowDialog();
        check($"{name} commits", result == DialogResult.OK, $"dialog result {result}; input never reached the overlay?");
        if (result != DialogResult.OK)
        {
            return;
        }

        check($"{name} selection exact", overlay.SelectedRegion == expected,
            $"expected {expected}, got {overlay.SelectedRegion}");

        if (expectColor is { } c)
        {
            using var crop = overlay.CreateResult();
            check($"{name} result size", crop.Size == expectSize, $"expected {expectSize}, got {crop.Size}");
            var px = crop.GetPixel(crop.Width / 2, crop.Height / 2);
            check($"{name} result pixels", Matches(px, c), $"expected ~{c}, got {px}");
        }
    }

    /// <summary>Runs off the UI thread; the overlay pumps its own modal loop meanwhile.</summary>
    private static void Drive(IntPtr overlay, Point from, Point to, Action closeOverlay)
    {
        for (int i = 0; i < 100 && Native.GetForegroundWindow() != overlay; i++)
        {
            Thread.Sleep(50);
        }

        if (Native.GetForegroundWindow() != overlay)
        {
            closeOverlay(); // never click into whatever else is focused
            return;
        }

        SetCursorPos(from.X, from.Y);
        Thread.Sleep(80);
        Button(MOUSEEVENTF_LEFTDOWN);
        for (int step = 1; step <= 4; step++)
        {
            Thread.Sleep(40);
            SetCursorPos(from.X + (to.X - from.X) * step / 4, from.Y + (to.Y - from.Y) * step / 4);
        }

        Thread.Sleep(80);
        Button(MOUSEEVENTF_LEFTUP);

        Thread.Sleep(3000);
        closeOverlay(); // watchdog: a failed drag becomes a failed check, not a hang
    }

    private static Form ShowMarker(Rectangle bounds, Color color)
    {
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = color,
            Bounds = bounds,
        };
        form.Show();
        form.Bounds = bounds; // re-assert in case creation adjusted it
        return form;
    }

    private static bool WaitForPixel(Rectangle marker, Color color)
    {
        for (int i = 0; i < 60; i++)
        {
            Application.DoEvents();
            using var shot = ScreenCapture.CaptureRect(new Rectangle(Center(marker), new Size(1, 1)));
            if (Matches(shot.GetPixel(0, 0), color))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    // Tolerant of color-shifting filters (night light), still far tighter than the palette spacing.
    private static bool Matches(Color a, Color b) =>
        Math.Abs(a.R - b.R) < 60 && Math.Abs(a.G - b.G) < 60 && Math.Abs(a.B - b.B) < 60;

    private static Rectangle CenteredRect(Rectangle outer, int w, int h) =>
        new(outer.X + (outer.Width - w) / 2, outer.Y + (outer.Height - h) / 2, w, h);

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static uint EffectiveDpi(Screen screen)
    {
        var monitor = MonitorFromPoint(new PT { X = Center(screen.Bounds).X, Y = Center(screen.Bounds).Y }, 2);
        return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX : 96;
    }

    // Test-only interop; the app's own interop stays in src/Memento/Native.cs.
    private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    private const uint MOUSEEVENTF_LEFTUP = 0x04;

    private static void Button(uint flag)
    {
        var input = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = flag } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(PT pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
