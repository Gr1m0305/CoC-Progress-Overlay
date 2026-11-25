using ClashOverlay.Core;
using ClashOverlay.Core.Data;

namespace ClashOverlay.Tests.Support;

/// <summary>
/// The real, downloaded ClashKing data set, shared by the data-driven tests. Uses <c>CLASHOVERLAY_DATA_DIR</c>, else
/// the app's own data folder, else downloads into <c>%TEMP%\ClashOverlayTestData</c> (once). If none of that works the
/// tests fail: a broken data update must fail loudly.
/// </summary>
public sealed class GameDataFixture
{
    public GameDataFixture()
    {
        DataDir = Resolve();
        Data = new ClashKingDataSource(DataDir).Load();
    }

    public string DataDir { get; }

    public GameData Data { get; }

    private static string Resolve()
    {
        var env = Environment.GetEnvironmentVariable("CLASHOVERLAY_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env) && new ClashKingDataSource(env).IsAvailable)
        {
            return env;
        }

        if (new ClashKingDataSource(AppPaths.Default.Data).IsAvailable)
        {
            return AppPaths.Default.Data;
        }

        var temp = Path.Combine(Path.GetTempPath(), "ClashOverlayTestData", "data");
        if (new ClashKingDataSource(temp).IsAvailable)
        {
            return temp;
        }

        using var http = StaticDataUpdater.CreateHttpClient("tests");
        var result = new StaticDataUpdater(http, temp).UpdateAsync(force: true).GetAwaiter().GetResult();
        if (result.Outcome == DataUpdateOutcome.Failed)
        {
            throw new InvalidOperationException("Could not download the game data for the tests: " + result.Message);
        }

        return temp;
    }
}

[CollectionDefinition(Name)]
public sealed class GameDataCollection : ICollectionFixture<GameDataFixture>
{
    public const string Name = "Downloaded game data";
}

public static class Fixtures
{
    public static string Dir => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string Path(params string[] parts) => System.IO.Path.Combine([Dir, .. parts]);

    public static string Read(params string[] parts) => File.ReadAllText(Path(parts));
}
