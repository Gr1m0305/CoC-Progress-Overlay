using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClashOverlay.Core.Data;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Data;

[Collection(GameDataCollection.Name)]
public sealed class StaticDataUpdaterTests(GameDataFixture fixture) : IDisposable
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClashOverlayTests", Guid.NewGuid().ToString("N"));

    private string DataDir => Path.Combine(_root, "data");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private byte[] Real(string file) => File.ReadAllBytes(Path.Combine(fixture.DataDir, file + ".json"));

    private static string Commits(string sha) =>
        $"[{{\"sha\":\"{sha}\",\"commit\":{{\"committer\":{{\"date\":\"2026-11-23T15:13:10Z\"}}}}}}]";

    private FakeHttpHandler GitHub(string sha, Func<string, byte[]>? file = null)
    {
        var handler = new FakeHttpHandler().OnJson(StaticDataUpdater.GitHubCommitsUrl, Commits(sha));
        foreach (var f in ClashKingDataSource.RequiredFiles)
        {
            handler.OnBytes(string.Format(StaticDataUpdater.GitHubRawFormat, sha, f), (file ?? Real)(f));
        }

        return handler;
    }

    [Fact]
    public async Task Downloads_from_GitHub_and_writes_a_manifest()
    {
        var handler = GitHub(Sha);
        var updater = new StaticDataUpdater(new HttpClient(handler), DataDir);

        var result = await updater.UpdateAsync();

        Assert.Equal(DataUpdateOutcome.Updated, result.Outcome);
        var manifest = StaticDataManifest.TryRead(DataDir)!;
        Assert.Equal(Sha, manifest.Commit);
        Assert.Equal("github", manifest.Origin);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 15, 13, 10, TimeSpan.Zero), manifest.CommitDate);
        Assert.Equal(ClashKingDataSource.RequiredFiles.Count, manifest.Files.Count);
        var buildings = manifest.Files.Single(f => f.Name == "buildings");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Real("buildings"))).ToLowerInvariant(), buildings.Sha256);
        Assert.Equal(19, new ClashKingDataSource(DataDir).Load().MaxTownHall);
        Assert.False(Directory.Exists(Path.Combine(_root, "data.staging")));
    }

    [Fact]
    public async Task Same_commit_is_up_to_date_without_downloading()
    {
        var handler = GitHub(Sha);
        var updater = new StaticDataUpdater(new HttpClient(handler), DataDir);
        await updater.UpdateAsync();
        var downloads = handler.Count("raw.githubusercontent.com");

        var again = await updater.UpdateAsync();

        Assert.Equal(DataUpdateOutcome.UpToDate, again.Outcome);
        Assert.Equal(downloads, handler.Count("raw.githubusercontent.com"));
    }

    [Fact]
    public async Task Falls_back_to_the_CDN_when_GitHub_rate_limits()
    {
        var handler = new FakeHttpHandler().OnJson(StaticDataUpdater.GitHubCommitsUrl, "{\"message\":\"API rate limit exceeded\"}", HttpStatusCode.Forbidden);
        var stats = new StringBuilder();
        foreach (var f in ClashKingDataSource.RequiredFiles)
        {
            var bytes = Real(f);
            handler.OnBytes(string.Format(StaticDataUpdater.CdnFormat, f), bytes);
            stats.Append($"{{\"path\":\"static_data/{f}.json\",\"sha\":\"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}\"}},");
        }

        handler.OnJson(StaticDataUpdater.CdnManifestUrl, $"{{\"version\":2,\"assets\":{{}},\"data\":{{\"stats\":[{stats.ToString().TrimEnd(',')}]}}}}");
        var updater = new StaticDataUpdater(new HttpClient(handler), DataDir);

        var result = await updater.UpdateAsync();

        Assert.Equal(DataUpdateOutcome.Updated, result.Outcome);
        Assert.Equal("cdn", StaticDataManifest.TryRead(DataDir)!.Origin);
        Assert.Null(StaticDataManifest.TryRead(DataDir)!.Commit);

        var again = await updater.UpdateAsync();
        Assert.Equal(DataUpdateOutcome.UpToDate, again.Outcome);
    }

    [Fact]
    public async Task A_bad_download_keeps_the_existing_data()
    {
        await new StaticDataUpdater(new HttpClient(GitHub(Sha)), DataDir).UpdateAsync();

        // The next commit's buildings.json has no Town Hall.
        var broken = GitHub("fedcba9876543210fedcba9876543210fedcba98",
            f => f == "buildings" ? Encoding.UTF8.GetBytes("{\"items\": []}") : Real(f));
        var result = await new StaticDataUpdater(new HttpClient(broken), DataDir).UpdateAsync();

        Assert.Equal(DataUpdateOutcome.Failed, result.Outcome);
        Assert.Contains("kept", result.Message);
        Assert.Equal(Sha, StaticDataManifest.TryRead(DataDir)!.Commit);
        Assert.Equal(19, new ClashKingDataSource(DataDir).Load().MaxTownHall);
    }

    [Fact]
    public async Task Offline_with_no_data_fails_with_a_message()
    {
        var handler = new FakeHttpHandler().On("https://", _ => throw new HttpRequestException("No such host is known."));
        var result = await new StaticDataUpdater(new HttpClient(handler), DataDir).UpdateAsync();

        Assert.Equal(DataUpdateOutcome.Failed, result.Outcome);
        Assert.False(new ClashKingDataSource(DataDir).IsAvailable);
    }
}
