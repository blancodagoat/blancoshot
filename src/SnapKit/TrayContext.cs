using System.Diagnostics;

namespace SnapKit;

/// <summary>
/// The app has no main window. The tray icon is the only entry point, and it owns the
/// hotkey window, the capture service and the settings window.
/// </summary>
internal sealed class TrayContext : ApplicationContext, ICaptureNotifier
{
    private const int BalloonMs = 2000;
    private const int TooltipLimit = 63; // NotifyIcon.Text rejects anything longer.

    private readonly AppConfig config;
    private readonly HotkeyWindow hotkeyWindow;
    private readonly HotkeyManager hotkeys;
    private readonly CaptureService capture;
    private readonly SystemTheme systemTheme;
    private readonly NotifyIcon tray;

    private IReadOnlyList<HotkeyId> unavailable = Array.Empty<HotkeyId>();
    private SettingsForm? settings;

    public TrayContext(SingleInstance instance)
    {
        config = AppConfig.Load();
        capture = new CaptureService(config, this);

        hotkeyWindow = new HotkeyWindow();
        hotkeyWindow.HotkeyPressed += OnHotkey;
        hotkeyWindow.ShowSettingsRequested += OpenSettings;

        hotkeys = new HotkeyManager(hotkeyWindow, config);

        systemTheme = new SystemTheme();
        systemTheme.Changed += ApplyTrayIcon;

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

        PrintScreenNotice.ShowIfNeeded(config);
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
                capture.CaptureRegion();
                break;

            case HotkeyId.FullDisplay:
                capture.CaptureActiveDisplay();
                break;
        }
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

    public void Saved(string path)
    {
        tray.Text = Truncate(AppInfo.Name);
        tray.ShowBalloonTip(BalloonMs, AppInfo.Name, Path.GetFileName(path), ToolTipIcon.None);
    }

    public void Failed(string message)
    {
        // The tooltip keeps the error visible after the balloon has gone.
        tray.Text = Truncate($"{AppInfo.Name} — {message}");
        tray.ShowBalloonTip(BalloonMs * 2, AppInfo.Name, message, ToolTipIcon.Warning);
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
            settings?.Dispose();
            systemTheme.Changed -= ApplyTrayIcon;
            systemTheme.Dispose();
            hotkeyWindow.Dispose();
        }

        base.Dispose(disposing);
    }
}
