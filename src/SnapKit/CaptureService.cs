using System.Drawing.Imaging;

namespace SnapKit;

internal interface ICaptureNotifier
{
    void Saved(string path);

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
            Deliver(result, app);
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
        var app = SourceApp.Resolve();

        try
        {
            using var bitmap = ScreenCapture.CaptureRect(ScreenCapture.ActiveDisplayBounds());
            Deliver(bitmap, app);
        }
        catch (Exception ex)
        {
            notifier.Failed(ex.Message);
        }
    }

    private void Deliver(Bitmap bitmap, string app)
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
            notifier.Saved(savedPath);
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

    /// <summary>
    /// The Win32 clipboard is routinely held open by another process, and the first
    /// attempt fails often enough that a single try loses captures.
    /// </summary>
    private static bool CopyToClipboard(Bitmap bitmap)
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
