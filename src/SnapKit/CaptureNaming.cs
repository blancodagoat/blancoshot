using System.Globalization;

namespace SnapKit;

/// <summary>Output path construction, kept free of any UI dependency.</summary>
internal static class CaptureNaming
{
    /// <summary>
    /// The nested year/month folder the capture belongs in. Months are "08 Aug" — the
    /// number keeps alphabetical listings chronological, the name keeps them readable.
    /// Every format is pinned to invariant culture so the tree (and the Gregorian year)
    /// never depends on the user's locale or calendar.
    /// </summary>
    public static string FolderFor(string root, DateTime now) =>
        Path.Combine(
            root,
            now.ToString("yyyy", CultureInfo.InvariantCulture),
            now.ToString("MM MMM", CultureInfo.InvariantCulture));

    /// <summary>
    /// chrome_2026-08-10_143052.png, with _2 / _3 suffixes when two captures land in the
    /// same second.
    /// </summary>
    public static string BuildPath(string folder, string app, DateTime now, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;

        var stem = string.Create(
            CultureInfo.InvariantCulture, $"{app}_{now:yyyy-MM-dd}_{now:HHmmss}");
        var path = Path.Combine(folder, stem + ".png");
        for (int n = 2; exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem}_{n}.png");
        }

        return path;
    }

    /// <summary>
    /// One-shot tidy for trees written by earlier folder schemes: month folders named
    /// "08" or "Aug" are folded into "08 Aug". Best-effort — capture never depends on it.
    /// </summary>
    public static void NormaliseMonthFolders(string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var yearDir in Directory.EnumerateDirectories(root))
            {
                if (Path.GetFileName(yearDir).Length != 4 || !int.TryParse(Path.GetFileName(yearDir), out _))
                {
                    continue;
                }

                foreach (var monthDir in Directory.EnumerateDirectories(yearDir).ToList())
                {
                    int month = ParseLegacyMonth(Path.GetFileName(monthDir));
                    if (month == 0)
                    {
                        continue;
                    }

                    var canonical = Path.Combine(
                        yearDir,
                        new DateTime(2000, month, 1).ToString("MM MMM", CultureInfo.InvariantCulture));

                    if (!Directory.Exists(canonical))
                    {
                        Directory.Move(monthDir, canonical);
                    }
                    else
                    {
                        foreach (var file in Directory.EnumerateFiles(monthDir).ToList())
                        {
                            File.Move(file, Path.Combine(canonical, Path.GetFileName(file)), overwrite: false);
                        }

                        if (!Directory.EnumerateFileSystemEntries(monthDir).Any())
                        {
                            Directory.Delete(monthDir);
                        }
                    }
                }
            }
        }
        catch
        {
            // A folder that cannot be tidied is left as it was found.
        }
    }

    /// <summary>"08" or "Aug" → 8; anything else (including "08 Aug") → 0.</summary>
    private static int ParseLegacyMonth(string name)
    {
        if (name.Length == 2 && int.TryParse(name, out int numeric) && numeric is >= 1 and <= 12)
        {
            return numeric;
        }

        if (name.Length == 3 && DateTime.TryParseExact(
                name, "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var named))
        {
            return named.Month;
        }

        return 0;
    }
}
