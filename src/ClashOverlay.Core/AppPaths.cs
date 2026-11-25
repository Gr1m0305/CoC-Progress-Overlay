namespace ClashOverlay.Core;

/// <summary>
/// Everything the app writes lives under <c>%LOCALAPPDATA%\ClashOverlay</c>. <c>CLASHOVERLAY_HOME</c> moves it (tests,
/// portable use).
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
    }

    public static AppPaths Default { get; } = new(DefaultRoot());

    public string Root { get; }

    public string Data => Path.Combine(Root, "data");

    public string Assets => Path.Combine(Root, "assets");

    public string Cache => Path.Combine(Root, "cache");

    public string IconCache => Path.Combine(Root, "cache", "icons");

    public string Accounts => Path.Combine(Root, "accounts");

    public string Logs => Path.Combine(Root, "logs");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string ApiKeyFile => Path.Combine(Root, "apikey.bin");

    public string DeveloperLoginFile => Path.Combine(Root, "devsite.bin");

    public string AccountDir(string tag) => Path.Combine(Accounts, PlayerTag.ForPath(tag));

    public string AccountCacheDir(string tag) => Path.Combine(Cache, PlayerTag.ForPath(tag));

    public string ExportFile(string tag) => Path.Combine(AccountDir(tag), "export.json");

    private static string DefaultRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("CLASHOVERLAY_HOME");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return overrideRoot;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClashOverlay");
    }
}
