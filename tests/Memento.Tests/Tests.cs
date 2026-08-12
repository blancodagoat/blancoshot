// Assertions over the parts of Memento that do not need a live desktop: hotkey
// round-tripping, source-app name sanitising and output path construction.
// The real source files are compiled into this project, not copied.
//
//   dotnet run --project tests/Memento.Tests
//   dotnet run --project tests/Memento.Tests -- smoke   # + live capture geometry, see SmokeTest.cs
//
// Exit code is 0 when everything passes.


using Memento;
using Memento.Tests;

bool smoke = args.Contains("smoke");
if (smoke)
{
    // Before any handle exists, so coordinates are physical pixels like the app's manifest makes them.
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
}

int failed = 0, passed = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; return; }
    failed++;
    Console.WriteLine($"FAIL  {name}{(detail is null ? "" : "  -> " + detail)}");
}

void Eq(string name, object? actual, object? expected) =>
    Check(name, Equals(actual, expected), $"expected <{expected}>, got <{actual}>");

// HotkeyBinding: display names
Eq("default region renders", HotkeyBinding.DefaultRegion.ToString(), "PrintScreen");
Eq("default full renders", HotkeyBinding.DefaultFullDisplay.ToString(), "F8");
Eq("modifier order", new HotkeyBinding(Keys.S, true, true, true, true).ToString(), "Ctrl+Alt+Shift+Win+S");
Eq("digit key name", new HotkeyBinding(Keys.D4, true, false, false, false).ToString(), "Ctrl+4");
Eq("oem key name", new HotkeyBinding(Keys.OemPeriod, false, true, false, false).ToString(), "Alt+.");
Eq("enter alias", new HotkeyBinding(Keys.Return, false, false, false, false).ToString(), "Enter");

// HotkeyBinding: round trip
// A parse failure here would silently reset the user's hotkeys to defaults on every launch.
var samples = new[]
{
    HotkeyBinding.DefaultRegion,
    HotkeyBinding.DefaultFullDisplay,
    new HotkeyBinding(Keys.S, true, false, true, false),
    new HotkeyBinding(Keys.F12, false, true, false, false),
    new HotkeyBinding(Keys.D0, true, true, false, false),
    new HotkeyBinding(Keys.OemQuestion, false, false, true, false),
    new HotkeyBinding(Keys.OemPipe, true, false, false, false),
    new HotkeyBinding(Keys.Oemtilde, false, false, false, true),
    new HotkeyBinding(Keys.Escape, true, false, false, false),
    new HotkeyBinding(Keys.Next, false, false, false, false),
};

foreach (var sample in samples)
{
    var text = sample.ToString();
    Check($"round trip {text}", HotkeyBinding.TryParse(text, out var back) && back == sample,
        $"reparsed as {(HotkeyBinding.TryParse(text, out var b) ? b.ToString() : "<parse failed>")}");
}

// HotkeyBinding: tolerant and rejecting parses
Check("parses lowercase", HotkeyBinding.TryParse("ctrl+shift+s", out var lower) && lower == new HotkeyBinding(Keys.S, true, false, true, false));
Check("parses control alias", HotkeyBinding.TryParse("Control+A", out _));
Check("parses prtsc alias", HotkeyBinding.TryParse("prtsc", out var prt) && prt.Key == Keys.PrintScreen);
Check("rejects empty", !HotkeyBinding.TryParse("", out _));
Check("rejects null", !HotkeyBinding.TryParse(null, out _));
Check("rejects modifier only", !HotkeyBinding.TryParse("Ctrl", out _));
Check("rejects nonsense", !HotkeyBinding.TryParse("Ctrl+Nope", out _));

// SourceApp.Sanitise
Eq("plain name", SourceApp.Sanitise("chrome"), "chrome");
Eq("strips exe", SourceApp.Sanitise("Chrome.exe"), "chrome");
Eq("strips path", SourceApp.Sanitise(@"C:\Program Files\Google\Chrome.exe"), "chrome");
Eq("strips forward path", SourceApp.Sanitise("/usr/bin/Code.exe"), "code");
Eq("spaces become dashes", SourceApp.Sanitise("Visual Studio Code"), "visual-studio-code");
Eq("invalid chars become dashes", SourceApp.Sanitise("we:ird*name?"), "we-ird-name");
Eq("collapses runs", SourceApp.Sanitise("a   b"), "a-b");
Eq("trims edge dashes", SourceApp.Sanitise("  spaced  "), "spaced");
Eq("null is unknown", SourceApp.Sanitise(null), "unknown");
Eq("blank is unknown", SourceApp.Sanitise("   "), "unknown");
Eq("all-invalid is unknown", SourceApp.Sanitise("???"), "unknown");
Eq("dot-only is unknown", SourceApp.Sanitise("..."), "unknown");
Check("never yields a path separator", !SourceApp.Sanitise(@"a\b/c").Contains('\\'));

