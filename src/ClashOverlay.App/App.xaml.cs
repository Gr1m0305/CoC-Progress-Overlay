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
    private string? _contentProblem;

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
        // Off-screen screenshots never show anything, so they run even beside an open demo.
        _mutex = new Mutex(true, options.Demo ? DemoMutexName : MutexName, out var first);
        if (!first && options.ScreenshotDir is not null)
        {
            _mutex.Dispose();
            _mutex = null;
        }
        else if (!first)
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
        // A demo registers its keys normally instead of hooking the keyboard, so it never holds keys from other apps.
        _hotkeys = new HotkeyManager(_host.Loggers.CreateLogger("Hotkeys"), useHook: !options.Demo);
        _overlay = new OverlayCoordinator(_host, _detector, _hotkeys, _vm);
        _tray = new TrayIcon(TrayActions(), options.Demo ? "Clash Tracker (demo)" : "Clash Tracker");
        _overlay.Arranged += (_, _) => _settings?.ViewModel.OverlayMoved();
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

        _keys = new KeyWatcher(_host, () => OpenSetup(1));
        _keys.AttentionChanged += (_, _) => ShowStatusInTray();
        _host.Keys.Changed += (_, _) => Dispatcher.BeginInvoke(ShowStatusInTray);
        _host.SettingsChanged += (_, _) => ShowStatusInTray();
        _host.Updates.Changed += (_, _) => ShowUpdateInTray();
        _host.Updates.IsBusy = () => _overlay?.IsVisible == true;
        ShowStatusInTray();
        ShowUpdateInTray();

        // Checked once at start (at sign-in when Clash Tracker starts with Windows) and from Settings.
        _host.Updates.Start();

        if (!_host.Settings.SetupCompleted || !_host.Keys.HasKey)
        {
            // Started with Windows: stay quiet in the tray, where the menu's first line opens setup.
            if (!options.FromStartup)
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

    /// <summary>The tray menu's first line: what needs the user now, if anything.</summary>
    private void ShowStatusInTray()
    {
        var host = _host!;
        if (_tray is null || host.IsDemo)
        {
            return;
        }

        if (!host.Settings.SetupCompleted || !host.Keys.HasKey)
        {
            _tray.ShowStatus("Finish setting up Clash Tracker…", () => OpenSetup(1));
        }
        else if (_keys?.Attention is { } attention)
        {
            _tray.ShowStatus(attention, _keys.OpenKeySteps);
        }
        else if (host.Content.IsRunning)
        {
            _tray.ShowStatus("Downloading game data and images…", null);
        }
        else if (_contentProblem is { } problem)
        {
            _tray.ShowStatus($"{problem}: try again…", () => _ = UpdateContentFromTray());
        }
        else
        {
            _tray.ShowStatus(null, null);
        }
    }

    /// <summary>The tray menu's update line.</summary>
    private void ShowUpdateInTray()
    {
        var updates = _host!.Updates;
        if (_tray is null)
        {
            return;
        }

        if (updates.Available is not { } release)
        {
            _tray.ShowUpdate(null, null);
            return;
        }

        switch (updates.State)
        {
            case UpdateState.Downloading:
                _tray.ShowUpdate($"Downloading Clash Tracker {release.VersionText}…", null);
                break;
            case UpdateState.Installing:
                _tray.ShowUpdate($"Installing Clash Tracker {release.VersionText}…", null);
                break;
            default:
                _tray.ShowUpdate(
                    updates.CanInstall ? $"Install Clash Tracker {release.VersionText}" : $"Download Clash Tracker {release.VersionText}…",
                    updates.CanInstall ? () => _ = updates.InstallAsync() : () => Shell.Open(release.Url));
                break;
        }
    }

    private TrayActions TrayActions() => new()
    {
        ToggleOverlay = () => _overlay!.Toggle(),
        Accounts = () => _vm!.Accounts.Select(a => (a.Tag, string.IsNullOrEmpty(a.Hall) ? a.Name : $"{a.Name}  ({a.Hall})", a.IsSelected)).ToList(),
        SelectAccount = tag => _vm!.SelectAccount(_vm.Accounts.ToList().FindIndex(a => a.Tag == tag)),
        AccountHint = n => HotkeyPlan.AccountGesture(_host!.Settings.Hotkeys, n),
        ImportExport = _host!.IsDemo ? null : () => OpenImport(null),
        OpenSettings = () => OpenSettings(null),
        UpdateContent = _host.IsDemo ? null : () => _ = UpdateContentFromTray(),
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
                _ = _vm?.LoadAsync(force: true);
            }

            ShowStatusInTray();
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

    /// <summary>First run: download data and images. Later: a quiet check once a week. Progress shows in the tray menu.</summary>
    private async Task UpdateContentAtStart()
    {
        var host = _host!;
        var missing = host.Data.Data is null || host.Content.ImagesMissing;
        if (!missing && host.Content.ImagesUpdatedAt is { } last && host.Time.GetUtcNow() - last < ContentCheckInterval)
        {
            return;
        }

        await RunContentUpdate();
    }

    private async Task UpdateContentFromTray()
    {
        if (!_host!.Content.IsRunning)
        {
            await RunContentUpdate();
        }
    }

    private async Task RunContentUpdate()
    {
        var host = _host!;
        var run = host.Content.RunAsync(force: false);
        ShowStatusInTray();
        var result = await run;
        host.Log.LogInformation("Content check: {Title}. {Detail}", result.Title, result.Detail);
        _contentProblem = result.Ok ? null : result.Title;
        ShowStatusInTray();
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
