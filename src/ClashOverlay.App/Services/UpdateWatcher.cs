using System.Windows.Threading;
using ClashOverlay.Core.Updates;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>
/// "Check for updates": asks GitHub at most once a day whether a newer Clash Tracker is out. A new version gets one tray
/// note and an "Update available" item in the tray menu, both of which only open the release page. Nothing is
/// downloaded or installed.
/// </summary>
internal sealed class UpdateWatcher : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);

    private readonly AppHost _host;
    private readonly ReleaseChecker? _checker;
    private readonly Version _current;
    private readonly DispatcherTimer _timer = new() { Interval = FirstCheckDelay };

    public UpdateWatcher(AppHost host)
    {
        _host = host;
        _current = ReleaseChecker.ParseVersion(host.Version) ?? new Version(0, 0, 0);
        if (ReleaseChecker.IsValidRepository(host.GitHubRepository))
        {
            _checker = new ReleaseChecker(host.DataHttp, host.GitHubRepository!);
        }

        // A release found before the last restart, unless this copy is already that version or newer.
        var s = host.Settings;
        if (ReleaseChecker.ParseVersion(s.UpdateTag) is { } saved && saved > _current && s.UpdateUrl is { } url)
        {
            Available = new ReleaseInfo(saved, s.UpdateTag!, url, null);
        }

        _timer.Tick += (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(1);
            _ = CheckIfDueAsync();
        };
    }

    /// <summary>A newer release than this copy, or null.</summary>
    public ReleaseInfo? Available { get; private set; }

    public bool IsChecking { get; private set; }

    public bool CanCheck => _checker is not null && !_host.IsDemo;

    /// <summary>Builds from source are 0.0.0-dev; they never check by themselves.</summary>
    public bool IsDevBuild => _current == new Version(0, 0, 0);

    public string? ReleasesPage => _checker?.ReleasesPage;

    public event EventHandler? AvailableChanged;

    /// <summary>A version not announced before was found by an automatic check.</summary>
    public event EventHandler<ReleaseInfo>? NewVersionFound;

    public void Start()
    {
        if (!CanCheck)
        {
            return;
        }

        _timer.Start();
    }

    private async Task CheckIfDueAsync()
    {
        var s = _host.Settings;
        if (!s.CheckForUpdates || IsDevBuild || !ReleaseChecker.IsDue(s.LastUpdateCheck, _host.Time.GetUtcNow()))
        {
            return;
        }

        await CheckAsync(manual: false);
    }

    /// <summary>Runs one check now. Returns a sentence for the Settings page.</summary>
    public async Task<string> CheckNowAsync()
    {
        var result = await CheckAsync(manual: true);
        return result switch
        {
            null => "Checking…",
            { Outcome: ReleaseCheckOutcome.UpdateAvailable, Latest: { } r } => $"Clash Tracker {r.VersionText} is out. You have {_host.Version}.",
            { Outcome: ReleaseCheckOutcome.UpToDate } => $"You have the newest version ({_host.Version}).",
            { Outcome: ReleaseCheckOutcome.NoRelease } => "No release has been published yet.",
            _ => result.Error ?? "The check did not work. Clash Tracker tries again later.",
        };
    }

    private async Task<ReleaseCheckResult?> CheckAsync(bool manual)
    {
        if (_checker is null || IsChecking || _host.IsDemo)
        {
            return null;
        }

        IsChecking = true;
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
            var announce = false;
            if (result is { Outcome: ReleaseCheckOutcome.UpdateAvailable, Latest: { } latest })
            {
                SetAvailable(latest);
                s.UpdateTag = latest.Tag;
                s.UpdateUrl = latest.Url;
                announce = s.UpdateNotifiedTag != latest.Tag;
                s.UpdateNotifiedTag = latest.Tag;
            }
            else
            {
                SetAvailable(null);
                s.UpdateTag = null;
                s.UpdateUrl = null;
            }

            _host.SaveSettings();
            if (announce && !manual && result.Latest is { } found)
            {
                NewVersionFound?.Invoke(this, found);
            }

            return result;
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void SetAvailable(ReleaseInfo? release)
    {
        if (Available?.Version == release?.Version)
        {
            Available = release ?? Available;
            return;
        }

        Available = release;
        AvailableChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _timer.Stop();
}
