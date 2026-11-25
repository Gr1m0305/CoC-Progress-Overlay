using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ClashOverlay.App.Overlay;
using ClashOverlay.App.Services;
using ClashOverlay.App.Tray;
using ClashOverlay.App.ViewModels;
using ClashOverlay.App.Windows;
using ClashOverlay.Core.Settings;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App;

public partial class App : Application
{
    public const string MutexName = @"Local\ClashOverlay.SingleInstance";
    private const string DemoMutexName = @"Local\ClashOverlay.Demo";
    private const string ActivateEventName = @"Local\ClashOverlay.Activate";

    /// <summary>The installer sets this to close every running copy (the app and a demo) before it replaces the exe.</summary>
    public const string ExitEventName = @"Local\ClashOverlay.Exit";

    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activateWait;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _exitWait;
    private AppHost? _host;
    private GameDetector? _detector;
    private HotkeyManager? _hotkeys;
    private OverlayCoordinator? _overlay;
    private OverlayViewModel? _vm;
    private TrayIcon? _tray;
    private KeyWatcher? _keys;
    private SetupWindow? _setup;
    private SettingsWindow? _settings;
    private ImportWindow? _import;

    /// <summary>Images and data are checked again in the background when the last check is older than this.</summary>
    private static readonly TimeSpan ContentCheckInterval = TimeSpan.FromDays(7);

    internal AppHost Host => _host!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = StartupOptions.Parse(e.Args);