// CaptureNaming
var when = new DateTime(2026, 8, 10, 14, 30, 52);
var folder = Path.Combine("root", "2026", "08 Aug");

Eq("folder nesting", CaptureNaming.FolderFor("root", when), folder);
Eq("filename shape",
    Path.GetFileName(CaptureNaming.BuildPath(folder, "chrome", when, _ => false)),
    "chrome_2026-08-10_143052.png");

// Same-second collisions must walk to _2, _3, ... rather than overwrite.
var taken = new HashSet<string>
{
    Path.Combine(folder, "chrome_2026-08-10_143052.png"),
    Path.Combine(folder, "chrome_2026-08-10_143052_2.png"),
};
Eq("collision suffix",
    Path.GetFileName(CaptureNaming.BuildPath(folder, "chrome", when, taken.Contains)),
    "chrome_2026-08-10_143052_3.png");

// Filename dates must stay zero-padded or the sort order breaks; month folders carry
// the number for the same reason ("01 Jan" sorts chronologically, "Jan" does not).
var early = new DateTime(2026, 1, 2, 3, 4, 5);
Eq("month folder shape", CaptureNaming.FolderFor("root", early), Path.Combine("root", "2026", "01 Jan"));
Eq("zero padded name",
    Path.GetFileName(CaptureNaming.BuildPath("f", "app", early, _ => false)),
    "app_2026-01-02_030405.png");

// Native.HitTest: topmost window wins; within it, the smallest child under the point.
var topWindow = new Native.WindowRects(new Rectangle(0, 0, 100, 100), new List<Rectangle>
{
    new(10, 10, 80, 80),   // client pane
    new(20, 20, 30, 30),   // nested control inside the pane
});
var behindWindow = new Native.WindowRects(new Rectangle(50, 50, 200, 200), new List<Rectangle>());
var desktopWindows = new List<Native.WindowRects> { topWindow, behindWindow };

Eq("hit nested control", Native.HitTest(desktopWindows, new Point(25, 25)), new Rectangle(20, 20, 30, 30));
Eq("hit pane outside control", Native.HitTest(desktopWindows, new Point(15, 60)), new Rectangle(10, 10, 80, 80));
Eq("hit title bar gives window", Native.HitTest(desktopWindows, new Point(5, 5)), new Rectangle(0, 0, 100, 100));
Eq("topmost occludes window behind", Native.HitTest(desktopWindows, new Point(60, 60)), new Rectangle(10, 10, 80, 80));
Eq("uncovered part of window behind", Native.HitTest(desktopWindows, new Point(150, 150)), new Rectangle(50, 50, 200, 200));
Eq("bare desktop is empty", Native.HitTest(desktopWindows, new Point(300, 300)), Rectangle.Empty);

// NormaliseMonthFolders
// Legacy "08" and "Aug" folders from earlier naming schemes fold into "08 Aug",
// merging when both exist; current-shape and unrelated folders are untouched.
var tempRoot = Path.Combine(Path.GetTempPath(), "snapkit-test-" + Guid.NewGuid().ToString("N"));
try
{
    var year = Path.Combine(tempRoot, "2026");
    Directory.CreateDirectory(Path.Combine(year, "08"));
    Directory.CreateDirectory(Path.Combine(year, "Aug"));
    Directory.CreateDirectory(Path.Combine(year, "01 Jan"));
    Directory.CreateDirectory(Path.Combine(year, "notes"));
    File.WriteAllText(Path.Combine(year, "08", "a.png"), "");
    File.WriteAllText(Path.Combine(year, "Aug", "b.png"), "");

    CaptureNaming.NormaliseMonthFolders(tempRoot);

    Check("numeric folder folded", !Directory.Exists(Path.Combine(year, "08")));
    Check("named folder folded", !Directory.Exists(Path.Combine(year, "Aug")));
    Check("merged file a", File.Exists(Path.Combine(year, "08 Aug", "a.png")));
    Check("merged file b", File.Exists(Path.Combine(year, "08 Aug", "b.png")));
    Check("canonical untouched", Directory.Exists(Path.Combine(year, "01 Jan")));
    Check("unrelated untouched", Directory.Exists(Path.Combine(year, "notes")));
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

// Issue reporting: the prefilled new-issue URL must scrub identity from the message.
{
    var url = IssueReport.BuildUrl(
        @"Could not save to C:\Users\casey\Pictures. Access denied.",
        "v1.0 · Windows 10.0.26200", @"C:\Users\casey", "casey");
    Check("issue url targets the repo", url.StartsWith(AppInfo.GitHubUrl + "/issues/new?title="));
    Check("issue url carries the error", url.Contains(Uri.EscapeDataString("Access denied")));
    Check("issue url leaks no username", !url.Contains("casey", StringComparison.OrdinalIgnoreCase));
}

if (smoke)
{
    Console.WriteLine("\nsmoke: live-desktop capture geometry");
    SmokeTest.Run(Check);
}

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
