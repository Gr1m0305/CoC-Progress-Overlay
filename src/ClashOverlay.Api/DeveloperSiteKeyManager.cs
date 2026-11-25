using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClashOverlay.Api;

public sealed record DeveloperLogin(string Email, string Password);

/// <summary>The optional developer-site login in <c>devsite.bin</c>, DPAPI-protected (CurrentUser). Off unless the player turns it on.</summary>
public sealed class DeveloperLoginStore
{
    private static readonly byte[] Entropy = "ClashOverlay.DeveloperLogin.v1"u8.ToArray();
    private readonly string _path;

    public DeveloperLoginStore(string path)
    {
        _path = path;
    }

    public bool HasLogin => Load() is not null;

    public DeveloperLogin? Load()
    {
        if (ProtectedFile.Read(_path, Entropy) is not { } bytes)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var email = doc.RootElement.GetProperty("email").GetString();
            var password = doc.RootElement.GetProperty("password").GetString();
            return string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) ? null : new DeveloperLogin(email, password);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Save(DeveloperLogin login)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { email = login.Email.Trim(), password = login.Password });
        ProtectedFile.Write(_path, json, Entropy);
    }

    public void Delete() => ProtectedFile.Delete(_path);
}

public sealed class DeveloperSiteException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A key as the developer site lists it.</summary>
public sealed record DeveloperKey(string Id, string Name, IReadOnlyList<string> CidrRanges, IReadOnlyList<string> Scopes, string? Key);

/// <summary>
/// Optional, off by default: logs in to developer.clashofclans.com with the player's own account and makes sure there is
/// a key for the current IP address, the same way coc.py does. Only keys it created itself (named
/// <see cref="KeyName"/>) are ever revoked, and only when the account is at its key limit. The password and keys are
/// never logged.
/// </summary>
public sealed class DeveloperSiteKeyManager
{
    public const string BaseUrl = "https://developer.clashofclans.com/api/";
    public const string KeyName = "Clash Tracker";
    public const int KeyLimit = 10;

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    public DeveloperSiteKeyManager(HttpClient http, TimeProvider? time = null, ILogger? logger = null)
    {
        _http = http;
        _time = time ?? TimeProvider.System;
        _log = logger ?? NullLogger.Instance;
    }

    /// <summary>A client that keeps the site's session cookie between calls.</summary>
    public static HttpClient CreateHttpClient(string version) => new(new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true })
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", $"ClashOverlay/{version}" } },
    };

    /// <summary>Returns a key that allows <paramref name="ip"/> (or, when null, the address the site sees), creating one if needed.</summary>
    public async Task<(string Key, string Ip, bool Created)> EnsureKeyAsync(DeveloperLogin login, string? ip = null, CancellationToken ct = default)
    {
        var siteIp = await LoginAsync(login, ct).ConfigureAwait(false);
        ip ??= siteIp ?? throw new DeveloperSiteException("Could not tell this PC's public IP address.");

        var keys = await ListAsync(ct).ConfigureAwait(false);
        var match = keys.FirstOrDefault(k => k.Key is not null && k.Scopes.Contains("clash") && k.CidrRanges.Any(c => ApiKeyInfo.CidrContains(c, ip)));
        if (match is not null)
        {
            _log.LogInformation("Developer site: a key for this IP already exists ({Name})", match.Name);
            return (match.Key!, ip, false);
        }

        if (keys.Count >= KeyLimit)
        {
            var oldest = keys.FirstOrDefault(k => k.Name == KeyName)
                ?? throw new DeveloperSiteException($"The developer account already has {KeyLimit} keys. Delete one on developer.clashofclans.com and try again.");
            await PostAsync("apikey/revoke", new { id = oldest.Id }, ct).ConfigureAwait(false);
            _log.LogInformation("Developer site: revoked an old {Name} key to make room", KeyName);
        }

        var description = $"Created by Clash Tracker on {_time.GetUtcNow():yyyy-MM-dd HH:mm} UTC";
        using var created = await PostAsync("apikey/create",
            new { name = KeyName, description, cidrRanges = new[] { ip }, scopes = new[] { "clash" } }, ct).ConfigureAwait(false);
        var key = created.RootElement.TryGetProperty("key", out var k) && k.TryGetProperty("key", out var value) ? value.GetString() : null;
        if (string.IsNullOrEmpty(key))
        {
            throw new DeveloperSiteException("The developer site did not return the new key.");
        }

        _log.LogInformation("Developer site: created a key for the current IP");
        return (key, ip, true);
    }

    /// <summary>Logs in; returns the public IP the site saw (from its temporary token), when it says.</summary>
    internal async Task<string?> LoginAsync(DeveloperLogin login, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(BaseUrl + "login", new { email = login.Email, password = login.Password }, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new DeveloperSiteException("Could not reach developer.clashofclans.com.", e);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                throw new DeveloperSiteException("The developer site did not accept the e-mail and password.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DeveloperSiteException($"The developer site answered {(int)response.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var token = doc.RootElement.TryGetProperty("temporaryAPIToken", out var t) ? t.GetString() : null;
            return ApiKeyInfo.TryParse(token)?.AllowedIps.Select(c => c.Split('/')[0]).FirstOrDefault();
        }
    }

    internal async Task<List<DeveloperKey>> ListAsync(CancellationToken ct)
    {
        using var doc = await PostAsync("apikey/list", new { }, ct).ConfigureAwait(false);
        var keys = new List<DeveloperKey>();
        if (doc.RootElement.TryGetProperty("keys", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in list.EnumerateArray())
            {
                keys.Add(new DeveloperKey(
                    k.TryGetProperty("id", out var id) ? id.ToString() : "",
                    k.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Strings(k, "cidrRanges"),
                    Strings(k, "scopes"),
                    k.TryGetProperty("key", out var key) ? key.GetString() : null));
            }
        }

        return keys;
    }

    private async Task<JsonDocument> PostAsync(string path, object body, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(BaseUrl + path, content, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DeveloperSiteException($"The developer site refused {path} ({(int)response.StatusCode}).");
            }

            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch (Exception e) when (e is HttpRequestException || e is JsonException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new DeveloperSiteException($"The developer site call {path} failed.", e);
        }
    }

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToList()
            : [];
}
