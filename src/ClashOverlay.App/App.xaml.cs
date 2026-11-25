using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ClashOverlay.App.Overlay;
using ClashOverlay.App.Services;
using ClashOverlay.App.Tray;
using ClashOverlay.App.ViewModels;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App;

public partial class App : Application
{
    public const string MutexName = @"Local\ClashOverlay.SingleInstance";
    private const string DemoMutexName = @"Local\ClashOverlay.Demo";
    private const string ActivateEventName = @"Local\ClashOverlay.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activateWait;
    private AppHost? _host;
    private GameDetector? _detector;
    private HotkeyManager? _hotkeys;
    private OverlayCoordinator? _overlay;
    private OverlayViewModel? _vm;
    private TrayIcon? _tray;

    internal AppHost Host => _host!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = StartupOptions.Parse(e.Args);

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

        if (!_host.Data.TryLoad())
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
        }
    }

    private TrayActions TrayActions() => new()
    {
        ToggleOverlay = () => _overlay!.Toggle(),
        ToggleHint = () => _host!.Settings.Hotkeys.Toggle,
        Accounts = () => _vm!.Accounts.Select(a => (a.Tag, string.IsNullOrEmpty(a.Hall) ? a.Name : $"{a.Name}  ({a.Hall})", a.IsSelected)).ToList(),
        SelectAccount = tag => _vm!.SelectAccount(_vm.Accounts.ToList().FindIndex(a => a.Tag == tag)),
        Preview = StartDemo,
        OpenLogs = () => OpenFolder(_host!.Paths.Logs),
        Exit = Shutdown,
    };

    private void OnSecondInstance() =>
        _tray?.Notify("Clash Tracker is already running", $"It lives in the notification area. Press {_host!.Settings.Hotkeys.Toggle} over the game.");

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
