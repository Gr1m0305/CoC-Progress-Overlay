using ClashOverlay.Api;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>
/// Notices when the API key stops working: the public IP changed (keys are locked to IP addresses) or the key was
/// refused. It puts a line at the top of the tray menu (<see cref="Attention"/>) that opens the key steps, or, when
/// the user opted in, makes a key for the new address with their saved developer-site login. The overlay shows the
/// same problem in its notice line. Saving a new key clears it.
/// </summary>
internal sealed class KeyWatcher
{
    private readonly AppHost _host;
    private readonly ILogger _log;
    private bool _making;

    public KeyWatcher(AppHost host, Action openKeySteps)
    {
        _host = host;
        OpenKeySteps = openKeySteps;
        _log = host.Loggers.CreateLogger("KeyWatcher");
        var ui = System.Windows.Application.Current.Dispatcher;
        host.Api.KeyProblem += (_, e) => ui.BeginInvoke(() => _ = OnKeyProblemAsync(e));
        host.Keys.Changed += (_, _) => ui.BeginInvoke(() => SetAttention(null));
    }

    /// <summary>A short line about a key problem for the tray menu, or null.</summary>
    public string? Attention { get; private set; }

    public Action OpenKeySteps { get; }

    public event EventHandler? AttentionChanged;

    private void SetAttention(string? text)
    {
        if (Attention != text)
        {
            Attention = text;
            AttentionChanged?.Invoke(this, EventArgs.Empty);
        }
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
                _log.LogInformation("The API refused the saved key");
                SetAttention("API key refused: make a new one…");
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
                _log.LogInformation("{Message} (automatic, with the saved developer-site login)", message);
                return;
            }
            catch (Exception ex) when (ex is DeveloperSiteException or HttpRequestException or TaskCanceledException)
            {
                _log.LogWarning("Automatic key failed: {Message}", ex.Message);
                SetAttention("Your IP address changed: make a new API key…");
                return;
            }
            finally
            {
                _making = false;
            }
        }

        SetAttention("Your IP address changed: make a new API key…");
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
