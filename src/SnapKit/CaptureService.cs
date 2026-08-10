using System.Drawing.Imaging;

namespace SnapKit;

internal enum CaptureKind
{
    Region,
    Display,
}

internal interface ICaptureNotifier
{
    /// <summary>The notifier takes ownership of <paramref name="thumbnail"/>.</summary>
    void Saved(string path, Bitmap thumbnail, CaptureKind kind);

    void Failed(string message);
}

/// <summary>
/// The capture pipeline: resolve source app, acquire the bitmap, save, copy, notify.
/// A failed save never costs the user the capture — the clipboard copy still happens.
/// </summary>
internal sealed class CaptureService
{
    private const int ClipboardAttempts = 3;
    private const int ClipboardRetryDelayMs = 80;

    private readonly AppConfig config;
    private readonly ICaptureNotifier notifier;
    private bool busy;

    public CaptureService(AppConfig config, ICaptureNotifier notifier)
    {
        this.config = config;
        this.notifier = notifier;
    }

    public void CaptureRegion()
    {
        // The overlay is modal; a second Print Screen while it is up must not stack another.
        if (busy)
        {
            return;
        }

        // First statement: the overlay is about to become the foreground window.
        var app = SourceApp.Resolve();

        busy = true;
        try
        {
            using var desktop = ScreenCapture.CaptureVirtualDesktop();
            using var overlay = new RegionOverlay(desktop);
            if (overlay.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            using var result = overlay.CreateResult();
            Deliver(result, app, CaptureKind.Region);
        }
        catch (Exception ex)
        {
            notifier.Failed(ex.Message);
        }
        finally
        {
            busy = false;
        }
    }

    public void CaptureActiveDisplay()
    {
        // Also guarded: with the region overlay modally open, its message loop still
        // dispatches WM_HOTKEY, and an unguarded display capture would photograph the
        // overlay's own dimmed screen.
        if (busy)
        {
            return;
        }

        var app = SourceApp.Resolve();

        busy = true;
        try
        {
            using var bitmap = ScreenCapture.CaptureRect(ScreenCapture.ActiveDisplayBounds());
            Deliver(bitmap, app, CaptureKind.Display);
        }
        catch (Exception ex)
        {
            notifier.Failed(ex.Message);
        }
        finally
        {
            busy = false;
        }
    }

    private void Deliver(Bitmap bitmap, string app, CaptureKind kind)
    {
        string? savedPath = null;
        string? saveError = null;

        try
        {
            savedPath = Save(bitmap, app);
        }
        catch (Exception ex)
        {
            saveError = ex.Message;
        }

        bool copied = CopyToClipboard(bitmap);

        if (savedPath is not null)
        {
            notifier.Saved(savedPath, MakeThumbnail(bitmap), kind);
        }
        else if (copied)
        {
            notifier.Failed($"Save failed, copied to clipboard instead. {saveError}");
        }
        else
        {
            notifier.Failed($"Capture lost — save and clipboard both failed. {saveError}");
        }
    }

    private string Save(Bitmap bitmap, string app)
    {
        var now = DateTime.Now;
        var folder = CaptureNaming.FolderFor(config.SaveRoot, now);
        Directory.CreateDirectory(folder);

        var path = CaptureNaming.BuildPath(folder, app, now);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>Small copy for the toast, made before the capture bitmap is disposed.</summary>
    private static Bitmap MakeThumbnail(Bitmap source)
    {
        const int maxW = 192, maxH = 120;
        double fit = Math.Min(1.0, Math.Min((double)maxW / source.Width, (double)maxH / source.Height));
        int w = Math.Max(1, (int)Math.Round(source.Width * fit));
        int h = Math.Max(1, (int)Math.Round(source.Height * fit));

        var thumb = new Bitmap(w, h);
        using var g = Graphics.FromImage(thumb);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, new Rectangle(0, 0, w, h));
        return thumb;
    }

    /// <summary>
    /// The Win32 clipboard is routinely held open by another process, and the first
    /// attempt fails often enough that a single try loses captures.
    /// </summary>
    internal static bool CopyToClipboard(Bitmap bitmap)
    {
        for (int attempt = 0; attempt < ClipboardAttempts; attempt++)
        {
            try
            {
                Clipboard.SetImage(bitmap);
                return true;
            }
            catch (Exception) when (attempt < ClipboardAttempts - 1)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
