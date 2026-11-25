using System.Net;
using ClashOverlay.Api;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Api;

public sealed class ClashApiClientTests : IDisposable
{
    private const string Tag = "#2PQ8LV9CY";
    private const string PlayerUrl = "https://api.clashofclans.com/v1/players/%232PQ8LV9CY";
    private const string WarUrl = "https://api.clashofclans.com/v1/clans/%232YQJ0PLCR/currentwar";
    private const string InvalidIp = """{"reason":"accessDenied.invalidIp","message":"Invalid authorization: API key does not allow access from IP 203.0.113.7"}""";

    private readonly TempDir _dir = new();
    private readonly FakeHttpHandler _http = new();
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly CapturingLogger _log = new();
    private readonly string _key = FakeKeys.Make("203.0.113.9");
    private string? _currentKey;

    public ClashApiClientTests()
    {
        _currentKey = _key;
    }

    public void Dispose() => _dir.Dispose();

    private ClashApiClient Client() => new(new HttpClient(_http), () => _currentKey, new ResponseCache(_dir.Path), _time, _log);

    private static string Player => Fixtures.Read("api", "demo_player.json");

    [Fact]
    public async Task Sends_the_key_as_a_bearer_token_and_encodes_the_tag()
    {
        _http.OnJson(PlayerUrl, Player);

        var result = await Client().GetPlayerAsync("2pq8lv9cy");

        Assert.True(result.HasValue);
        Assert.Null(result.Error);
        Assert.Equal("Demo Chief", result.Value!.Name);
        var request = Assert.Single(_http.Requests);
        Assert.Equal(PlayerUrl, request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(_key, request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Fresh_answers_come_from_the_cache_until_they_are_old()
    {
        _http.OnJson(PlayerUrl, Player);
        var client = Client();

        await client.GetPlayerAsync(Tag);
        var second = await client.GetPlayerAsync(Tag);
        Assert.True(second.FromCache);
        Assert.Equal(1, _http.Count("players"));

        _time.Advance(ClashApiClient.MaxAge.Player + TimeSpan.FromSeconds(1));
        var third = await client.GetPlayerAsync(Tag);
        Assert.False(third.FromCache);
        Assert.Equal(2, _http.Count("players"));

        await client.GetPlayerAsync(Tag, force: true);
        Assert.Equal(3, _http.Count("players"));
    }

    [Fact]
    public async Task The_disk_cache_survives_a_restart()
    {
        _http.OnJson(PlayerUrl, Player);
        await Client().GetPlayerAsync(Tag);

        var again = await Client().GetPlayerAsync(Tag);

        Assert.True(again.FromCache);
        Assert.Equal(_time.Now, again.FetchedAt);
        Assert.Equal(1, _http.Count("players"));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "2PQ8LV9CY", "player.json")));
    }

    [Fact]
    public async Task Offline_returns_the_last_good_copy_marked_stale()
    {
        var online = true;
        _http.On(PlayerUrl, _ => online ? FakeHttpHandler.Json(Player) : throw new HttpRequestException("no route"));
        var client = Client();
        await client.GetPlayerAsync(Tag);
        online = false;
        _time.Advance(TimeSpan.FromHours(1));

        var result = await client.GetPlayerAsync(Tag);

        Assert.True(result.IsStale);
        Assert.Equal("Demo Chief", result.Value!.Name);
        Assert.Equal(ApiErrorKind.Network, result.Error!.Kind);
        Assert.Equal(_time.Now - TimeSpan.FromHours(1), result.FetchedAt);
    }

