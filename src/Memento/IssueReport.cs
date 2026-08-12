using System.Diagnostics;

namespace Memento;

/// <summary>
/// Failure reporting that keeps "no phone-home" literally true: the app only builds a
/// prefilled new-issue URL and hands it to the browser, where the user reads exactly
/// what would be sent and submits it themselves — or closes the tab. Offered only after
/// a real failure, never on a timer and never on first run.
/// </summary>
internal static class IssueReport
{
    public static void Open(string context)
    {
        try
        {
            var environment = $"v{UpdateCheck.Current} · Windows {Environment.OSVersion.Version}";
            var url = BuildUrl(context, environment,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Reporting must never hurt the app.
        }
    }

    /// <summary>Pure so the tests can assert on it; Open supplies the live values.</summary>
    public static string BuildUrl(string context, string environment, string userProfile, string userName)
    {
        var body =
            $"**What happened:** {Scrub(context, userProfile, userName)}\n\n" +
            $"**Environment:** {environment}\n";

        return $"{AppInfo.GitHubUrl}/issues/new" +
            $"?title={Uri.EscapeDataString("Error report (from the app)")}" +
            $"&body={Uri.EscapeDataString(body)}";
    }

    /// <summary>Error messages carry paths; paths carry the username. Neither belongs in a public issue.</summary>
    public static string Scrub(string text, string userProfile, string userName)
    {
        if (userProfile.Length > 0)
        {
            text = text.Replace(userProfile, "~", StringComparison.OrdinalIgnoreCase);
        }

        if (userName.Length > 0)
        {
            text = text.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }
}
