using System.Reflection;
using ClashOverlay.Core;
using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Data;

namespace ClashOverlay.AssetFetch;

/// <summary>
/// <c>AssetFetch [--data DIR] [--out DIR] [--force] [--update-data] [--list-missing]</c>: downloads the game data (with
/// <c>--update-data</c>) and the images into the app's folders. The tray's "Update data and images" runs the same code.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var dataDir = Arg(args, "--data") ?? AppPaths.Default.Data;
        var outDir = Arg(args, "--out") ?? AppPaths.Default.Assets;
        var force = args.Contains("--force");
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("AssetFetch [--data DIR] [--out DIR] [--force] [--update-data] [--list-missing]");
            Console.WriteLine("Downloads ClashKing static data and artwork at run time. Nothing is bundled or committed.");
            return 0;
        }

        try
        {
            if (args.Contains("--update-data") || !new ClashKingDataSource(dataDir).IsAvailable)
            {
                using var dataHttp = StaticDataUpdater.CreateHttpClient(version);
                var update = await new StaticDataUpdater(dataHttp, dataDir).UpdateAsync(force).ConfigureAwait(false);
                Console.WriteLine($"Game data: {update.Outcome} {update.Message}");
                if (update.Outcome == DataUpdateOutcome.Failed && !new ClashKingDataSource(dataDir).IsAvailable)
                {
                    return 2;
                }
            }

            var data = new ClashKingDataSource(dataDir).Load();
            var wanted = AssetPaths.Wanted(data);
            using var http = AssetFetcher.CreateHttpClient(version);
            var fetcher = new AssetFetcher(http, outDir);
            var last = -1;
            var progress = new Progress<AssetFetchProgress>(p =>
            {
                var pct = p.Done * 100 / Math.Max(1, p.Total);
                if (pct / 10 != last / 10)
                {
                    last = pct;
                    Console.WriteLine($"  {p.Done}/{p.Total} checked, {p.Downloaded} downloaded");
                }
            });

            var result = await fetcher.RunAsync(wanted, AssetFetcher.ExtraFolders, force, progress).ConfigureAwait(false);
            Console.WriteLine($"Images: {result.Summary}. Saved in {outDir}");
            if (args.Contains("--list-missing"))
            {
                foreach (var p in result.MissingPaths)
                {
                    Console.WriteLine("  missing: " + p);
                }
            }

            return result.Failed == 0 ? 0 : 1;
        }
        catch (GameDataException e)
        {
            Console.Error.WriteLine("Game data: " + e.Message);
            return 2;
        }
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
