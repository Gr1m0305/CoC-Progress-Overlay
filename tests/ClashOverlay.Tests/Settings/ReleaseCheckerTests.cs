using System.Net;
using ClashOverlay.Core.Updates;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Settings;

public sealed class ReleaseCheckerTests
{
    private const string Repo = "example-owner/example-repo";
    private const string Latest = "https://api.github.com/repos/example-owner/example-repo/releases/latest";

    private static string Release(string tag, bool draft = false, bool prerelease = false, string? url = null) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Clash Tracker {{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "{{url ?? $"https://github.com/{Repo}/releases/tag/{tag}"}}",
          "published_at": "2026-12-01T10:00:00Z",
          "assets": []
        }
        """;

    private static (ReleaseChecker Checker, FakeHttpHandler Handler) Make(Action<FakeHttpHandler> setup)
    {
        var handler = new FakeHttpHandler();
        setup(handler);
        return (new ReleaseChecker(new HttpClient(handler), Repo), handler);
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V10.0.1", 10, 0, 1)]
    [InlineData("0.0.0-dev", 0, 0, 0)]
    [InlineData("1.4.0-beta.2", 1, 4, 0)]
    [InlineData(" v2.0.0 ", 2, 0, 0)]
    public void Versions_parse(string text, int major, int minor, int patch) =>
        Assert.Equal(new Version(major, minor, patch), ReleaseChecker.ParseVersion(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3 extra")]
    [InlineData("99999999999.0.0")]
    public void Bad_versions_are_null(string? text) => Assert.Null(ReleaseChecker.ParseVersion(text));

    [Theory]
    [InlineData("Gr1m0305/CoC-Progress-Overlay", true)]
    [InlineData("owner/repo.name_2", true)]
    [InlineData("", false)]
    [InlineData("owner", false)]
    [InlineData("owner/repo/extra", false)]
    [InlineData("owner/../x?y", false)]
    [InlineData("owner name/repo", false)]
    public void Repository_names_are_checked(string repo, bool ok) => Assert.Equal(ok, ReleaseChecker.IsValidRepository(repo));

    [Fact]
    public async Task Newer_release_is_reported()
    {
        var (checker, handler) = Make(h => h.OnJson(Latest, Release("v1.2.0")));
        var result = await checker.CheckAsync("1.0.0");

        Assert.Equal(ReleaseCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal(new Version(1, 2, 0), result.Latest!.Version);
        Assert.Equal("1.2.0", result.Latest.VersionText);
        Assert.Equal($"https://github.com/{Repo}/releases/tag/v1.2.0", result.Latest.Url);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero), result.Latest.PublishedAt);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
    }

    [Theory]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("v1.0.0", "1.1.0")]
    [InlineData("v0.9.9", "1.0.0")]
    public async Task Same_or_older_release_is_up_to_date(string tag, string current)
    {
        var (checker, _) = Make(h => h.OnJson(Latest, Release(tag)));
        Assert.Equal(ReleaseCheckOutcome.UpToDate, (await checker.CheckAsync(current)).Outcome);
    }

    [Fact]
    public async Task Dev_build_sees_any_release_as_newer()
    {
        var (checker, _) = Make(h => h.OnJson(Latest, Release("v1.0.0")));
        Assert.Equal(ReleaseCheckOutcome.UpdateAvailable, (await checker.CheckAsync("0.0.0-dev")).Outcome);
    }

    [Fact]
    public async Task No_release_yet_is_404()
    {
        var (checker, _) = Make(_ => { });
        var result = await checker.CheckAsync("1.0.0");
        Assert.Equal(ReleaseCheckOutcome.NoRelease, result.Outcome);
        Assert.Null(result.Latest);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Drafts_and_prereleases_are_ignored(bool draft, bool prerelease)
    {
        var (checker, _) = Make(h => h.OnJson(Latest, Release("v9.0.0", draft, prerelease)));
        Assert.Equal(ReleaseCheckOutcome.NoRelease, (await checker.CheckAsync("1.0.0")).Outcome);
    }

    [Fact]
    public async Task Tag_that_is_not_a_version_is_ignored()
    {
        var (checker, _) = Make(h => h.OnJson(Latest, Release("nightly")));
        Assert.Equal(ReleaseCheckOutcome.NoRelease, (await checker.CheckAsync("1.0.0")).Outcome);
    }

    [Fact]
    public async Task Link_off_github_falls_back_to_the_releases_page()
    {
        var (checker, _) = Make(h => h.OnJson(Latest, Release("v2.0.0", url: "https://example.invalid/download.exe")));
        var result = await checker.CheckAsync("1.0.0");
        Assert.Equal($"https://github.com/{Repo}/releases", result.Latest!.Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Errors_fail_without_throwing(HttpStatusCode status)
    {
        var (checker, _) = Make(h => h.OnJson(Latest, "{\"message\":\"nope\"}", status));
        var result = await checker.CheckAsync("1.0.0");
        Assert.Equal(ReleaseCheckOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task Broken_json_fails_without_throwing()
    {
        var (checker, _) = Make(h => h.OnJson(Latest, "{not json"));
        Assert.Equal(ReleaseCheckOutcome.Failed, (await checker.CheckAsync("1.0.0")).Outcome);
    }

    [Fact]
    public async Task Network_error_fails_without_throwing()
    {
        var (checker, _) = Make(h => h.On(Latest, _ => throw new HttpRequestException("offline")));
        var result = await checker.CheckAsync("1.0.0");
        Assert.Equal(ReleaseCheckOutcome.Failed, result.Outcome);
        Assert.Equal("GitHub could not be reached.", result.Error);
    }

    [Fact]
    public void Checks_are_due_once_a_day()
    {
        var now = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.True(ReleaseChecker.IsDue(null, now));
        Assert.False(ReleaseChecker.IsDue(now.AddHours(-23), now));
        Assert.True(ReleaseChecker.IsDue(now.AddHours(-24), now));
        Assert.True(ReleaseChecker.IsDue(now.AddDays(-30), now));
        Assert.False(ReleaseChecker.IsDue(now.AddMinutes(30), now));
        Assert.True(ReleaseChecker.IsDue(now.AddDays(2), now));
    }
}
