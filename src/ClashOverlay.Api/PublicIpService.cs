using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClashOverlay.Api;

/// <summary>
/// This PC's public IPv4 address, asked from plain what-is-my-IP services (they get the request and nothing else).
/// Used to tell the player which address to add to the key, and to notice when it changes.
/// </summary>
public sealed class PublicIpService
{
    public static readonly string[] Endpoints = ["https://api.ipify.org", "https://checkip.amazonaws.com", "https://ipv4.icanhazip.com"];

    private readonly HttpClient _http;
    private readonly ILogger _log;

    public PublicIpService(HttpClient http, ILogger? logger = null)
    {
        _http = http;
        _log = logger ?? NullLogger.Instance;
    }

    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        foreach (var url in Endpoints)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var text = (await _http.GetStringAsync(url, cts.Token).ConfigureAwait(false)).Trim();
                if (IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }

                _log.LogDebug("Public IP: {Url} answered something else", url);
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                _log.LogDebug("Public IP: {Url} failed ({Error})", url, e.GetType().Name);
            }
        }

        return null;
    }
}
