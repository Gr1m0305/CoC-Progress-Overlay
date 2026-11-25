using ClashOverlay.AssetFetch;
using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Data;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Assets;

/// <summary>
/// The naming rules against ClashKing's live manifest (downloaded once into %TEMP%\ClashOverlayTestData and refreshed
/// weekly), so a renamed folder or a new item without art shows up here instead of as blank tiles.
/// </summary>
[Collection(GameDataCollection.Name)]
public class CdnCoverageTests(GameDataFixture fixture)
{
    private static readonly Lazy<CdnManifest> Manifest = new(Load);

    private static CdnManifest Load()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClashOverlayTestData", "cdn-manifest.json");
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7))
        {
            using var http = AssetFetcher.CreateHttpClient("tests");
            var json = http.GetStringAsync(AssetPaths.ManifestUrl).GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        return CdnManifest.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Nearly_every_wanted_image_exists()
    {
        var wanted = AssetPaths.Wanted(fixture.Data);
        var missing = wanted.Where(p => !Manifest.Value.ShaByPath.ContainsKey(p)).ToList();
        Assert.True(missing.Count <= wanted.Count / 50, "Missing on the CDN: " + string.Join(", ", missing.Take(30)));
    }

    [Fact]
    public void Every_counted_item_has_its_art()
    {
        var missing = new List<string>();
        foreach (var item in fixture.Data.Items.Where(i => i.CountsTowardsProgress && i.FirstHall <= fixture.Data.MaxHall(i.Village)))
        {
            var path = AssetPaths.ForItem(item, item.TopLevel);
            if (!Manifest.Value.ShaByPath.ContainsKey(path))
            {
                missing.Add(path);
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Leagues_and_resources()
    {
        var have = Manifest.Value.ShaByPath;
        Assert.All(fixture.Data.LeagueTiers, l => Assert.Contains(AssetPaths.LeagueTier(l.Name), have.Keys));
        Assert.All(fixture.Data.BuilderLeagues, l => Assert.Contains(AssetPaths.BuilderLeague(l.Name), have.Keys));
        Assert.All(fixture.Data.WarLeagues, l => Assert.Contains(AssetPaths.WarLeague(l.Name), have.Keys));
        Assert.All(Enum.GetValues<Resource>().Select(AssetPaths.ForResource).OfType<string>(), p => Assert.Contains(p, have.Keys));
        Assert.All(AssetPaths.UiIcons, p => Assert.Contains(p, have.Keys));
    }
}
