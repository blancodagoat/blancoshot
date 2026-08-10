using Microsoft.Win32;

namespace SnapKit;

/// <summary>
/// If Windows' own "Use the Print screen key to open screen capture" is on, RegisterHotKey
/// on VK_SNAPSHOT still succeeds but Snipping Tool gets the key first — capture just
/// silently does nothing. The setting is never changed programmatically; the user is shown
/// the conflict once and pointed at the right Settings page.
/// </summary>
internal static class PrintScreenNotice
{
    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";
    private const string SettingsUri = "ms-settings:easeofaccess-keyboard";

    private static string MarkerPath => Path.Combine(AppInfo.DataDirectory, "printscreen-notice.flag");

    public static bool SnippingOwnsPrintScreen()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyboardKey, writable: false);
            return key?.GetValue(ValueName) is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Shows the notice at most once, and only while Print Screen is actually bound.</summary>
    public static void ShowIfNeeded(AppConfig config)
    {
        if (config.RegionHotkey.Key != Keys.PrintScreen || !SnippingOwnsPrintScreen() || AlreadyShown())
        {
            return;
        }

        MarkShown();

        using var form = new NoticeForm();
        form.ShowDialog();
    }

    private static bool AlreadyShown()
    {
        try
        {
            return File.Exists(MarkerPath);
        }
        catch
        {
            return false;
        }
    }

    private static void MarkShown()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            // If the marker cannot be written the notice reappears next launch. Acceptable.
        }
    }

    private sealed class NoticeForm : Form
    {
        public NoticeForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = AppInfo.Name;
            ClientSize = new Size(420, 190);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            Icon = TrayIcons.CreateWindowIcon();

            var heading = new Label
            {
                Text = "Print Screen is taken",
                ForeColor = Theme.Accent,
                Font = Theme.Ui(11f, FontStyle.Bold),
                AutoSize = false,
            };
            heading.SetBounds(22, 20, 376, 24);
            Controls.Add(heading);

            var body = new Label
            {
                Text = "Windows currently opens Snipping Tool when you press Print Screen, " +
                       "so it never reaches SnapKit. Turn off \"Use the Print screen key to open " +
                       "screen capture\" in Windows Settings, or pick a different shortcut.",
                ForeColor = Theme.Dim,
                AutoSize = false,
            };
            body.SetBounds(22, 50, 376, 72);
            Controls.Add(body);

            var open = Button("Open Windows Settings", 152, 138, 156, 28);
            open.Click += (_, _) => SettingsForm.OpenUrl(SettingsUri);
            Controls.Add(open);

            var dismiss = Button("Dismiss", 316, 138, 82, 28);
            dismiss.Click += (_, _) => Close();
            Controls.Add(dismiss);

            CancelButton = dismiss;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.TryUseDarkTitleBar(Handle);
        }

        private static Button Button(string text, int x, int y, int width, int height)
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
            button.SetBounds(x, y, width, height);
            return button;
        }
    }
}
