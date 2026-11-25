using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ClashOverlay.AssetFetch;
using ClashOverlay.Core.Assets;
using ClashOverlay.Tests.Support;
using SkiaSharp;

namespace ClashOverlay.Tests.Assets;

public sealed class AssetFetcherTests : IDisposable
{
    private const string Cdn = AssetPaths.CdnBase;
    private readonly TempDir _dir = new();
    private readonly FakeHttpHandler _http = new();

    public void Dispose() => _dir.Dispose();

    private static byte[] Webp(int w, int h, SKColor color)
    {
        using var bitmap = new SKBitmap(w, h);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 90);
        return data.ToArray();
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static string Manifest(params (string Path, byte[] Bytes)[] files) => JsonSerializer.Serialize(new
    {
        version = 2,
        assets = files.GroupBy(f => f.Path.Split('/')[0]).ToDictionary(g => g.Key, g => g.Select(f => new { path = f.Path, sha = Sha(f.Bytes), animated = false })),
    });

    private AssetFetcher Fetcher() => new(new HttpClient(_http), _dir.Path) { Parallelism = 2 };

    private string Local(string remote) => Path.Combine(_dir.Path, AssetPaths.LocalRelative(remote));

    [Fact]
    public async Task Downloads_converts_and_then_skips_unchanged_files()
    {
        var cannon = Webp(64, 64, SKColors.Red);
        var spell = Webp(32, 32, SKColors.Purple);
        var manifest = Manifest(("buildings/home-village/cannon/level_1.webp", cannon), ("spells/rage_spell.webp", spell));
        _http.OnJson(AssetPaths.ManifestUrl, manifest)
            .OnBytes(Cdn + "buildings/home-village/cannon/level_1.webp", cannon)
            .OnBytes(Cdn + "spells/rage_spell.webp", spell);
        string[] wanted = ["buildings/home-village/cannon/level_1.webp", "spells/rage_spell.webp", "spells/not_there.webp"];

        var first = await Fetcher().RunAsync(wanted);

        Assert.Equal(2, first.Downloaded);
        Assert.Equal(1, first.Missing);
        Assert.Equal(["spells/not_there.webp"], first.MissingPaths);
        Assert.True(first.UsedManifest);
        Assert.True(ImageConverter.IsPng(await File.ReadAllBytesAsync(Local("spells/rage_spell.webp"))));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "manifest.json")));

        var second = await Fetcher().RunAsync(wanted);
        Assert.Equal(0, second.Downloaded);
        Assert.Equal(2, second.UpToDate);
        Assert.Equal(2, _http.Count("rage_spell") + _http.Count("cannon")); // only the first run downloaded
    }

    [Fact]
    public async Task A_changed_sha_downloads_again()
    {
        var v1 = Webp(16, 16, SKColors.Red);
        var v2 = Webp(16, 16, SKColors.Blue);
        const string path = "troops/barbarian/icon.webp";
        var current = v1;
        _http.On(AssetPaths.ManifestUrl, _ => FakeHttpHandler.Json(Manifest((path, current))))
            .On(Cdn + path, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(current) });

        await Fetcher().RunAsync([path]);
        current = v2;
        var again = await Fetcher().RunAsync([path]);

        Assert.Equal(1, again.Downloaded);
        using var png = SKBitmap.Decode(Local(path));
        Assert.Equal(SKColors.Blue, png.GetPixel(8, 8));
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_sha_is_not_saved()
    {
        const string path = "spells/rage_spell.webp";
        _http.OnJson(AssetPaths.ManifestUrl, Manifest((path, Webp(8, 8, SKColors.Red))))
            .OnBytes(Cdn + path, Webp(8, 8, SKColors.Green));

        var result = await Fetcher().RunAsync([path]);

        Assert.Equal(1, result.Failed);
        Assert.False(File.Exists(Local(path)));
    }

    [Fact]
    public async Task Without_the_manifest_it_compares_last_modified()
    {
        var bytes = Webp(16, 16, SKColors.Gold);
        var modified = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        _http.On(AssetPaths.ManifestUrl, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .On(Cdn + "resources/gold.webp", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                r.Content.Headers.LastModified = modified;
                return r;
            })
            .On(Cdn + "resources/missing.webp", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var first = await Fetcher().RunAsync(["resources/gold.webp", "resources/missing.webp"]);
        var second = await Fetcher().RunAsync(["resources/gold.webp"]);

        Assert.False(first.UsedManifest);
        Assert.Equal(1, first.Downloaded);
        Assert.Equal(1, first.Missing);
        Assert.Equal(1, second.UpToDate);
        Assert.Equal(1, _http.Requests.Count(r => r.Method == HttpMethod.Get && r.RequestUri!.ToString().EndsWith("gold.webp")));
    }

    [Fact]
    public async Task Large_images_are_scaled_down()
    {
        const string path = "heroes/barbarian_king/portrait.webp";
        var big = Webp(1024, 512, SKColors.Orange);
        _http.OnJson(AssetPaths.ManifestUrl, Manifest((path, big))).OnBytes(Cdn + path, big);

        await Fetcher().RunAsync([path]);

        using var png = SKBitmap.Decode(Local(path));
        Assert.Equal(ImageConverter.MaxSize, png.Width);
        Assert.Equal(ImageConverter.MaxSize / 2, png.Height);
    }

    [Fact]
    public async Task Broken_image_bytes_fail_cleanly()
    {
        const string path = "spells/broken.webp";
        byte[] junk = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        _http.OnJson(AssetPaths.ManifestUrl, Manifest((path, junk))).OnBytes(Cdn + path, junk);

        var result = await Fetcher().RunAsync([path]);

        Assert.Equal(1, result.Failed);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Extra_folders_come_from_the_manifest()
    {
        var a = Webp(8, 8, SKColors.Red);
        var b = Webp(8, 8, SKColors.Blue);
        _http.OnJson(AssetPaths.ManifestUrl, Manifest(("player_labels/clan_wars.webp", a), ("player_labels/farming.webp", b)))
            .OnBytes(Cdn + "player_labels/clan_wars.webp", a)
            .OnBytes(Cdn + "player_labels/farming.webp", b);

        var result = await Fetcher().RunAsync([], ["player_labels"]);

        Assert.Equal(2, result.Downloaded);
        Assert.True(File.Exists(Local("player_labels/farming.webp")));
    }

    [Fact]
    public void Manifest_without_files_is_rejected() =>
        Assert.Throws<JsonException>(() => CdnManifest.Parse("{\"version\":2,\"assets\":{}}"));
}
