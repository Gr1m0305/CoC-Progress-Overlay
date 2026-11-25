using System.Net;
using System.Text.Json;
using ClashOverlay.Api;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Api;

public class DeveloperSiteKeyManagerTests
{
    private const string Base = DeveloperSiteKeyManager.BaseUrl;
    private static readonly DeveloperLogin Login = new("chief@example.com", "correct horse");

    private static string LoginOk(string ip) =>
        JsonSerializer.Serialize(new { status = new { code = 0, message = "ok" }, temporaryAPIToken = FakeKeys.Make($"{ip}/32") });

    private static object Key(string id, string name, string ip) =>
        new { id, name, description = "", scopes = new[] { "clash" }, cidrRanges = new[] { ip }, key = FakeKeys.Make(ip) };

    private static string List(params object[] keys) => JsonSerializer.Serialize(new { status = new { code = 0 }, keys });

    private static FakeHttpHandler Site(string ip, string list, string? created = null)
    {
        var http = new FakeHttpHandler()
            .OnJson(Base + "login", LoginOk(ip))
            .OnJson(Base + "apikey/list", list)
            .OnJson(Base + "apikey/revoke", "{\"status\":{\"code\":0}}");
        if (created is not null)
        {
            http.OnJson(Base + "apikey/create", JsonSerializer.Serialize(new { status = new { code = 0 }, key = new { id = "new", key = created } }));
        }

        return http;
    }

    [Fact]
    public async Task Reuses_a_key_that_already_allows_this_ip()
    {
        var http = Site("203.0.113.7", List(Key("1", "Other app", "198.51.100.1"), Key("2", "Mine", "203.0.113.7")));

        var (key, ip, created) = await new DeveloperSiteKeyManager(new HttpClient(http)).EnsureKeyAsync(Login);

        Assert.Equal(FakeKeys.Make("203.0.113.7"), key);
        Assert.Equal("203.0.113.7", ip);
        Assert.False(created);
        Assert.Equal(0, http.Count("apikey/create"));
    }

    [Fact]
    public async Task Creates_a_key_for_a_new_ip()
    {
        var newKey = FakeKeys.Make("203.0.113.8");
        var http = Site("203.0.113.8", List(Key("1", DeveloperSiteKeyManager.KeyName, "203.0.113.7")), newKey);

        var (key, ip, created) = await new DeveloperSiteKeyManager(new HttpClient(http)).EnsureKeyAsync(Login);

        Assert.Equal(newKey, key);
        Assert.Equal("203.0.113.8", ip);
        Assert.True(created);
        Assert.Equal(0, http.Count("apikey/revoke"));
        var body = JsonDocument.Parse(http.RequestBodies[http.Requests.FindIndex(r => r.RequestUri!.ToString().EndsWith("create"))]).RootElement;
        Assert.Equal(DeveloperSiteKeyManager.KeyName, body.GetProperty("name").GetString());
        Assert.Equal("203.0.113.8", body.GetProperty("cidrRanges")[0].GetString());
        Assert.Equal("clash", body.GetProperty("scopes")[0].GetString());
        var login = JsonDocument.Parse(http.RequestBodies[0]).RootElement;
        Assert.Equal("chief@example.com", login.GetProperty("email").GetString());
    }

    [Fact]
    public async Task At_the_limit_only_its_own_oldest_key_is_revoked()
    {
        var keys = Enumerable.Range(1, 9).Select(i => Key($"o{i}", "Someone else", $"198.51.100.{i}")).ToList();
        keys.Insert(3, Key("mine", DeveloperSiteKeyManager.KeyName, "203.0.113.1"));
        var http = Site("203.0.113.8", List([.. keys]), FakeKeys.Make("203.0.113.8"));

        await new DeveloperSiteKeyManager(new HttpClient(http)).EnsureKeyAsync(Login);

        var revoke = JsonDocument.Parse(http.RequestBodies[http.Requests.FindIndex(r => r.RequestUri!.ToString().EndsWith("revoke"))]).RootElement;
        Assert.Equal("mine", revoke.GetProperty("id").GetString());
    }

    [Fact]
    public async Task At_the_limit_with_no_key_of_its_own_it_asks_the_player()
    {
        var keys = Enumerable.Range(1, 10).Select(i => Key($"o{i}", "Someone else", $"198.51.100.{i}")).ToArray();
        var http = Site("203.0.113.8", List(keys), FakeKeys.Make("203.0.113.8"));

        var e = await Assert.ThrowsAsync<DeveloperSiteException>(() => new DeveloperSiteKeyManager(new HttpClient(http)).EnsureKeyAsync(Login));

        Assert.Contains("10 keys", e.Message);
        Assert.Equal(0, http.Count("apikey/revoke"));
        Assert.Equal(0, http.Count("apikey/create"));
    }

    [Fact]
    public async Task Wrong_password()
    {
        var http = new FakeHttpHandler().OnJson(Base + "login", "{\"status\":{\"code\":403,\"message\":\"invalid credentials\"}}", HttpStatusCode.Forbidden);
        var logger = new CapturingLogger();

        var e = await Assert.ThrowsAsync<DeveloperSiteException>(() => new DeveloperSiteKeyManager(new HttpClient(http), logger: logger).EnsureKeyAsync(Login));

        Assert.Contains("did not accept", e.Message);
        Assert.DoesNotContain("correct horse", e.Message);
        Assert.DoesNotContain(logger.Lines, l => l.Contains("correct horse"));
    }

    [Fact]
    public async Task Uses_the_given_ip_when_the_site_does_not_say()
    {
        var http = new FakeHttpHandler()
            .OnJson(Base + "login", "{\"status\":{\"code\":0}}")
            .OnJson(Base + "apikey/list", List())
            .OnJson(Base + "apikey/create", JsonSerializer.Serialize(new { key = new { key = "eyJnew" } }));

        var (_, ip, created) = await new DeveloperSiteKeyManager(new HttpClient(http)).EnsureKeyAsync(Login, "203.0.113.50");

        Assert.Equal("203.0.113.50", ip);
        Assert.True(created);
    }
}
