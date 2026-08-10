namespace SnapKit;

/// <summary>Output path construction, kept free of any UI dependency.</summary>
internal static class CaptureNaming
{
    /// <summary>The nested year/month folder the capture belongs in.</summary>
    public static string FolderFor(string root, DateTime now) =>
        Path.Combine(root, now.ToString("yyyy"), now.ToString("MM"));

    /// <summary>
    /// chrome_2026-08-10_143052.png, with _2 / _3 suffixes when two captures land in the
    /// same second.
    /// </summary>
    public static string BuildPath(string folder, string app, DateTime now, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;

        var stem = $"{app}_{now:yyyy-MM-dd}_{now:HHmmss}";
        var path = Path.Combine(folder, stem + ".png");
        for (int n = 2; exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem}_{n}.png");
        }

        return path;
    }
}
