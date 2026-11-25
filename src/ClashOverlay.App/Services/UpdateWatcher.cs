using System.Diagnostics;
using System.Windows.Threading;
using ClashOverlay.Core.Updates;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

public enum UpdateState
{
    Idle,
    Checking,
    Downloading,
    Installing,
}

/// <summary>
/// Asks GitHub whether a newer Clash Tracker is out when the app starts (which is at sign-in when it starts with
/// Windows) and when "Check now" is pressed; there is no timer. With "Install updates automatically" on and this copy
/// installed by its setup, a new version is downloaded, checked against its SHA-256 (see <see cref="UpdateDownloader"/>)
/// and its setup run silently: setup closes this copy, replaces it and starts the new one in the tray. Otherwise the
/// tray menu and Settings offer the update. Nothing pops up.
/// </summary>
internal sealed class UpdateWatcher : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);

    /// <summary>At sign-in the network may not be up yet: a failed check at start is tried again after these waits.</summary>
    private static readonly TimeSpan[] Retries = [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

    private static readonly TimeSpan BusyRecheck = TimeSpan.FromSeconds(30);

    private readonly AppHost _host;
    private readonly ReleaseChecker? _checker;
    private readonly UpdateDownloader _downloader;
    private readonly Version _current;
    private readonly DispatcherTimer _timer = new();
    private int _retry;
    private bool _autoPending;

    public UpdateWatcher(AppHost host)
    {
        _host = host;
        _current = ReleaseChecker.ParseVersion(host.Version) ?? new Version(0, 0, 0);
        if (ReleaseChecker.IsValidRepository(host.GitHubRepository))
        {
            _checker = new ReleaseChecker(host.DataHttp, host.GitHubRepository!);
        }

        _downloader = new UpdateDownloader(host.DataHttp, host.Paths.Updates);

        // A release found before the last restart, unless this copy is already that version or newer.
        var s = host.Settings;
        if (ReleaseChecker.ParseVersion(s.UpdateTag) is { } saved && saved > _current && s.UpdateUrl is { } url)
        {
            Available = new ReleaseInfo(saved, s.UpdateTag!, url, null);
        }

        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _ = OnTimerAsync();
        };
    }

    /// <summary>A newer release than this copy, or null.</summary>
    public ReleaseInfo? Available { get; private set; }

    public UpdateState State { get; private set; }

    /// <summary>Why the last download or install did not happen, for the tray and Settings.</summary>
    public string? Problem { get; private set; }

    public bool CanCheck => _checker is not null && !_host.IsDemo;

    /// <summary>Builds from source are 0.0.0-dev; they never check by themselves.</summary>
    public bool IsDevBuild => _current == new Version(0, 0, 0);

    /// <summary>
    /// This copy was installed by Clash Tracker's setup (in its own folder, with the uninstaller beside it), so a new
    /// setup can replace it. A portable copy is never replaced; it is only told about new versions.
    /// </summary>
    public bool IsInstalledCopy { get; } = DetectInstalledCopy();

    public bool CanInstall => CanCheck && !IsDevBuild && IsInstalledCopy;

    public string? ReleasesPage => _checker?.ReleasesPage;

    /// <summary>True while the overlay is showing: an automatic install waits until it is closed.</summary>
    public Func<bool> IsBusy { get; set; } = () => false;

    public event EventHandler? Changed;

    public void Start()
    {
        if (!CanCheck || IsDevBuild)
        {
            return;
        }

        _timer.Interval = FirstCheckDelay;
        _timer.Start();
    }

    private async Task OnTimerAsync()
    {
        if (_autoPending)
        {
            await AutoInstallAsync();
            return;
        }

        var result = await CheckAsync();
        if (result?.Outcome == ReleaseCheckOutcome.Failed && _retry < Retries.Length)
        {
            _timer.Interval = Retries[_retry++];
            _timer.Start();
            return;
        }

        if (result?.Outcome == ReleaseCheckOutcome.UpdateAvailable && ShouldAutoInstall())
        {
            _autoPending = true;
            await AutoInstallAsync();
        }
    }

    /// <summary>"Check now": checks, and installs at once when automatic installs are on. Returns a sentence for Settings.</summary>
    public async Task<string> CheckNowAsync()
    {
        var result = await CheckAsync();
        switch (result)
        {
            case null:
                return "Checking…";
            case { Outcome: ReleaseCheckOutcome.UpdateAvailable, Latest: { } r }:
                if (CanInstall && _host.Settings.InstallUpdatesAutomatically)
                {
                    return await InstallAsync();
                }

                return CanInstall
                    ? $"Clash Tracker {r.VersionText} is out. You have {_host.Version}."
                    : $"Clash Tracker {r.VersionText} is out. You have {_host.Version}. Download it from the release page.";
            case { Outcome: ReleaseCheckOutcome.UpToDate }:
                return $"You have the newest version ({_host.Version}).";
            case { Outcome: ReleaseCheckOutcome.NoRelease }:
                return "No release has been published yet.";
            default:
                return result.Error ?? "The check did not work. Try again later.";
        }
    }

    private bool ShouldAutoInstall()
    {
        var s = _host.Settings;
        if (!CanInstall || !s.InstallUpdatesAutomatically || Available is not { } a)
        {
            return false;
        }

        // An install for this version already started once and this copy is still older: it did not finish.
        if (s.UpdateAttemptTag == a.Tag && s.UpdateAttemptAt is { } at && _host.Time.GetUtcNow() - at < TimeSpan.FromDays(1))
        {
            _host.Log.LogWarning("Not installing {Tag} again by itself: the install started {At} did not finish", a.Tag, at);
            Problem = $"Installing {a.VersionText} did not finish. Choose Install to try again.";
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        return true;
    }

    private async Task AutoInstallAsync()
    {
        if (IsBusy())
        {
            _timer.Interval = BusyRecheck;
            _timer.Start();
            return;
        }

        _autoPending = false;
        await InstallAsync();
    }

    private async Task<ReleaseCheckResult?> CheckAsync()
    {
        if (_checker is null || State != UpdateState.Idle || _host.IsDemo)
        {
            return null;
        }

        SetState(UpdateState.Checking);
        try
        {
            var result = await _checker.CheckAsync(_host.Version);
            _host.Log.LogInformation("Update check: {Outcome}, latest {Latest}", result.Outcome, result.Latest?.Tag ?? "(none)");
            if (result.Outcome == ReleaseCheckOutcome.Failed)
            {
                _host.Log.LogInformation("Update check failed: {Error}", result.Error);
                return result;
            }

            var s = _host.Settings;
            s.LastUpdateCheck = _host.Time.GetUtcNow();
            if (result is { Outcome: ReleaseCheckOutcome.UpdateAvailable, Latest: { } latest })
            {
                Available = latest;
                s.UpdateTag = latest.Tag;
                s.UpdateUrl = latest.Url;
            }
            else
            {
                Available = null;
                Problem = null;
                s.UpdateTag = null;
                s.UpdateUrl = null;
                _downloader.RemoveOthers(null);
            }

            _host.SaveSettings();
            return result;
        }
        finally
        {
            SetState(UpdateState.Idle);
        }
    }

    /// <summary>
    /// Downloads, checks and runs the new version's setup. On success setup closes this copy within seconds. Returns a
    /// sentence for Settings.
    /// </summary>
    public async Task<string> InstallAsync()
    {
        if (State != UpdateState.Idle)
        {
            return "An update is already being installed.";
        }

        if (!CanInstall)
        {
            return "This copy cannot update itself. Download the new version from the release page.";
        }

        // The release details (installer link and checksum) come from a fresh check.
        if (Available?.Setup is null)
        {
            var result = await CheckAsync();
            if (result?.Outcome != ReleaseCheckOutcome.UpdateAvailable)
            {
                return result?.Outcome == ReleaseCheckOutcome.UpToDate ? $"You have the newest version ({_host.Version})." : result?.Error ?? "The check did not work. Try again later.";
            }
        }

        var release = Available!;
        SetState(UpdateState.Downloading);
        try
        {
            _host.Log.LogInformation("Downloading {Tag}", release.Tag);
            var download = await _downloader.DownloadAsync(release);
            if (!download.Ok)
            {
                return Failed(release, download.Error!);
            }

            SetState(UpdateState.Installing);
            var s = _host.Settings;
            s.UpdateAttemptTag = release.Tag;
            s.UpdateAttemptAt = _host.Time.GetUtcNow();
            _host.SaveSettings();

            var log = Path.Combine(_host.Paths.Logs, "update-install.log");
            var start = new ProcessStartInfo(download.Path!)
            {
                UseShellExecute = false,
                WorkingDirectory = _downloader.Folder,
            };
            foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", $"/LOG={log}" })
            {
                start.ArgumentList.Add(arg);
            }

            _host.Log.LogInformation("Running the {Tag} setup; it closes this copy and starts the new one", release.Tag);
            using var setup = Process.Start(start);
            Problem = null;
            _ = WatchForStuckInstallAsync(release);
            return $"Installing Clash Tracker {release.VersionText}. It restarts in the tray in a few seconds.";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            SetState(UpdateState.Idle);
            return Failed(release, $"The setup could not be started: {e.Message}");
        }
        finally
        {
            if (State == UpdateState.Downloading)
            {
                SetState(UpdateState.Idle);
            }
        }
    }

    /// <summary>Setup normally closes this copy within seconds; if it is still running after two minutes, it gave up.</summary>
    private async Task WatchForStuckInstallAsync(ReleaseInfo release)
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        if (State == UpdateState.Installing)
        {
            SetState(UpdateState.Idle);
            Failed(release, $"Installing {release.VersionText} did not finish. Choose Install to try again.");
        }
    }

    private string Failed(ReleaseInfo release, string error)
    {
        _host.Log.LogWarning("Update to {Tag} not installed: {Error}", release.Tag, error);
        Problem = error;
        Changed?.Invoke(this, EventArgs.Empty);
        return error;
    }

    private void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool DetectInstalledCopy()
    {
        try
        {
            var exe = Environment.ProcessPath;
            var dir = exe is null ? null : Path.GetDirectoryName(exe);
            var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ClashOverlay");
            return dir is not null
                && string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(programs).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                && Directory.EnumerateFiles(dir, "unins*.exe").Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public void Dispose() => _timer.Stop();
}
