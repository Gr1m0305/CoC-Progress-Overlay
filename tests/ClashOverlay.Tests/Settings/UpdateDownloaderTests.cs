using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClashOverlay.Core.Updates;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Settings;

public sealed class UpdateDownloaderTests : IDisposable
{
    private const string Base = "https://github.com/example-owner/example-repo/releases/download/v1.1.0/";
    private const string SetupName = "ClashOverlay-Setup-1.1.0.exe";

    private static readonly byte[] Installer = Encoding.ASCII.GetBytes("pretend installer bytes for the test");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Installer)).ToLowerInvariant();
    private static readonly string OtherHash = new('a', 64);

    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Folder => _dir.File("updates");

    private static ReleaseInfo Release(string? digest, bool sums = true) => new(
        new Version(1, 1, 0),
        "v1.1.0",
        "https://github.com/example-owner/example-repo/releases/tag/v1.1.0",
        null,
        new ReleaseAsset(SetupName, Base + SetupName, Installer.Length, digest),
        sums ? new ReleaseAsset("SHA256SUMS.txt", Base + "SHA256SUMS.txt", 200, null) : null);

    private (UpdateDownloader Downloader, FakeHttpHandler Http) Make(string? sumsText = null)
    {
        var http = new FakeHttpHandler().OnBytes(Base + SetupName, Installer);
        if (sumsText is not null)
        {
            http.OnBytes(Base + "SHA256SUMS.txt", Encoding.ASCII.GetBytes(sumsText));
        }

        return (new UpdateDownloader(new HttpClient(http), Folder), http);
    }

    private static string Sums(string hash) => $"{OtherHash}  ClashOverlay-1.1.0-portable.zip\n{hash}  {SetupName}\n";

    [Fact]
    public async Task A_matching_installer_is_kept_and_older_ones_removed()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "ClashOverlay-Setup-1.0.5.exe"), "old");
        var (downloader, _) = Make(Sums(Hash));

        var result = await downloader.DownloadAsync(Release(Hash));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(Path.Combine(Folder, SetupName), result.Path);
        Assert.Equal(Installer, File.ReadAllBytes(result.Path!));
        Assert.Single(Directory.GetFiles(Folder));
    }

    [Fact]
    public async Task The_checksum_list_alone_is_enough()
    {
        var (downloader, _) = Make(Sums(Hash));
        Assert.True((await downloader.DownloadAsync(Release(null))).Ok);
    }

    [Fact]
    public async Task The_digest_alone_is_enough()
    {
        var (downloader, _) = Make();
        Assert.True((await downloader.DownloadAsync(Release(Hash, sums: false))).Ok);
    }

    [Fact]
    public async Task A_file_that_does_not_match_is_deleted()
    {
        var (downloader, _) = Make(Sums(OtherHash));
        var result = await downloader.DownloadAsync(Release(OtherHash));
        Assert.False(result.Ok);
        Assert.Contains("did not match", result.Error);
        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public async Task Disagreeing_checksums_stop_before_downloading()
    {
        var (downloader, http) = Make(Sums(OtherHash));
        var result = await downloader.DownloadAsync(Release(Hash));
        Assert.False(result.Ok);
        Assert.Contains("do not agree", result.Error);
        Assert.Equal(0, http.Count(SetupName));
    }

    [Fact]
    public async Task No_checksum_means_no_install()
    {
        var (downloader, http) = Make();
        var result = await downloader.DownloadAsync(Release(null, sums: false));
        Assert.False(result.Ok);
        Assert.Equal(0, http.Count(SetupName));
    }

    [Fact]
    public async Task A_checksum_list_without_the_installer_is_refused()
    {
        var (downloader, _) = Make($"{OtherHash}  something-else.zip\n");
        var result = await downloader.DownloadAsync(Release(Hash));
        Assert.False(result.Ok);
        Assert.Contains("does not include", result.Error);
    }

    [Fact]
    public async Task A_release_without_an_installer_is_refused()
    {
        var (downloader, _) = Make();
        var release = Release(Hash) with { Setup = null };
        Assert.False((await downloader.DownloadAsync(release)).Ok);
    }

    [Fact]
    public async Task An_installer_already_downloaded_is_not_fetched_again()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Path.Combine(Folder, SetupName), Installer);
        var (downloader, http) = Make();

        var result = await downloader.DownloadAsync(Release(Hash, sums: false));

        Assert.True(result.Ok);
        Assert.Equal(0, http.Count(SetupName));
    }

    [Fact]
    public async Task A_failed_download_says_so()
    {
        var http = new FakeHttpHandler().On(Base + SetupName, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var downloader = new UpdateDownloader(new HttpClient(http), Folder);
        var result = await downloader.DownloadAsync(Release(Hash, sums: false));
        Assert.False(result.Ok);
        Assert.Contains("could not be reached", result.Error);
    }

    [Fact]
    public void Checksum_lists_parse()
    {
        var map = UpdateDownloader.ParseChecksums($"{Hash.ToUpperInvariant()}  {SetupName}\r\n{OtherHash} *portable.zip\nnot a line\n{new string('z', 64)}  bad.txt\n");
        Assert.Equal(Hash, map[SetupName]);
        Assert.Equal(OtherHash, map["portable.zip"]);
        Assert.Equal(2, map.Count);
    }
}
