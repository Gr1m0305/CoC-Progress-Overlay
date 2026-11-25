using System.Net;
using System.Text.RegularExpressions;
using ClashOverlay.Api.Models;

namespace ClashOverlay.Api;

public enum ApiErrorKind
{
    /// <summary>No key stored yet.</summary>
    NoKey,

    /// <summary>The key is wrong, revoked or expired (403 <c>accessDenied</c>).</summary>
    InvalidKey,

    /// <summary>The key is fine but locked to other IP addresses (403 <c>accessDenied.invalidIp</c>).</summary>
    InvalidIp,

    /// <summary>The clan's war log is private (403 on a war endpoint). Not a key problem.</summary>
    PrivateWarLog,

    NotFound,

    /// <summary>429 <c>requestThrottled</c>.</summary>
    Throttled,

    /// <summary>503 <c>inMaintenance</c>.</summary>
    Maintenance,

    BadRequest,

    ServerError,

    Network,

    Timeout,

    /// <summary>The answer was not the JSON we expected.</summary>
    BadResponse,
}

/// <summary>A failed API call. The message is safe to show and to log: it never contains the key.</summary>
public sealed class ClashApiException : Exception
{
    private static readonly Regex IpInMessage = new(@"\bIP\s+([0-9A-Fa-f:.]+)", RegexOptions.CultureInvariant);

    public ClashApiException(ApiErrorKind kind, string message, int? statusCode = null, string? reason = null,
        string? ipAddress = null, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        Reason = reason;
        IpAddress = ipAddress;
        RetryAfter = retryAfter;
    }

    public ApiErrorKind Kind { get; }

    public int? StatusCode { get; }

    /// <summary>The API's own <c>reason</c>, e.g. <c>accessDenied.invalidIp</c>.</summary>
    public string? Reason { get; }

    /// <summary>For <see cref="ApiErrorKind.InvalidIp"/>: the address the API saw, when its message names it.</summary>
    public string? IpAddress { get; }

    public TimeSpan? RetryAfter { get; }

    /// <summary>The user has to do something about the key (paste one, or allow this IP).</summary>
    public bool IsKeyProblem => Kind is ApiErrorKind.NoKey or ApiErrorKind.InvalidKey or ApiErrorKind.InvalidIp;

    /// <summary>Worth trying again later without changing anything.</summary>
    public bool IsTransient => Kind is ApiErrorKind.Throttled or ApiErrorKind.Maintenance or ApiErrorKind.ServerError
        or ApiErrorKind.Network or ApiErrorKind.Timeout;

    public static ClashApiException NoKey() =>
        new(ApiErrorKind.NoKey, "No API key yet. Paste one in Settings (developer.clashofclans.com > My Account > Create New Key).");

    public static ClashApiException FromResponse(HttpStatusCode status, string? body, TimeSpan? retryAfter, bool warEndpoint)
    {
        ApiErrorDto? error = null;
        try
        {
            error = string.IsNullOrWhiteSpace(body) ? null : ApiJson.Deserialize<ApiErrorDto>(body);
        }
        catch (System.Text.Json.JsonException)
        {
            // not JSON (a proxy's HTML page, say): fall back to the status code
        }

        var code = (int)status;
        var reason = error?.Reason;
        var apiMessage = error?.Message ?? "";

        if (code == 403 && string.Equals(reason, "accessDenied.invalidIp", StringComparison.OrdinalIgnoreCase))
        {
            var ip = IpInMessage.Match(apiMessage) is { Success: true } m && IPAddress.TryParse(m.Groups[1].Value.TrimEnd('.'), out var parsed)
                ? parsed.ToString()
                : null;
            var where = ip is null ? "this IP address" : $"this IP address ({ip})";
            return new ClashApiException(ApiErrorKind.InvalidIp,
                $"The API key does not allow {where}. Add it to the key on developer.clashofclans.com, or create a new key for it.",
                code, reason, ip);
        }

        if (code == 403 && warEndpoint)
        {
            return new ClashApiException(ApiErrorKind.PrivateWarLog, "The clan's war log is private, so war details are hidden.", code, reason);
        }

        return code switch
        {
            403 => new ClashApiException(ApiErrorKind.InvalidKey,
                "The API key was not accepted. It may have been revoked or mistyped; paste it again in Settings.", code, reason),
            404 => new ClashApiException(ApiErrorKind.NotFound, "Not found. Check the tag.", code, reason),
            429 => new ClashApiException(ApiErrorKind.Throttled, "Too many requests; waiting before trying again.", code, reason, retryAfter: retryAfter),
            503 => new ClashApiException(ApiErrorKind.Maintenance, "Clash of Clans is in maintenance. Showing saved data.", code, reason, retryAfter: retryAfter),
            400 => new ClashApiException(ApiErrorKind.BadRequest, "The API rejected the request.", code, reason),
            >= 500 => new ClashApiException(ApiErrorKind.ServerError, $"The API had a problem ({code}). Showing saved data.", code, reason, retryAfter: retryAfter),
            _ => new ClashApiException(ApiErrorKind.BadResponse, $"Unexpected answer from the API ({code}).", code, reason),
        };
    }
}
