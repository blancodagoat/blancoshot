using System.Diagnostics;

namespace BlancoShot;

/// <summary>
/// Every edit applies and persists immediately. There is no Save button, no dirty
/// tracking and no confirmation; the only button is Close, which hides to tray.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppConfig config;
    private readonly HotkeyManager hotkeys;

    private readonly HotkeyBox regionBox = new();
    private readonly HotkeyBox fullBox = new();
    private readonly Label regionWarning = new();
    private readonly Label fullWarning = new();
    private readonly TextBox saveRootBox = new();
    private readonly Label previewLabel = new();
    private readonly CheckBox startupCheck = new();

    private bool loading;

    public SettingsForm(AppConfig config, HotkeyManager hotkeys)
    {
        this.config = config;
        this.hotkeys = hotkeys;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = AppInfo.Name;
        ClientSize = new Size(420, 380);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9.5f);
        Icon = TrayIcons.CreateWindowIcon();

        BuildLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.TryUseDarkTitleBar(Handle);
    }

    private void BuildLayout()
    {
        const int left = 22;
        const int right = 22;
        int contentWidth = ClientSize.Width - left - right;
        const int fieldLeft = 160;
        int fieldWidth = ClientSize.Width - right - fieldLeft;

        Controls.Add(SectionLabel("HOTKEYS", left, 16, contentWidth));

        Controls.Add(FieldLabel("Region capture", left, 42));
        regionBox.SetBounds(fieldLeft, 38, fieldWidth, 26);
        regionBox.Binding = config.RegionHotkey;
        regionBox.Recorded = candidate => Apply(HotkeyId.Region, candidate, regionWarning);
        regionBox.RecordingChanged = OnRecordingChanged;
        Controls.Add(regionBox);

        ConfigureWarning(regionWarning, fieldLeft, 66, fieldWidth);
        Controls.Add(regionWarning);

        Controls.Add(FieldLabel("Full display", left, 92));
        fullBox.SetBounds(fieldLeft, 88, fieldWidth, 26);
        fullBox.Binding = config.FullDisplayHotkey;
        fullBox.Recorded = candidate => Apply(HotkeyId.FullDisplay, candidate, fullWarning);
        fullBox.RecordingChanged = OnRecordingChanged;
        Controls.Add(fullBox);

        ConfigureWarning(fullWarning, fieldLeft, 116, fieldWidth);
        Controls.Add(fullWarning);

        Controls.Add(SectionLabel("SAVE LOCATION", left, 146, contentWidth));

        // Read-only on purpose: a hand-typed path that only fails at capture time is the
        // worst failure this app can have.
        saveRootBox.SetBounds(left, 170, contentWidth - 90, 26);
        saveRootBox.ReadOnly = true;
        saveRootBox.BorderStyle = BorderStyle.FixedSingle;
        saveRootBox.BackColor = Theme.Field;
        saveRootBox.ForeColor = Theme.Text;
        saveRootBox.Text = config.SaveRoot;
        Controls.Add(saveRootBox);

        var browse = FlatButton("Browse…", ClientSize.Width - right - 82, 170, 82, 26);
        browse.Click += OnBrowse;
        Controls.Add(browse);

        previewLabel.SetBounds(left, 204, contentWidth, 32);
        previewLabel.ForeColor = Theme.Dim;
        previewLabel.Font = Theme.Ui(8.25f);
        Controls.Add(previewLabel);

        Controls.Add(SectionLabel("STARTUP", left, 248, contentWidth));

        startupCheck.SetBounds(left, 272, contentWidth, 24);
        startupCheck.Text = "Start with Windows";
        startupCheck.FlatStyle = FlatStyle.Flat;
        startupCheck.ForeColor = Theme.Text;
        startupCheck.BackColor = Theme.Background;
        startupCheck.CheckedChanged += OnStartupChanged;
        Controls.Add(startupCheck);

        var footer = new LinkLabel
        {
            Text = $"{AppInfo.Name} · GitHub · Portfolio",
            Font = Theme.Ui(8.25f),
            ForeColor = Theme.Dim,
            LinkColor = Theme.Dim,
            ActiveLinkColor = Theme.Accent,
            VisitedLinkColor = Theme.Dim,
            LinkBehavior = LinkBehavior.HoverUnderline,
            AutoSize = false,
        };
        footer.SetBounds(left, 312, contentWidth, 18);
        footer.Links.Clear();
        footer.Links.Add(
            footer.Text.IndexOf("GitHub", StringComparison.Ordinal), "GitHub".Length, AppInfo.GitHubUrl);
        footer.Links.Add(
            footer.Text.IndexOf("Portfolio", StringComparison.Ordinal), "Portfolio".Length, AppInfo.PortfolioUrl);
        footer.LinkClicked += (_, e) => OpenUrl((string)e.Link!.LinkData!);
        Controls.Add(footer);

        var close = FlatButton("Close", ClientSize.Width - right - 82, 338, 82, 28);
        close.Click += (_, _) => Hide();
        Controls.Add(close);

        CancelButton = close;
        UpdatePreview();
    }

    private static Label SectionLabel(string text, int x, int y, int width)
    {
        var label = new Label
        {
            Text = text,
            ForeColor = Theme.Accent,
            Font = Theme.Ui(8f, FontStyle.Bold),
            AutoSize = false,
        };
        label.SetBounds(x, y, width, 16);
        return label;
    }

    private static Label FieldLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            ForeColor = Theme.Dim,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        label.SetBounds(x, y, 130, 20);
        return label;
    }

    private static void ConfigureWarning(Label label, int x, int y, int width)
    {
        label.Text = string.Empty;
        label.ForeColor = Theme.Warning;
        label.Font = Theme.Ui(8.25f);
        label.AutoSize = false;
        label.Visible = false;
        label.SetBounds(x, y, width, 16);
    }

    private static Button FlatButton(string text, int x, int y, int width, int height)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.MouseOverBackColor = Theme.FieldHover;
        button.FlatAppearance.MouseDownBackColor = Theme.FieldHover;
        button.SetBounds(x, y, width, height);
        return button;
    }

    private void OnRecordingChanged(bool recording)
    {
        if (recording)
        {
            hotkeys.Suspend();
        }
        else
        {
            hotkeys.Resume();
        }
    }

    private bool Apply(HotkeyId id, HotkeyBinding candidate, Label warning)
    {
        if (hotkeys.TryRebind(id, candidate))
        {
            warning.Visible = false;
            warning.Text = string.Empty;

            // Rebinding to Print Screen must take the key from Snipping Tool now, not at
            // the next launch.
            if (candidate.Key == Keys.PrintScreen)
            {
                PrintScreenNotice.ShowIfNeeded(config);
            }

            return true;
        }

        warning.Text = "That shortcut is in use by another app.";
        warning.Visible = true;
        return false;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Where should screenshots be saved?",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        if (Directory.Exists(config.SaveRoot))
        {
            dialog.SelectedPath = config.SaveRoot;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        config.SaveRoot = dialog.SelectedPath;
        config.Save();
        saveRootBox.Text = config.SaveRoot;
        UpdatePreview();
    }

    private void OnStartupChanged(object? sender, EventArgs e)
    {
        if (loading)
        {
            return;
        }

        if (!StartupRegistry.TrySet(startupCheck.Checked))
        {
            loading = true;
            startupCheck.Checked = StartupRegistry.IsEnabled();
            loading = false;
        }
    }

    /// <summary>Documents the naming scheme in place of any docs the user would not read.</summary>
    private void UpdatePreview()
    {
        var now = DateTime.Now;

        // Built through the same helper the capture path uses, so the preview cannot
        // drift away from what actually gets written.
        previewLabel.Text = CaptureNaming.BuildPath(
            CaptureNaming.FolderFor(config.SaveRoot, now), "chrome", now, _ => false);
    }

    /// <summary>Re-reads live state each time the window appears rather than trusting the config.</summary>
    public void ShowSettings()
    {
        loading = true;
        regionBox.Binding = config.RegionHotkey;
        fullBox.Binding = config.FullDisplayHotkey;
        saveRootBox.Text = config.SaveRoot;
        startupCheck.Checked = StartupRegistry.IsEnabled();
        regionWarning.Visible = false;
        fullWarning.Visible = false;
        loading = false;

        UpdatePreview();

        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    /// <summary>Closing hides to tray; it does not end the process.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No browser or no shell association — nothing useful to do about it.
        }
    }
}
