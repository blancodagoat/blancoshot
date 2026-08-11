using System.Diagnostics;

namespace Memento;

/// <summary>
/// The app has no main window. The tray icon is the only entry point, and it owns the
/// hotkey window, the capture service and the settings window.
/// </summary>
internal sealed class TrayContext : ApplicationContext, ICaptureNotifier
{
    private const int TooltipLimit = 63; // NotifyIcon.Text rejects anything longer.

    private readonly AppConfig config;
    private readonly HotkeyWindow hotkeyWindow;
    private readonly HotkeyManager hotkeys;
    private readonly CaptureService capture;
    private readonly SystemTheme systemTheme;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem openLastItem;
    private readonly ToolStripMenuItem copyLastItem;

    private IReadOnlyList<HotkeyId> unavailable = Array.Empty<HotkeyId>();
    private SettingsForm? settings;
    private CaptureToast? toast;
    private string? lastCapture;

    public TrayContext(SingleInstance instance)
    {
        config = AppConfig.Load();
        StartupRegistry.Repair();
        capture = new CaptureService(config, this);

        hotkeyWindow = new HotkeyWindow();
        hotkeyWindow.HotkeyPressed += OnHotkey;
        hotkeyWindow.ShowSettingsRequested += OpenSettings;

        hotkeys = new HotkeyManager(hotkeyWindow, config);

        systemTheme = new SystemTheme();
        systemTheme.Changed += ApplyTrayIcon;

        openLastItem = new ToolStripMenuItem("Open last capture", null, (_, _) => OpenLastCapture())
        {
            Enabled = false,
        };
        copyLastItem = new ToolStripMenuItem("Copy last capture", null, (_, _) => CopyLastCapture())
        {
            Enabled = false,
        };

        tray = new NotifyIcon
        {
            Icon = TrayIcons.ForTaskbar(systemTheme.LightTaskbar),
            Text = AppInfo.Name,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        tray.DoubleClick += (_, _) => OpenSettings();

        instance.ListenForSignals(hotkeyWindow.Handle);

        unavailable = hotkeys.RegisterAll();

        // Both of these wait for first idle: a balloon shown before Application.Run has a
        // message loop is unreliable, and the modal notice would otherwise open a nested
        // loop from inside the constructor.
        Application.Idle += OnFirstIdle;
    }

    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;

        if (unavailable.Count > 0)
        {
            var names = unavailable.Select(id => id == HotkeyId.Region ? "region capture" : "full display");
            Failed($"Shortcut unavailable for {string.Join(" and ", names)}. Pick another in Settings.");
        }

        if (PrintScreenNotice.ShowIfNeeded(config) && !Native.NotificationsSuppressed())
        {
            Toast.ShowNotice("Print Screen reclaimed from Windows Snipping Tool.");
        }

        CaptureNaming.NormaliseMonthFolders(config.SaveRoot);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Theme.Background,
            ForeColor = Theme.Text,
            Font = Theme.Ui(9.5f),
            ShowImageMargin = false,
        };

        menu.Items.Add("Capture region", null, (_, _) => AfterMenuCloses(() => RunCapture(capture.CaptureRegion)));
        menu.Items.Add("Capture display", null, (_, _) => AfterMenuCloses(() => RunCapture(capture.CaptureActiveDisplay)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openLastItem);
        menu.Items.Add(copyLastItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Open screenshots folder", null, (_, _) => OpenScreenshotsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        return menu;
    }

    private void OnHotkey(HotkeyId id)
    {
        switch (id)
        {
            case HotkeyId.Region:
                RunCapture(capture.CaptureRegion);
                break;

            case HotkeyId.FullDisplay:
                RunCapture(capture.CaptureActiveDisplay);
                break;
        }
    }

    /// <summary>A still-visible toast from the previous capture must not end up in this one.</summary>
    private void RunCapture(Action action)
    {
        toast?.HideForCapture();
        action();
    }

    private void ApplyTrayIcon() => tray.Icon = TrayIcons.ForTaskbar(systemTheme.LightTaskbar);

    private void OpenSettings()
    {
        settings ??= new SettingsForm(config, hotkeys);
        settings.ShowSettings();
    }

    private void OpenScreenshotsFolder()
    {
        try
        {
            Directory.CreateDirectory(config.SaveRoot);
            Process.Start(new ProcessStartInfo(config.SaveRoot) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Failed($"Could not open {config.SaveRoot}. {ex.Message}");
        }
    }

    public void Saved(string path, Bitmap thumbnail, CaptureKind kind)
    {
        lastCapture = path;
        openLastItem.Enabled = copyLastItem.Enabled = true;
        tray.Text = Truncate(AppInfo.Name);

        if (Native.NotificationsSuppressed())
        {
            thumbnail.Dispose();
            return;
        }

        Toast.ShowCapture(kind, path, thumbnail);
    }

    public void Failed(string message)
    {
        // The tooltip keeps the error visible after the toast has gone.
        tray.Text = Truncate($"{AppInfo.Name} — {message}");

        if (Native.NotificationsSuppressed())
        {
            // The toast must not appear over a game or during quiet hours, but an error
            // cannot vanish either — a balloon is queued into the notification center
            // for later instead of being shown now.
            tray.ShowBalloonTip(4000, AppInfo.Name, message, ToolTipIcon.Warning);
            return;
        }

        Toast.ShowError(message);
    }

    private CaptureToast Toast => toast ??= new CaptureToast();

    /// <summary>
    /// Menu-triggered captures are deferred a beat so the menu is gone and foreground has
    /// returned to the user's window before source-app resolution and the overlay run.
    /// </summary>
    private static void AfterMenuCloses(Action action)
    {
        var delay = new System.Windows.Forms.Timer { Interval = 150 };
        delay.Tick += (_, _) =>
        {
            delay.Dispose();
            action();
        };
        delay.Start();
    }

    private void OpenLastCapture()
    {
        if (lastCapture is null || !File.Exists(lastCapture))
        {
            Failed("Last capture is gone from disk.");
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{lastCapture}\"");
        }
        catch (Exception ex)
        {
            Failed($"Could not open Explorer. {ex.Message}");
        }
    }

    private void CopyLastCapture()
    {
        if (lastCapture is null || !File.Exists(lastCapture))
        {
            Failed("Last capture is gone from disk.");
            return;
        }

        try
        {
            using var image = new Bitmap(lastCapture);
            if (!CaptureService.CopyToClipboard(image))
            {
                Failed("Clipboard is held by another app. Try again.");
            }
        }
        catch (Exception ex)
        {
            Failed($"Could not copy. {ex.Message}");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= TooltipLimit ? value : value[..(TooltipLimit - 1)] + "…";

    private void Quit()
    {
        tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The SingleInstance handle is owned by Main and released there.
            Application.Idle -= OnFirstIdle;
            tray.Visible = false;
            tray.Dispose();
            toast?.Dispose();
            settings?.Dispose();
            systemTheme.Changed -= ApplyTrayIcon;
            systemTheme.Dispose();
            hotkeyWindow.Dispose();
        }

        base.Dispose(disposing);
    }
}
