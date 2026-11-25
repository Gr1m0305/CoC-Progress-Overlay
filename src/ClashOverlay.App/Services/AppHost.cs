using System.Reflection;
using ClashOverlay.Api;
using ClashOverlay.Core;
using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Settings;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>Command line: <c>--demo</c>, <c>--screenshots DIR</c> (with --demo), <c>--settings</c>, <c>--startup</c>, <c>--verbose</c>.</summary>
internal sealed record StartupOptions(bool Demo, string? ScreenshotDir, bool OpenSettings, bool FromStartup, bool Verbose)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? shots = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals("--screenshots", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                shots = args[i + 1];
            }
        }

        bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return new StartupOptions(Has("--demo") || shots is not null, shots, Has("--settings"), Has("--startup"), Has("--verbose"));
    }
}

/// <summary>The app's services, built once at start-up.</summary>
internal sealed class AppHost : IDisposable
{
    private readonly HttpClient _apiHttp;
    private readonly HttpClient _dataHttp;

    public AppHost(StartupOptions options)
    {
        Options = options;
        Paths = AppPaths.Default;
        Version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev";
        GitHubRepository = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GitHubRepository")?.Value;
        Loggers = AppLogging.Create(Paths.Logs, options.Verbose, options.Demo);
        Log = Loggers.CreateLogger("App");
        Log.LogInformation("Clash Tracker {Version} starting{Demo}", Version, options.Demo ? " in demo mode" : "");

        Time = options.Demo ? new DemoClock() : TimeProvider.System;
        Settings = options.Demo ? DemoSettings() : AppSettings.Load(Paths.SettingsFile);

        _dataHttp = StaticDataUpdater.CreateHttpClient(Version);
        Data = new GameDataService(Paths.Data, _dataHttp, Loggers);
        Images = new ImageCache(new AssetResolver(Paths.Assets), Loggers.CreateLogger("Images"));
        Keys = new ApiKeyStore(Paths.ApiKeyFile);
        Logins = new DeveloperLoginStore(Paths.DeveloperLoginFile);
        PublicIp = new PublicIpService(_dataHttp);

        _apiHttp = ClashApiClient.CreateHttpClient(Version);
        Api = options.Demo
            ? new DemoApiClient(Time)
            : new ClashApiClient(_apiHttp, () => Keys.Load(), new ResponseCache(Paths.Cache), Time, Loggers.CreateLogger<ClashApiClient>());
        Store = new AccountStore(Paths, Loggers.CreateLogger("Accounts"), options.Demo ? DemoData.Exports() : null);
        Loader = new AccountLoader(Data, Api, Store, () => Settings, Time, Loggers.CreateLogger("AccountLoader"));
        Content = new ContentUpdater(this);
        Updates = new UpdateWatcher(this);
    }

    public StartupOptions Options { get; }

    public bool IsDemo => Options.Demo;

    public AppPaths Paths { get; }

    public string Version { get; }

    public string? GitHubRepository { get; }

    public ILoggerFactory Loggers { get; }

    public ILogger Log { get; }

    public TimeProvider Time { get; }

    public AppSettings Settings { get; private set; }

    public GameDataService Data { get; }

    public ImageCache Images { get; }

    public ApiKeyStore Keys { get; }

    /// <summary>The optional developer-site login for automatic keys (DPAPI, off unless the user turns it on).</summary>
    public DeveloperLoginStore Logins { get; }

    public ContentUpdater Content { get; }

    /// <summary>The "is a newer Clash Tracker out" check at start-up, and installing it.</summary>
    public UpdateWatcher Updates { get; }

    public PublicIpService PublicIp { get; }

    public IClashApiClient Api { get; }

    public AccountStore Store { get; }

    public AccountLoader Loader { get; }

    public HttpClient DataHttp => _dataHttp;

    public event EventHandler? SettingsChanged;

    /// <summary>Saves (not in demo mode) and tells everyone.</summary>
    public void SaveSettings()
    {
        Settings.Normalize();
        if (!IsDemo)
        {
            try
            {
                Settings.Save(Paths.SettingsFile);
            }
            catch (IOException e)
            {
                Log.LogError(e, "Settings could not be saved");
            }
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceSettings(AppSettings settings)
    {
        Settings = settings.Normalize();
        SaveSettings();
    }

    private static AppSettings DemoSettings() => new AppSettings
    {
        Accounts =
        [
            new AccountSettings { Tag = DemoData.MainTag, Name = "Demo Chief", TownHall = 19, TownHallChangedAt = DemoData.Now.AddDays(-9) },
            new AccountSettings { Tag = DemoData.AltTag, Name = "Demo Alt", Nickname = "Alt", TownHall = 13 },
        ],
        SelectedAccount = DemoData.MainTag,
        SetupCompleted = true,
        InstallUpdatesAutomatically = false,
    }.Normalize();

    public void Dispose()
    {
        Updates.Dispose();
        _apiHttp.Dispose();
        _dataHttp.Dispose();
        Loggers.Dispose();
    }
}
