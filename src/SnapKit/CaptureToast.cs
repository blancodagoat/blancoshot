using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace SnapKit;

/// <summary>
/// The capture-complete card: borderless, top-most, never activated, bottom-right of the
/// primary display's working area. Fades in, holds (longer for errors, paused while
/// hovered), fades out. Clicking a success card reveals the file in Explorer. One
/// instance is reused — a new capture replaces the content and restarts the cycle
/// instead of stacking cards.
/// </summary>
internal sealed class CaptureToast : Form
{
    private const int WS_EX_TOOLWINDOW = 0x0000_0080;
    private const int WS_EX_NOACTIVATE = 0x0800_0000;

    private const int TickMs = 16;
    private const int FadeInMs = 150;
    private const int FadeOutMs = 300;
    private const int SuccessHoldMs = 3000;
    private const int ErrorHoldMs = 6000;

    // Logical pixels, scaled by the primary display's DPI at layout time.
    private const int EdgeMargin = 12;
    private const int Pad = 12;
    private const int BarWidth = 3;
    private const int LineGap = 4;
    private const int ThumbMaxW = 96;
    private const int ThumbMaxH = 64;
    private const int TextWidth = 190;
    private const int MinContentH = 40;

    private enum Phase
    {
        FadeIn,
        Hold,
        FadeOut,
    }

    private readonly System.Windows.Forms.Timer timer = new() { Interval = TickMs };
    private readonly Font titleFont = Theme.Ui(9.75f, FontStyle.Bold);
    private readonly Font detailFont = Theme.Ui(8.25f);

    private Phase phase;
    private int holdMs;
    private int holdRemainingMs;
    private Bitmap? thumbnail;
    private string title = "";
    private string body = "";
    private bool isError;
    private string? revealPath;