    [Fact]
    public async Task Invalid_ip_names_the_address_raises_one_event_and_pauses()
    {
        _http.OnJson(PlayerUrl, InvalidIp, HttpStatusCode.Forbidden);
        var client = Client();
        var events = new List<ClashApiException>();
        client.KeyProblem += (_, e) => events.Add(e);

        var first = await client.GetPlayerAsync(Tag);
        var second = await client.GetPlayerAsync(Tag, force: true);

        Assert.Equal(ApiErrorKind.InvalidIp, first.Error!.Kind);
        Assert.Equal("203.0.113.7", first.Error.IpAddress);
        Assert.Contains("203.0.113.7", first.Error.Message);
        Assert.True(first.Error.IsKeyProblem);
        Assert.Single(events);
        Assert.Equal(ApiErrorKind.InvalidIp, second.Error!.Kind);
        Assert.Equal(1, _http.Count("players")); // paused: the second call never went out
        Assert.NotNull(client.PausedUntil);

        client.ResetBackoff(); // e.g. the player fixed the key
        await client.GetPlayerAsync(Tag);
        Assert.Equal(2, _http.Count("players"));
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task Invalid_ip_without_an_address_in_the_message()
    {
        _http.OnJson(PlayerUrl, """{"reason":"accessDenied.invalidIp","message":"Invalid authorization"}""", HttpStatusCode.Forbidden);
        var error = (await Client().GetPlayerAsync(Tag)).Error!;
        Assert.Equal(ApiErrorKind.InvalidIp, error.Kind);
        Assert.Null(error.IpAddress);
    }

    [Fact]
    public async Task Wrong_key()
    {
        _http.OnJson(PlayerUrl, """{"reason":"accessDenied","message":"Invalid authorization"}""", HttpStatusCode.Forbidden);
        var error = (await Client().GetPlayerAsync(Tag)).Error!;
        Assert.Equal(ApiErrorKind.InvalidKey, error.Kind);
        Assert.True(error.IsKeyProblem);
    }

    [Fact]
    public async Task No_key_means_no_request()
    {
        _currentKey = null;
        var result = await Client().GetPlayerAsync(Tag);
        Assert.Equal(ApiErrorKind.NoKey, result.Error!.Kind);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Private_war_log_is_not_a_key_problem_and_does_not_pause()
    {
        _http.OnJson(WarUrl, """{"reason":"accessDenied","message":"Access denied"}""", HttpStatusCode.Forbidden);
        _http.OnJson(PlayerUrl, Player);
        var client = Client();

        var war = await client.GetCurrentWarAsync("#2YQJ0PLCR");
        var player = await client.GetPlayerAsync(Tag);

        Assert.Equal(ApiErrorKind.PrivateWarLog, war.Error!.Kind);
        Assert.False(war.Error.IsKeyProblem);
        Assert.Null(player.Error);
        Assert.Null(client.PausedUntil);
    }

    [Fact]
    public async Task Not_found()
    {
        _http.OnJson(PlayerUrl, """{"reason":"notFound","message":"Not found"}""", HttpStatusCode.NotFound);
        var client = Client();
        Assert.Equal(ApiErrorKind.NotFound, (await client.GetPlayerAsync(Tag)).Error!.Kind);
        Assert.Null(client.PausedUntil);
    }

    [Fact]
    public async Task Throttling_honours_retry_after()
    {
        var calls = 0;
        _http.On(PlayerUrl, _ =>
        {
            calls++;
            if (calls > 1)
            {
                return FakeHttpHandler.Json(Player);
            }

            var r = FakeHttpHandler.Json("""{"reason":"requestThrottled","message":"Request was throttled"}""", (HttpStatusCode)429);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return r;
        });
        var client = Client();

        var first = await client.GetPlayerAsync(Tag);
        Assert.Equal(ApiErrorKind.Throttled, first.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(30), first.Error.RetryAfter);
        Assert.Equal(_time.Now.AddSeconds(30), client.PausedUntil);

        _time.Advance(TimeSpan.FromSeconds(20));
        await client.GetPlayerAsync(Tag);
        Assert.Equal(1, calls);

        _time.Advance(TimeSpan.FromSeconds(11));
        var later = await client.GetPlayerAsync(Tag);
        Assert.Null(later.Error);
        Assert.Equal(2, calls);
        Assert.Null(client.PausedUntil);
    }

    [Fact]
    public async Task Maintenance_waits_longer_each_time()
    {
        _http.OnJson(PlayerUrl, """{"reason":"inMaintenance","message":"Maintenance"}""", HttpStatusCode.ServiceUnavailable);
        var client = Client();

        Assert.Equal(ApiErrorKind.Maintenance, (await client.GetPlayerAsync(Tag)).Error!.Kind);
        var firstWait = client.PausedUntil!.Value - _time.Now;
        _time.Advance(firstWait + TimeSpan.FromSeconds(1));
        await client.GetPlayerAsync(Tag);
        var secondWait = client.PausedUntil!.Value - _time.Now;

        Assert.Equal(TimeSpan.FromMinutes(1), firstWait);
        Assert.Equal(TimeSpan.FromMinutes(2), secondWait);
    }

    [Fact]
    public async Task Html_error_pages_do_not_break_parsing()
    {
        _http.On(PlayerUrl, _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") });
        var error = (await Client().GetPlayerAsync(Tag)).Error!;
        Assert.Equal(ApiErrorKind.ServerError, error.Kind);
        Assert.True(error.IsTransient);
    }

    [Fact]
    public async Task Timeouts_are_reported_as_timeouts()
    {
        _http.On(PlayerUrl, _ => throw new TaskCanceledException("timeout"));
        Assert.Equal(ApiErrorKind.Timeout, (await Client().GetPlayerAsync(Tag)).Error!.Kind);
    }

    [Fact]
    public async Task Ended_cwl_wars_are_kept_for_good()
    {
        var wars = System.Text.Json.JsonDocument.Parse(Fixtures.Read("api", "demo_cwlwars.json"));
        var first = wars.RootElement.EnumerateObject().First();
        var url = "https://api.clashofclans.com/v1/clanwarleagues/wars/" + Uri.EscapeDataString(first.Name);
        _http.OnJson(url, first.Value.GetRawText());
        var client = Client();

        await client.GetCwlWarAsync(first.Name);
        _time.Advance(TimeSpan.FromDays(3));
        var again = await client.GetCwlWarAsync(first.Name);

        Assert.True(again.FromCache);
        Assert.Equal(1, _http.Count("clanwarleagues"));
    }

    [Fact]
    public async Task Server_cache_control_extends_the_max_age()
    {
        _http.On(PlayerUrl, _ =>
        {
            var r = FakeHttpHandler.Json(Player);
            r.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxAge = TimeSpan.FromMinutes(10), Public = true };
            return r;
        });
        var client = Client();
        await client.GetPlayerAsync(Tag);
        _time.Advance(TimeSpan.FromMinutes(7));
        Assert.True((await client.GetPlayerAsync(Tag)).FromCache);
    }

    [Fact]
    public async Task The_key_never_reaches_the_log()
    {
        _http.OnJson(PlayerUrl, InvalidIp, HttpStatusCode.Forbidden);
        _http.OnJson(WarUrl, "{\"state\":\"notInWar\"}");
        var client = Client();
        await client.GetPlayerAsync(Tag);
        client.ResetBackoff();
        await client.GetCurrentWarAsync("#2YQJ0PLCR");
        await client.CheckKeyAsync(_key);

        Assert.NotEmpty(_log.Lines);
        Assert.DoesNotContain(_log.Lines, l => l.Contains(_key, StringComparison.Ordinal) || l.Contains(_key[..20], StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_key_reports_problems_without_pausing()
    {
        const string gold = "https://api.clashofclans.com/v1/goldpass/seasons/current";
        _http.OnJson(gold, InvalidIp, HttpStatusCode.Forbidden);
        var client = Client();

        var error = await client.CheckKeyAsync("  " + _key + " ");

        Assert.Equal(ApiErrorKind.InvalidIp, error!.Kind);
        Assert.Null(client.PausedUntil);
        Assert.Equal(_key, _http.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task A_damaged_cache_file_is_ignored()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "2PQ8LV9CY"));
        await File.WriteAllTextAsync(Path.Combine(_dir.Path, "2PQ8LV9CY", "player.json"), "{ not json");
        _http.OnJson(PlayerUrl, Player);

        var result = await Client().GetPlayerAsync(Tag);

        Assert.False(result.FromCache);
        Assert.Equal("Demo Chief", result.Value!.Name);
    }

    [Theory]
    [InlineData(ApiErrorKind.Throttled, 1, 5)]
    [InlineData(ApiErrorKind.Throttled, 3, 20)]
    [InlineData(ApiErrorKind.Network, 1, 10)]
    [InlineData(ApiErrorKind.Network, 20, 300)]
    [InlineData(ApiErrorKind.InvalidIp, 1, 300)]
    [InlineData(ApiErrorKind.NotFound, 1, 0)]
    [InlineData(ApiErrorKind.PrivateWarLog, 1, 0)]
    public void Backoff_waits(ApiErrorKind kind, int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), BackoffGate.WaitFor(new ClashApiException(kind, "x"), failures));
}
