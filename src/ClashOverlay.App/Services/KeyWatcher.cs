using ClashOverlay.Api;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>
/// Notices when the API key stops working: the public IP changed (keys are locked to IP addresses) or the key was
/// refused. Shows a tray alert that opens the key steps, or, when the user opted in, makes a key for the new address
/// with their saved developer-site login.
/// </summary>
internal sealed class KeyWatcher
{
    private readonly AppHost _host;
    private readonly Action<string, string, Action?, bool> _notify;
    private readonly Action _openKeySteps;
    private readonly ILogger _log;
    private bool _making;

    /// <param name="notify">Tray balloon: title, text, click action, warning.</param>
    public KeyWatcher(AppHost host, Action<string, string, Action?, bool> notify, Action openKeySteps)
    {
        _host = host;
        _notify = notify;
        _openKeySteps = openKeySteps;
        _log = host.Loggers.CreateLogger("KeyWatcher");
        var ui = System.Windows.Application.Current.Dispatcher;
        host.Api.KeyProblem += (_, e) => ui.BeginInvoke(() => _ = OnKeyProblemAsync(e));
    }

    /// <summary>At start-up: tell the user before the first call fails when the key does not cover this PC's address.</summary>
    public async Task CheckAtStartAsync()
    {
        if (_host.IsDemo || _host.Keys.Load() is not { } key || ApiKeyInfo.TryParse(key) is not { } info)
        {
            return;
        }

        var ip = await _host.PublicIp.GetAsync();
        if (ip is null)
        {
            return;
        }

        Remember(ip);
        if (!info.Allows(ip))
        {
            _log.LogInformation("The saved key does not allow this PC's current public IP");
            await OnIpChangedAsync(ip);
        }
    }

    private async Task OnKeyProblemAsync(ClashApiException e)
    {
        switch (e.Kind)
        {
            case ApiErrorKind.InvalidIp:
                var ip = e.IpAddress ?? await _host.PublicIp.GetAsync();
                if (ip is not null)
                {
                    Remember(ip);
                }

                await OnIpChangedAsync(ip);
                break;
            case ApiErrorKind.InvalidKey:
                _notify("API key refused", "The Clash of Clans API refused the saved key. Click to set up a new one.", _openKeySteps, true);
                break;
        }
    }

    private async Task OnIpChangedAsync(string? ip)
    {
        if (_host.Settings.DeveloperSiteAutoKey && _host.Logins.Load() is { } login && !_making)
        {
            try
            {
                _making = true;
                var message = await MakeKeyAsync(login, ip, CancellationToken.None);
                _notify("New API key", message + " Clash Tracker made it with your saved developer-site login.", null, false);
                return;
            }
            catch (Exception ex) when (ex is DeveloperSiteException or HttpRequestException or TaskCanceledException)
            {
                _log.LogWarning("Automatic key failed: {Message}", ex.Message);
                _notify("Your IP address changed", $"Making a new key automatically failed: {ex.Message} Click to make one by hand.", _openKeySteps, true);
                return;
            }
            finally
            {
                _making = false;
            }
        }

        var address = ip is null ? "this PC's new address" : ip;
        _notify("Your IP address changed", $"Your API key doesn't allow {address}. Click to make a new key for it.", _openKeySteps, true);
    }

    /// <summary>
    /// Logs in to developer.clashofclans.com, finds or creates a key for <paramref name="ip"/> (or the address the site
    /// sees), and saves it. Returns a sentence for the user.
    /// </summary>
    public async Task<string> MakeKeyAsync(DeveloperLogin login, string? ip, CancellationToken ct)
    {
        using var http = DeveloperSiteKeyManager.CreateHttpClient(_host.Version);
        var manager = new DeveloperSiteKeyManager(http, _host.Time, _host.Loggers.CreateLogger<DeveloperSiteKeyManager>());
        var (key, usedIp, created) = await manager.EnsureKeyAsync(login, ip, ct);
        _host.Keys.Save(key);
        _host.Api.ResetBackoff();
        Remember(usedIp);
        return created ? $"Made a new API key for {usedIp}." : $"Found an existing API key for {usedIp}.";
    }

    private void Remember(string ip)
    {
        if (_host.Settings.LastKnownIp != ip)
        {
            _host.Settings.LastKnownIp = ip;
            _host.SaveSettings();
        }
    }
}