    public CaptureToast()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Background;
        Cursor = Cursors.Hand;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
        timer.Tick += (_, _) => OnTick();
    }

    /// <summary>Focus must stay wherever the user is typing or playing.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.TryRoundCorners(Handle);
    }

    /// <summary>Takes ownership of <paramref name="thumb"/>.</summary>
    public void ShowCapture(CaptureKind kind, string path, Bitmap thumb)
    {
        thumbnail?.Dispose();
        thumbnail = thumb;
        isError = false;
        revealPath = path;
        title = kind == CaptureKind.Region ? "Captured area" : "Captured display";
        body = Path.GetFileName(path) + SizeSuffix(path);
        Present(SuccessHoldMs);
    }

    public void ShowError(string message)
    {
        thumbnail?.Dispose();
        thumbnail = null;
        isError = true;
        revealPath = null;
        title = AppInfo.Name;
        body = message;
        Present(ErrorHoldMs);
    }

    /// <summary>An informational card: normal accent, normal hold, no thumbnail.</summary>
    public void ShowNotice(string message)
    {
        thumbnail?.Dispose();
        thumbnail = null;
        isError = false;
        revealPath = null;
        title = AppInfo.Name;
        body = message;
        Present(SuccessHoldMs);
    }

    private void Present(int duration)
    {
        _ = Handle; // Force creation so DeviceDpi is real before layout.

        var layout = Measure();
        // Always the primary display; per-capture-display placement can wait until
        // someone actually misses it.
        var work = Screen.PrimaryScreen?.WorkingArea ?? ScreenCapture.VirtualBounds;
        int margin = Scale(EdgeMargin);
        Bounds = new Rectangle(
            work.Right - layout.Total.Width - margin,
            work.Bottom - layout.Total.Height - margin,
            layout.Total.Width,
            layout.Total.Height);

        holdMs = duration;
        phase = Phase.FadeIn;
        Opacity = 0;
        if (!Visible)
        {
            Show();
        }

        Invalidate();
        timer.Start();
    }

    private void OnTick()
    {
        switch (phase)
        {
            case Phase.FadeIn:
                Opacity = Math.Min(1.0, Opacity + (double)TickMs / FadeInMs);
                if (Opacity >= 1.0)
                {
                    phase = Phase.Hold;
                    holdRemainingMs = holdMs;
                }

                break;

            case Phase.Hold:
                // Hovering pauses the countdown so the card can actually be read or clicked.
                if (!Bounds.Contains(Cursor.Position))
                {
                    holdRemainingMs -= TickMs;
                    if (holdRemainingMs <= 0)
                    {
                        phase = Phase.FadeOut;
                    }
                }

                break;

            case Phase.FadeOut:
                double next = Opacity - (double)TickMs / FadeOutMs;
                if (next <= 0)
                {
                    Dismiss();
                }
                else
                {
                    Opacity = next;
                }

                break;
        }
    }

    private void Dismiss()
    {
        timer.Stop();
        Hide();
    }

    /// <summary>
    /// Called at the start of every capture so the card never appears inside the shot it
    /// would otherwise be sitting on. Waits out one composition; costs at most a frame.
    /// </summary>
    public void HideForCapture()
    {
        if (!Visible)
        {
            return;
        }

        Dismiss();
        Native.DwmFlush();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && revealPath is not null)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{revealPath}\"");
            }
            catch
            {
                // A reveal that fails is not worth a second toast.
            }
        }

        Dismiss();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var layout = Measure();

        using (var border = new Pen(Theme.Border))
        {
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        using (var accent = new SolidBrush(isError ? Theme.Warning : Theme.Accent))
        {
            g.FillRectangle(accent, 0, 0, Scale(BarWidth), Height);
        }

        if (thumbnail is not null && !layout.Thumb.IsEmpty)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(thumbnail, layout.Thumb);
            using var frame = new Pen(Theme.Border);
            g.DrawRectangle(
                frame, layout.Thumb.X, layout.Thumb.Y, layout.Thumb.Width - 1, layout.Thumb.Height - 1);
        }

        TextRenderer.DrawText(
            g, title, titleFont, layout.Title, Theme.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            g, body, detailFont, layout.Body, isError ? Theme.Text : Theme.Dim,
            TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
    }

    private readonly record struct ToastLayout(Rectangle Thumb, Rectangle Title, Rectangle Body, Size Total);

    /// <summary>One layout used for both sizing (Present) and drawing (OnPaint).</summary>
    private ToastLayout Measure()
    {
        int pad = Scale(Pad);
        int gap = Scale(LineGap);
        int textW = Scale(TextWidth);

        var thumbSize = Size.Empty;
        if (thumbnail is not null)
        {
            double fit = Math.Min(
                1.0,
                Math.Min(
                    (double)Scale(ThumbMaxW) / thumbnail.Width,
                    (double)Scale(ThumbMaxH) / thumbnail.Height));
            thumbSize = new Size(
                Math.Max(1, (int)Math.Round(thumbnail.Width * fit)),
                Math.Max(1, (int)Math.Round(thumbnail.Height * fit)));
        }

        var titleSize = TextRenderer.MeasureText(title, titleFont);
        var bodySize = TextRenderer.MeasureText(
            body, detailFont, new Size(textW, int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);

        int textH = titleSize.Height + gap + bodySize.Height;
        int contentH = Math.Max(Scale(MinContentH), Math.Max(thumbSize.Height, textH));

        int x = Scale(BarWidth) + pad;
        var thumbRect = Rectangle.Empty;
        if (!thumbSize.IsEmpty)
        {
            thumbRect = new Rectangle(
                x, pad + (contentH - thumbSize.Height) / 2, thumbSize.Width, thumbSize.Height);
            x = thumbRect.Right + pad;
        }

        int textTop = pad + (contentH - textH) / 2;
        var titleRect = new Rectangle(x, textTop, textW, titleSize.Height);
        var bodyRect = new Rectangle(x, titleRect.Bottom + gap, textW, bodySize.Height);
        var total = new Size(x + textW + pad, pad * 2 + contentH);
        return new ToastLayout(thumbRect, titleRect, bodyRect, total);
    }

    private int Scale(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);

    private static string SizeSuffix(string path)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            string text = bytes < 1024 * 1024
                ? $"{Math.Max(1, bytes / 1024)} KB"
                : $"{bytes / (1024.0 * 1024.0):0.0} MB";
            return "  ·  " + text;
        }
        catch
        {
            return "";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            titleFont.Dispose();
            detailFont.Dispose();
            thumbnail?.Dispose();
        }

        base.Dispose(disposing);
    }
}
