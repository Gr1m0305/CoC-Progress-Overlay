using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClashOverlay.Api.Models;
using ClashOverlay.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClashOverlay.Api;

/// <summary>What a call returned: the value (fresh, or the last good copy) and any error.</summary>
public sealed class ApiResult<T>
    where T : class
{
    public T? Value { get; init; }

    public DateTimeOffset? FetchedAt { get; init; }

    public bool FromCache { get; init; }

    /// <summary>The value is an old copy because the API could not be asked or said no.</summary>
    public bool IsStale { get; init; }

    public ClashApiException? Error { get; init; }

    public bool HasValue => Value is not null;
}

public interface IClashApiClient
{
    /// <summary>The key was refused or does not allow this IP. Raised once per distinct problem.</summary>
    event EventHandler<ClashApiException>? KeyProblem;

    /// <summary>Set while the client is waiting after throttling, maintenance or a key problem.</summary>
    DateTimeOffset? PausedUntil { get; }

    Task<ApiResult<PlayerDto>> GetPlayerAsync(string tag, bool force = false, CancellationToken ct = default);

    Task<ApiResult<ClanDto>> GetClanAsync(string clanTag, bool force = false, CancellationToken ct = default);

    Task<ApiResult<ItemList<ClanMemberDto>>> GetClanMembersAsync(string clanTag, bool force = false, CancellationToken ct = default);

    Task<ApiResult<WarDto>> GetCurrentWarAsync(string clanTag, bool force = false, CancellationToken ct = default);

    Task<ApiResult<ItemList<WarLogEntryDto>>> GetWarLogAsync(string clanTag, bool force = false, CancellationToken ct = default);

    Task<ApiResult<LeagueGroupDto>> GetLeagueGroupAsync(string clanTag, bool force = false, CancellationToken ct = default);

    Task<ApiResult<WarDto>> GetCwlWarAsync(string warTag, bool force = false, CancellationToken ct = default);

    Task<ApiResult<ItemList<RaidSeasonDto>>> GetRaidSeasonsAsync(string clanTag, bool force = false, CancellationToken ct = default);

    Task<ApiResult<GoldPassDto>> GetGoldPassAsync(bool force = false, CancellationToken ct = default);

    /// <summary>Tries a key (for setup) without touching the stored one. Null when it works.</summary>
    Task<ClashApiException?> CheckKeyAsync(string key, CancellationToken ct = default);

    /// <summary>After the key or the network changed: allow calls again straight away.</summary>
    void ResetBackoff();
}

/// <summary>
/// The official Clash of Clans API with the player's own key. Responses are cached on disk; errors come back as
/// <see cref="ClashApiException"/> alongside the last good copy; the client waits after 429/503/network failures and
/// pauses on key problems. The key is sent only in the Authorization header and is never logged.
/// </summary>
public sealed class ClashApiClient : IClashApiClient
{
    public const string BaseUrl = "https://api.clashofclans.com/v1/";

    private readonly HttpClient _http;
    private readonly Func<string?> _key;
    private readonly ResponseCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly BackoffGate _gate;
    private readonly SemaphoreSlim _concurrency = new(4);
    private string? _lastProblem;

    public ClashApiClient(HttpClient http, Func<string?> key, ResponseCache cache, TimeProvider? time = null, ILogger? logger = null)
    {
        _http = http;
        _key = key;
        _cache = cache;
        _time = time ?? TimeProvider.System;
        _log = logger ?? NullLogger.Instance;
        _gate = new BackoffGate(_time);
    }

    public event EventHandler<ClashApiException>? KeyProblem;

    public DateTimeOffset? PausedUntil => _gate.ClosedUntil;

    /// <summary>How long each kind of answer counts as fresh.</summary>
    public static class MaxAge
    {
        public static readonly TimeSpan Player = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan Clan = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan War = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan WarLog = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan LeagueGroup = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan EndedWar = TimeSpan.FromDays(30);
        public static readonly TimeSpan Raids = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan GoldPass = TimeSpan.FromHours(6);
    }