        // Listen for the installer before taking the mutex it looks for, so there is no moment when the mutex says
        // "running" but the exit event does not exist yet.
        _exitSignal = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _host?.Log.LogInformation("Closing: asked to by the installer");
            Shutdown();
        }), null, Timeout.Infinite, true);

        // One copy at a time (the installer closes it by this name). A demo runs beside the real app.
        _mutex = new Mutex(true, options.Demo ? DemoMutexName : MutexName, out var first);
        if (!first)
        {
            if (!options.Demo && EventWaitHandle.TryOpenExisting(ActivateEventName, out var other))
            {
                other.Set();
                other.Dispose();
            }

            Shutdown();
            return;
        }

        _host = new AppHost(options);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _host.Log.LogCritical(args.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _host.Log.LogError(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        if (!_host.Data.TryLoad() && options.Demo)
        {
            _ = UpdateDataInBackground();
        }

        if (options.ScreenshotDir is { } dir)
        {
            _ = RunScreenshots(dir);
            return;
        }

        _vm = new OverlayViewModel(_host);
        _detector = new GameDetector(() => _host.Settings.GameRules, _host.Loggers.CreateLogger("GameDetector"));
        _hotkeys = new HotkeyManager(_host.Loggers.CreateLogger("Hotkeys"));
        _overlay = new OverlayCoordinator(_host, _detector, _hotkeys, _vm);
        _tray = new TrayIcon(TrayActions(), options.Demo ? "Clash Tracker (demo)" : "Clash Tracker");
        _overlay.GameDetected += (_, game) =>
        {
            if (_host.Settings.ShowDetectionToast)
            {
                _tray.Notify("Clash of Clans detected", $"Press {_host.Settings.Hotkeys.Toggle} to show Clash Tracker.");
            }
        };
        _overlay.Start();

        if (!options.Demo)
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _activateWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(OnSecondInstance), null, Timeout.Infinite, false);
        }

        if (options.Demo)
        {
            _overlay.Show();
            return;
        }

        _keys = new KeyWatcher(_host, (title, text, click, warning) => _tray.Notify(title, text, click, warning), () => OpenSetup(1));
        _host.Updates.AvailableChanged += (_, _) => ShowUpdateInTray();
        _host.Updates.NewVersionFound += (_, release) => _tray.Notify(
            $"Clash Tracker {release.VersionText} is out",
            $"You have {_host.Version}. Click here to open the download page.",
            () => Shell.Open(release.Url));
        ShowUpdateInTray();
        _host.Updates.Start();

        if (!_host.Settings.SetupCompleted || !_host.Keys.HasKey)
        {
            if (options.FromStartup)
            {
                _tray.Notify("Finish setting up Clash Tracker", "Click here to make your API key and add your accounts.", () => OpenSetup(1));
            }
            else
            {
                OpenSetup(1);
            }
        }
        else
        {
            _ = _keys.CheckAtStartAsync();
        }

        if (options.OpenSettings)
        {
            OpenSettings(null);
        }

        _ = UpdateContentAtStart();
    }

    private void ShowUpdateInTray()
    {
        var release = _host!.Updates.Available;
        _tray?.ShowUpdate(
            release is null ? null : $"Update available: {release.VersionText}",
            release is null ? null : () => Shell.Open(release.Url));
    }

    private TrayActions TrayActions() => new()
    {
        ToggleOverlay = () => _overlay!.Toggle(),
        ToggleHint = () => _host!.Settings.Hotkeys.Toggle,
        Accounts = () => _vm!.Accounts.Select(a => (a.Tag, string.IsNullOrEmpty(a.Hall) ? a.Name : $"{a.Name}  ({a.Hall})", a.IsSelected)).ToList(),
        SelectAccount = tag => _vm!.SelectAccount(_vm.Accounts.ToList().FindIndex(a => a.Tag == tag)),
        AccountHint = n => HotkeyPlan.AccountGesture(_host!.Settings.Hotkeys, n),
        ImportExport = _host!.IsDemo ? null : () => OpenImport(null),
        OpenSettings = () => OpenSettings(null),
        CheckForUpdates = _host.IsDemo ? null : () => _ = UpdateContentFromTray(),
        About = () => OpenSettings(SettingsViewModel.AboutPage),
        Preview = StartDemo,
        OpenLogs = () => OpenFolder(_host!.Paths.Logs),
        Exit = Shutdown,
    };

    /// <summary>Started again while running (Start menu, desktop shortcut): show a window rather than nothing.</summary>
    private void OnSecondInstance()
    {
        if (!_host!.Settings.SetupCompleted)
        {
            OpenSetup(1);
        }
        else
        {
            OpenSettings(null);
        }
    }

    // ----- windows (one of each) -----

    internal void OpenSetup(int step)
    {
        if (_setup is { } open)
        {
            open.ViewModel.Step = Math.Clamp(step, 1, 4);
            open.ShowOrActivate();
            return;
        }

        var vm = new SetupViewModel(_host!, _keys, step);
        var window = new SetupWindow(vm);
        var finished = false;
        vm.Finished += (_, _) => finished = true;
        window.Closed += (_, _) =>
        {
            _setup = null;
            var s = _host!.Settings;
            if (!s.SetupCompleted && _host.Keys.HasKey && s.Accounts.Count > 0)
            {
                // Closed after the parts that matter; the import can come later.
                s.SetupCompleted = true;
                _host.SaveSettings();
                finished = true;
            }

            if (finished)
            {
                _tray?.Notify("Clash Tracker is ready", $"Press {s.Hotkeys.Toggle} while the game is in front to show it. Clash Tracker stays here in the notification area.");
                _ = _vm?.LoadAsync(force: true);
            }
            else if (!s.SetupCompleted)
            {
                _tray?.Notify("Setup is not finished", "Click here to carry on. Clash Tracker needs an API key and at least one account.", () => OpenSetup(1));
            }
        };
        _setup = window;
        window.ShowOrActivate();
    }

    internal void OpenSettings(string? page)
    {
        if (_settings is { } open)
        {
            open.ViewModel.ShowPage(page);
            open.ShowOrActivate();
            return;
        }

        var actions = new SettingsActions
        {
            Preview = StartDemo,
            OpenSetup = OpenSetup,
            OpenImport = OpenImport,
            Confirm = Confirm,
            OpenFolder = OpenFolder,
        };
        var window = new SettingsWindow(new SettingsViewModel(_host!, _host!.IsDemo ? null : _detector, actions, page));
        window.Closed += (_, _) => _settings = null;
        _settings = window;
        window.ShowOrActivate();
    }

    internal void OpenImport(string? tag)
    {
        if (_host!.Settings.Accounts.Count == 0)
        {
            OpenSettings(SettingsViewModel.AccountsPage);
            return;
        }

        if (_import is { } open)
        {
            open.ViewModel.Select(tag);
            open.ShowOrActivate();
            return;
        }

        var window = new ImportWindow(new ImportViewModel(_host, tag));
        window.Closed += (_, _) => _import = null;
        _import = window;
        window.ShowOrActivate();
    }

    private bool Confirm(string title, string question)
    {
        var owner = Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var answer = owner is null
            ? MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, question, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }

    // ----- game data and images -----

    /// <summary>First run: download data and images with notifications. Later: a quiet check once a week.</summary>
    private async Task UpdateContentAtStart()
    {
        var host = _host!;
        var missing = host.Data.Data is null || host.Content.ImagesMissing;
        if (!missing && host.Content.ImagesUpdatedAt is { } last && host.Time.GetUtcNow() - last < ContentCheckInterval)
        {
            return;
        }

        if (missing)
        {
            _tray?.Notify("Downloading game data", "Clash Tracker is downloading upgrade data and images. The first time takes a minute or two.");
        }

        var result = await host.Content.RunAsync(force: false);
        host.Log.LogInformation("Start-up content check: {Title}. {Detail}", result.Title, result.Detail);
        if (missing || !result.Ok)
        {
            _tray?.Notify(result.Ok ? "Game data is ready" : result.Title, result.Detail, null, !result.Ok);
        }
    }

    private async Task UpdateContentFromTray()
    {
        var host = _host!;
        if (host.Content.IsRunning)
        {
            _tray?.Notify("Already updating", host.Content.Status ?? "An update is running.");
            return;
        }

        _tray?.Notify("Checking for updates", "Checking game data and images…");
        var result = await host.Content.RunAsync(force: false);
        _tray?.Notify(result.Title, result.Detail, null, !result.Ok);
    }

    /// <summary>A second copy of the app in demo mode (no game, key or internet needed).</summary>
    private void StartDemo()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--demo") { UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _host!.Log.LogWarning(e, "Could not start the demo");
        }
    }

    internal static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private async Task UpdateDataInBackground()
    {
        try
        {
            var result = await _host!.Data.UpdateAsync(force: false);
            _host.Log.LogInformation("Game data: {Outcome} {Message}", result.Outcome, result.Message);
        }
        catch (Exception ex)
        {
            _host!.Log.LogWarning(ex, "Game data download failed");
        }
    }

    private async Task RunScreenshots(string dir)
    {
        try
        {
            await ScreenshotRunner.RunAsync(_host!, dir);
        }
        catch (Exception ex)
        {
            _host!.Log.LogError(ex, "Screenshots failed");
            Console.Error.WriteLine(ex);
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Log.LogError(e.Exception, "Unhandled UI exception");
        e.Handled = true; // keep the tray app alive; the log has the details
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activate?.Dispose();
        _exitWait?.Unregister(null);
        _exitSignal?.Dispose();
        _overlay?.Dispose();
        _hotkeys?.Dispose();
        _detector?.Dispose();
        _tray?.Dispose();
        _host?.Log.LogInformation("Clash Tracker exiting");
        _host?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _mutex.Dispose();
        }

        base.OnExit(e);
    }
}
