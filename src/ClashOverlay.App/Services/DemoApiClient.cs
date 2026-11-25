using System.Reflection;
using System.Text.Json;
using ClashOverlay.Api;
using ClashOverlay.Api.Models;
using ClashOverlay.Core;

namespace ClashOverlay.App.Services;

/// <summary>The bundled demo data: anonymised fixtures compiled into the exe (see <c>tests/fixtures</c>).</summary>
internal static class DemoData
{
    public const string MainTag = "#2PQ8LV9CY";
    public const string AltTag = "#9LJRG2UQ";

    /// <summary>One hour after the demo export was taken, so its running upgrades show sensible timers.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 25, 1, 0, 0, TimeSpan.Zero);

    public static string Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Demo." + name)
            ?? throw new InvalidOperationException($"Demo resource {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyDictionary<string, string> Exports() => new Dictionary<string, string>
    {
        [MainTag] = Read("demo_th19.json"),
        [AltTag] = Read("rushed_th13.json"),
    };
}

/// <summary>A clock that starts at <see cref="DemoData.Now"/> and then runs normally, so countdowns tick.</summary>
internal sealed class DemoClock : TimeProvider
{
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => DemoData.Now + (DateTimeOffset.UtcNow - _started);
}

/// <summary>Answers every call from the bundled fixtures. No network, no key.</summary>
internal sealed class DemoApiClient(TimeProvider time) : IClashApiClient
{
    private readonly Lazy<PlayerDto> _main = new(() => ApiJson.Deserialize<PlayerDto>(DemoData.Read("demo_player.json"))!);
    private readonly Lazy<Dictionary<string, JsonElement>> _cwl = new(() => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(DemoData.Read("demo_cwlwars.json"))!);

    public event EventHandler<ClashApiException>? KeyProblem { add { } remove { } }

    public DateTimeOffset? PausedUntil => null;

    /// <summary>
    /// For the screenshot run: answer the player call the way the real client does when the API fails and an older
    /// copy is cached (the copy plus the error).
    /// </summary>
    public ClashApiException? Fault { get; set; }

    private ApiResult<T> Ok<T>(T value, double minutesAgo = 2)
        where T : class => Fault is { } f
            ? new() { Value = value, FetchedAt = time.GetUtcNow().AddMinutes(-minutesAgo - 95), FromCache = true, IsStale = true, Error = f }
            : new() { Value = value, FetchedAt = time.GetUtcNow().AddMinutes(-minutesAgo), FromCache = true };

    private static ApiResult<T> Missing<T>()
        where T : class => new() { Error = new ClashApiException(ApiErrorKind.NotFound, "Not in the demo data.", 404) };

    private ApiResult<T> File<T>(string name)
        where T : class => Ok(ApiJson.Deserialize<T>(DemoData.Read(name))!);

    public Task<ApiResult<PlayerDto>> GetPlayerAsync(string tag, bool force = false, CancellationToken ct = default) =>
        Task.FromResult(PlayerTag.Normalize(tag) switch
        {
            DemoData.MainTag => Ok(_main.Value),
            DemoData.AltTag => Ok(Alt(), 9),
            _ => Missing<PlayerDto>(),
        });

    /// <summary>A second, rushed account without a clan, to show the switcher and an old export.</summary>
    private static PlayerDto Alt() => new()
    {
        Tag = DemoData.AltTag,
        Name = "Demo Alt",
        TownHallLevel = 13,
        ExpLevel = 168,
        Trophies = 3120,
        BestTrophies = 3388,
        WarStars = 312,
        AttackWins = 41,
        DefenseWins = 3,
        BuilderHallLevel = 8,
        BuilderBaseTrophies = 3510,
        BestBuilderBaseTrophies = 3640,
        Donations = 320,
        DonationsReceived = 410,
        LeagueTier = new LeagueRef { Id = 105000024, Name = "Crystal League 24" },
        BuilderBaseLeague = new LeagueRef { Id = 44000029, Name = "Ruby League III" },
    };

    public Task<ApiResult<ClanDto>> GetClanAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        Task.FromResult(File<ClanDto>("demo_clan.json"));

    public Task<ApiResult<ItemList<ClanMemberDto>>> GetClanMembersAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        Task.FromResult(File<ItemList<ClanMemberDto>>("demo_members.json"));

    public Task<ApiResult<WarDto>> GetCurrentWarAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        Task.FromResult(File<WarDto>("demo_currentwar.json"));

    public Task<ApiResult<ItemList<WarLogEntryDto>>> GetWarLogAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        Task.FromResult(File<ItemList<WarLogEntryDto>>("demo_warlog.json"));

    public Task<ApiResult<LeagueGroupDto>> GetLeagueGroupAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        Task.FromResult(File<LeagueGroupDto>("demo_leaguegroup.json"));

    public Task<ApiResult<WarDto>> GetCwlWarAsync(string warTag, bool force = false, CancellationToken ct = default) =>
        Task.FromResult(_cwl.Value.TryGetValue(warTag, out var json) ? Ok(ApiJson.Deserialize<WarDto>(json.GetRawText())!) : Missing<WarDto>());

    public Task<ApiResult<ItemList<RaidSeasonDto>>> GetRaidSeasonsAsync(string clanTag, bool force = false, CancellationToken ct = default) =>
        Task.FromResult(File<ItemList<RaidSeasonDto>>("demo_raids.json"));

    public Task<ApiResult<GoldPassDto>> GetGoldPassAsync(bool force = false, CancellationToken ct = default) =>
        Task.FromResult(File<GoldPassDto>("demo_goldpass.json"));

    public Task<ClashApiException?> CheckKeyAsync(string key, CancellationToken ct = default) => Task.FromResult<ClashApiException?>(null);

    public void ResetBackoff()
    {
    }
}
