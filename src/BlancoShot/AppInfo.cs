namespace BlancoShot;

internal static class AppInfo
{
    public const string Name = "BlancoShot";
    public const string GitHubUrl = "https://github.com/blancodagoat/blancoshot";
    public const string PortfolioUrl = "https://blancodagoat.dev/";

    /// <summary>
    /// Full path of the running executable. ProcessPath is the only reliable source under
    /// single-file publish, where Assembly.Location is empty.
    /// </summary>
    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Name + ".exe");

    /// <summary>%APPDATA%\BlancoShot — config plus the first-run notice marker.</summary>
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");

    /// <summary>
    /// A folder of our own. Pictures\Screenshots is where Win+PrintScreen dumps its output,
    /// so defaulting there buried captures among Windows' — see AppConfig's migration.
    /// </summary>
    public static string DefaultSaveRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BlancoShot");
}