    public static HttpClient CreateHttpClient(string version) => new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", $"ClashOverlay/{version}" } },
    };

    public static string PlayerScope(string tag) => PlayerTag.ForPath(tag);

    public static string ClanScope(string clanTag) => "clan-" + PlayerTag.ForPath(clanTag);

    public Task<ApiResult<PlayerDto>> GetPlayerAsync(string tag, bool force = false, CancellationToken ct = default) =>
        GetAsync<PlayerDto>($"players/{PlayerTag.ForUrl(tag)}", PlayerScope(tag), "player", MaxAge.Player, force, false, null, ct);

    public Task<ApiResult<ClanDto>> GetClanAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        GetAsync<ClanDto>($"clans/{PlayerTag.ForUrl(clanTag)}", ClanScope(clanTag), "clan", MaxAge.Clan, force, false, null, ct);

    public Task<ApiResult<ItemList<ClanMemberDto>>> GetClanMembersAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        GetAsync<ItemList<ClanMemberDto>>($"clans/{PlayerTag.ForUrl(clanTag)}/members", ClanScope(clanTag), "members", MaxAge.Clan, force, false, null, ct);

    public Task<ApiResult<WarDto>> GetCurrentWarAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        GetAsync<WarDto>($"clans/{PlayerTag.ForUrl(clanTag)}/currentwar", ClanScope(clanTag), "currentwar", MaxAge.War, force, true, null, ct);

    public Task<ApiResult<ItemList<WarLogEntryDto>>> GetWarLogAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        GetAsync<ItemList<WarLogEntryDto>>($"clans/{PlayerTag.ForUrl(clanTag)}/warlog?limit=10", ClanScope(clanTag), "warlog", MaxAge.WarLog, force, true, null, ct);

    public Task<ApiResult<LeagueGroupDto>> GetLeagueGroupAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        GetAsync<LeagueGroupDto>($"clans/{PlayerTag.ForUrl(clanTag)}/currentwar/leaguegroup", ClanScope(clanTag), "leaguegroup", MaxAge.LeagueGroup, force, true, null, ct);

    public Task<ApiResult<WarDto>> GetCwlWarAsync(string warTag, bool force = false, CancellationToken ct = default) =>
        GetAsync<WarDto>($"clanwarleagues/wars/{PlayerTag.ForUrl(warTag)}", "cwl", PlayerTag.ForPath(warTag), MaxAge.War, force, true,
            w => w.IsEnded ? MaxAge.EndedWar : MaxAge.War, ct);

    public Task<ApiResult<ItemList<RaidSeasonDto>>> GetRaidSeasonsAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        GetAsync<ItemList<RaidSeasonDto>>($"clans/{PlayerTag.ForUrl(clanTag)}/capitalraidseasons?limit=1", ClanScope(clanTag), "raids", MaxAge.Raids, force, false, null, ct);

    public Task<ApiResult<GoldPassDto>> GetGoldPassAsync(bool force = false, CancellationToken ct = default) =>
        GetAsync<GoldPassDto>("goldpass/seasons/current", "global", "goldpass", MaxAge.GoldPass, force, false, null, ct);

    public async Task<ClashApiException?> CheckKeyAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return ClashApiException.NoKey();
        }

        try
        {
            using var request = Request("goldpass/seasons/current", key.Trim());
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ClashApiException.FromResponse(response.StatusCode, body, RetryAfter(response), warEndpoint: false);
        }
        catch (Exception e) when (IsNetworkFailure(e, ct))
        {
            return NetworkError(e);
        }
    }

    public void ResetBackoff()
    {
        _gate.Reset();
        _lastProblem = null;
    }

    private async Task<ApiResult<T>> GetAsync<T>(string path, string scope, string key, TimeSpan maxAge, bool force, bool warEndpoint,
        Func<T, TimeSpan>? maxAgeFor, CancellationToken ct)
        where T : class
    {
        var cached = _cache.Read(scope, key);
        var cachedValue = cached is null ? null : TryParse<T>(cached.Body);
        if (!force && cached is not null && cachedValue is not null && cached.IsFreshAt(_time.GetUtcNow()))
        {
            return new ApiResult<T> { Value = cachedValue, FetchedAt = cached.FetchedAt, FromCache = true };
        }

        ApiResult<T> Fallback(ClashApiException error) => new()
        {
            Value = cachedValue,
            FetchedAt = cachedValue is null ? null : cached!.FetchedAt,
            FromCache = cachedValue is not null,
            IsStale = cachedValue is not null,
            Error = error,
        };

        var apiKey = _key();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Fallback(ClashApiException.NoKey());
        }

        if (_gate.IsClosed && _gate.LastError is { } waiting)
        {
            return Fallback(waiting);
        }

        await _concurrency.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var request = Request(path, apiKey);
            var clock = Stopwatch.StartNew();
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _log.LogDebug("GET {Path} {Status} in {Ms} ms", path, (int)response.StatusCode, clock.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                var error = ClashApiException.FromResponse(response.StatusCode, body, RetryAfter(response), warEndpoint);
                Failed(path, error);
                return Fallback(error);
            }

            var value = TryParse<T>(body);
            if (value is null)
            {
                var error = new ClashApiException(ApiErrorKind.BadResponse, "The API's answer could not be read.", (int)response.StatusCode);
                _log.LogWarning("GET {Path}: unreadable body ({Length} chars)", path, body.Length);
                return Fallback(error);
            }

            _gate.Success();
            _lastProblem = null;
            var fetchedAt = _time.GetUtcNow();
            var age = maxAgeFor?.Invoke(value) ?? maxAge;
            if (response.Headers.CacheControl?.MaxAge is { } serverAge && serverAge > age)
            {
                age = serverAge;
            }

            _cache.Write(scope, key, body, fetchedAt, age);
            return new ApiResult<T> { Value = value, FetchedAt = fetchedAt };
        }
        catch (Exception e) when (IsNetworkFailure(e, ct))
        {
            var error = NetworkError(e);
            Failed(path, error);
            return Fallback(error);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private void Failed(string path, ClashApiException error)
    {
        _gate.Failure(error);
        if (error.Kind is ApiErrorKind.NotFound or ApiErrorKind.PrivateWarLog)
        {
            _log.LogInformation("GET {Path}: {Kind}", path, error.Kind);
        }
        else
        {
            _log.LogWarning("GET {Path}: {Kind} ({Reason}) {Message}", path, error.Kind, error.Reason, error.Message);
        }

        if (error.Kind is ApiErrorKind.InvalidIp or ApiErrorKind.InvalidKey)
        {
            var id = $"{error.Kind}:{error.IpAddress}";
            if (id != _lastProblem)
            {
                _lastProblem = id;
                KeyProblem?.Invoke(this, error);
            }
        }
    }

    private static HttpRequestMessage Request(string path, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static T? TryParse<T>(string body)
        where T : class
    {
        try
        {
            return ApiJson.Deserialize<T>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } retry)
        {
            return null;
        }

        if (retry.Delta is { } delta)
        {
            return delta;
        }

        return retry.Date is { } date && date > _time.GetUtcNow() ? date - _time.GetUtcNow() : null;
    }

    private static bool IsNetworkFailure(Exception e, CancellationToken ct) =>
        e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested);

    private static ClashApiException NetworkError(Exception e) => e is OperationCanceledException
        ? new ClashApiException(ApiErrorKind.Timeout, "The API did not answer in time. Showing saved data.", inner: e)
        : new ClashApiException(ApiErrorKind.Network, "No connection to the API. Showing saved data.", inner: e);
}
