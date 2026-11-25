using System.Net;
using System.Text;
using ClashOverlay.Api;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Api;

public sealed class KeyStorageTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Api_key_round_trips_and_is_not_stored_in_plain_text()
    {
        var key = FakeKeys.Make("203.0.113.7");
        var store = new ApiKeyStore(_dir.File("apikey.bin"));
        Assert.False(store.HasKey);

        store.Save("  Bearer " + key + "\r\n");

        var bytes = File.ReadAllBytes(_dir.File("apikey.bin"));
        Assert.DoesNotContain(key[..30], Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain(key[..30], Encoding.Unicode.GetString(bytes));
        Assert.Equal(key, new ApiKeyStore(_dir.File("apikey.bin")).Load());

        store.Delete();
        Assert.False(File.Exists(_dir.File("apikey.bin")));
        Assert.Null(new ApiKeyStore(_dir.File("apikey.bin")).Load());
    }

    [Fact]
    public void A_file_from_someone_else_reads_as_no_key()
    {
        File.WriteAllBytes(_dir.File("apikey.bin"), [1, 2, 3, 4, 5]);
        Assert.Null(new ApiKeyStore(_dir.File("apikey.bin")).Load());
    }

    [Fact]
    public void Developer_login_round_trips()
    {
        var store = new DeveloperLoginStore(_dir.File("devsite.bin"));
        Assert.False(store.HasLogin);

        store.Save(new DeveloperLogin(" chief@example.com ", "correct horse"));

        Assert.DoesNotContain("correct horse", Encoding.UTF8.GetString(File.ReadAllBytes(_dir.File("devsite.bin"))));
        Assert.Equal(new DeveloperLogin("chief@example.com", "correct horse"), new DeveloperLoginStore(_dir.File("devsite.bin")).Load());
        store.Delete();
        Assert.False(store.HasLogin);
    }

    [Fact]
    public void Key_payload_lists_the_allowed_addresses()
    {
        var info = ApiKeyInfo.TryParse(FakeKeys.Make("203.0.113.7", "198.51.100.0/24"))!;

        Assert.Equal(["203.0.113.7", "198.51.100.0/24"], info.AllowedIps);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795564800), info.IssuedAt);
        Assert.True(info.Allows("203.0.113.7"));
        Assert.True(info.Allows("198.51.100.42"));
        Assert.False(info.Allows("203.0.113.8"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("eyJhbGciOi.bad!!.sig-but-long-enough-to-look-like-a-key-xxxxxxxx")]
    public void Garbage_is_not_a_key(string? key) => Assert.Null(ApiKeyInfo.TryParse(key));

    [Theory]
    [InlineData("10.0.0.0/8", "10.20.30.40", true)]
    [InlineData("10.0.0.0/8", "11.0.0.1", false)]
    [InlineData("192.168.1.128/25", "192.168.1.200", true)]
    [InlineData("192.168.1.128/25", "192.168.1.100", false)]
    [InlineData("203.0.113.7", "203.0.113.7", true)]
    [InlineData("0.0.0.0/0", "8.8.8.8", true)]
    [InlineData("203.0.113.7", "::1", false)]
    public void Cidr(string cidr, string ip, bool expected) => Assert.Equal(expected, ApiKeyInfo.CidrContains(cidr, ip));

    [Theory]
    [InlineData("\"abc.def\"", "abc.def")]
    [InlineData("Bearer abc", "abc")]
    [InlineData(" a b\tc\n", "abc")]
    public void Normalize(string raw, string expected) => Assert.Equal(expected, ApiKeyStore.Normalize(raw));

    [Fact]
    public async Task Public_ip_falls_back_to_the_next_service()
    {
        var http = new FakeHttpHandler()
            .On(PublicIpService.Endpoints[0], _ => throw new HttpRequestException("down"))
            .OnJson(PublicIpService.Endpoints[1], "<html>oops</html>")
            .On(PublicIpService.Endpoints[2], _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("203.0.113.7\n") });

        Assert.Equal("203.0.113.7", await new PublicIpService(new HttpClient(http)).GetAsync());
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Public_ip_gives_up_quietly()
    {
        var http = new FakeHttpHandler();
        foreach (var url in PublicIpService.Endpoints)
        {
            http.On(url, _ => throw new HttpRequestException("offline"));
        }

        Assert.Null(await new PublicIpService(new HttpClient(http)).GetAsync());
    }
}
