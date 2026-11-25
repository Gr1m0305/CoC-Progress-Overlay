using ClashOverlay.Api;
using ClashOverlay.Api.Models;
using ClashOverlay.Core;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Export;
using ClashOverlay.Core.Settings;
using ClashOverlay.Core.State;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>Everything the overlay shows for one account, computed off the UI thread.</summary>
internal sealed class AccountData
{
    public required AccountSettings Account { get; init; }

    public required DateTimeOffset LoadedAt { get; init; }

    public VillageExport? Export { get; init; }

    public string? ExportProblem { get; init; }

    public ApiResult<PlayerDto>? Player { get; init; }

    public AccountState? State { get; init; }

    public VillageReport? Home { get; init; }

    public VillageReport? Builder { get; init; }

    /// <summary>Hand edits in effect, by item id.</summary>
    public IReadOnlyDictionary<int, int> EditCounts { get; init; } = new Dictionary<int, int>();

    public string Name => Player?.Value?.Name ?? Account.Name ?? Account.Nickname ?? Account.Tag;

    public VillageReport? Report(Village v) => v == Village.Home ? Home : Builder;

    /// <summary>Town Hall level in the export itself, before the API's level is applied.</summary>
    public int ExportHall { get; init; }
}

/// <summary>A war to show: the regular war, or the CWL round in progress.</summary>
internal sealed record WarInfo(WarDto War, bool IsCwl, int Round, int Rounds);

internal sealed class ProfileData
{
    public ApiResult<ClanDto>? Clan { get; init; }

    public WarInfo? War { get; init; }

    public string? WarProblem { get; init; }

    public RaidSeasonDto? Raid { get; init; }

    public GoldPassDto? GoldPass { get; init; }
}

/// <summary>Builds <see cref="AccountData"/>: export + API player + hand edits → merged state → reports.</summary>
internal sealed class AccountLoader(GameDataService data, IClashApiClient api, AccountStore store, Func<AppSettings> settings, TimeProvider time, ILogger logger)
{
    public async Task<AccountData> LoadAsync(AccountSettings account, bool force, CancellationToken ct)
    {
        var tag = account.Tag;
        var player = await api.GetPlayerAsync(tag, force, ct).ConfigureAwait(false);
        var s = settings();
        return await Task.Run(() => Build(account, player, s), ct).ConfigureAwait(false);
    }

    private AccountData Build(AccountSettings account, ApiResult<PlayerDto> player, AppSettings s)
    {
        var now = time.GetUtcNow();
        var export = store.LoadExport(account.Tag, out var exportProblem);
        var exportHall = export?.In(ExportSection.Buildings).Where(e => e.Id == GameData.TownHallId).Select(e => e.Level).DefaultIfEmpty(0).Max() ?? 0;
        var game = data.Data;
        if (game is null)
        {
            return new AccountData { Account = account, LoadedAt = now, Export = export, ExportProblem = exportProblem, Player = player, ExportHall = exportHall };
        }

        ApiPlayerSnapshot? snapshot = null;
        if (player.Value is { } p)
        {
            snapshot = ApiPlayerMapper.ToSnapshot(p, player.FetchedAt ?? now);
            var unknown = ApiPlayerMapper.UnknownNames(game, snapshot);
            if (unknown.Count > 0)
            {
                logger.LogInformation("API names not in the game data for {Tag}: {Names}", account.Tag, string.Join(", ", unknown));
            }
        }

        var state = VillageStateBuilder.Build(game, export, snapshot, now, logger);
        var edits = store.LoadEdits(account.Tag);
        var editCounts = new Dictionary<int, int>();
        if (edits.Count > 0)
        {
            var applied = LevelEdits.Apply(game, state, edits);
            state = applied.State;
            foreach (var e in applied.Applied)
            {
                editCounts[e.ItemId] = editCounts.GetValueOrDefault(e.ItemId) + 1;
            }

            if (applied.Obsolete.Count > 0)
            {
                logger.LogInformation("{Count} hand edit(s) for {Tag} are covered by newer data and were dropped", applied.Obsolete.Count, account.Tag);
                store.SaveEdits(account.Tag, applied.Applied);
            }
        }

        var options = new AnalyzerOptions(s.ProgressMode, s.BuildersOverride) { Boosts = account.Boosts };
        var home = state.Home.HallLevel > 0 ? VillageAnalyzer.Analyze(game, state.Home, options) : null;
        var bb = state.BuilderBase.HallLevel > 0 ? VillageAnalyzer.Analyze(game, state.BuilderBase, options with { BuildersOverride = null }) : null;
        return new AccountData
        {
            Account = account,
            LoadedAt = now,
            Export = export,
            ExportProblem = exportProblem,
            Player = player,
            State = state,
            Home = home,
            Builder = bb,
            EditCounts = editCounts,
            ExportHall = exportHall,
        };
    }

    public async Task<ProfileData> LoadProfileAsync(PlayerDto player, bool force, CancellationToken ct)
    {
        var goldPass = api.GetGoldPassAsync(force, ct);
        if (player.Clan is not { Tag.Length: > 0 } clanRef)
        {
            return new ProfileData { GoldPass = (await goldPass.ConfigureAwait(false)).Value };
        }

        var clan = api.GetClanAsync(clanRef.Tag, force, ct);
        var raids = api.GetRaidSeasonsAsync(clanRef.Tag, force, ct);
        var (war, warProblem) = await FindWarAsync(clanRef.Tag, force, ct).ConfigureAwait(false);
        return new ProfileData
        {
            Clan = await clan.ConfigureAwait(false),
            War = war,
            WarProblem = warProblem,
            Raid = (await raids.ConfigureAwait(false)).Value?.Items.FirstOrDefault(),
            GoldPass = (await goldPass.ConfigureAwait(false)).Value,
        };
    }

    /// <summary>The regular war if there is one; otherwise this clan's CWL war in the latest round that has started.</summary>
    private async Task<(WarInfo? War, string? Problem)> FindWarAsync(string clanTag, bool force, CancellationToken ct)
    {
        var current = await api.GetCurrentWarAsync(clanTag, force, ct).ConfigureAwait(false);
        if (current.Value is { State: not "notInWar" } w && w.Clan is not null)
        {
            return (new WarInfo(w, false, 0, 0), null);
        }

        string? problem = current.Error?.Kind == ApiErrorKind.PrivateWarLog ? "The clan's war log is private." : null;
        var group = await api.GetLeagueGroupAsync(clanTag, force, ct).ConfigureAwait(false);
        if (group.Value is not { } g || g.Rounds.Count == 0 || g.State is "notInWar" or "")
        {
            return (null, problem);
        }

        var ours = PlayerTag.Normalize(clanTag);
        WarInfo? fallback = null;
        for (var r = g.Rounds.Count - 1; r >= 0; r--)
        {
            var tags = g.Rounds[r].WarTags.Where(t => t != "#0").ToList();
            if (tags.Count == 0)
            {
                continue;
            }

            foreach (var tag in tags)
            {
                var res = await api.GetCwlWarAsync(tag, force: false, ct).ConfigureAwait(false);
                if (res.Value is not { } war || war.Clan is null || war.Opponent is null)
                {
                    continue;
                }

                WarDto? mine = PlayerTag.Normalize(war.Clan.Tag) == ours ? war
                    : PlayerTag.Normalize(war.Opponent.Tag) == ours ? Swap(war) : null;
                if (mine is null)
                {
                    continue;
                }

                var info = new WarInfo(mine, true, r + 1, g.Rounds.Count);
                if (mine.State == "inWar")
                {
                    return (info, null);
                }

                fallback ??= info; // preparation, or the last war that ended
                if (mine.State == "warEnded")
                {
                    return (fallback, null);
                }

                break;
            }
        }

        return (fallback, problem);
    }

    private static WarDto Swap(WarDto w) => new()
    {
        State = w.State,
        TeamSize = w.TeamSize,
        AttacksPerMember = w.AttacksPerMember,
        BattleModifier = w.BattleModifier,
        PreparationStartTime = w.PreparationStartTime,
        StartTime = w.StartTime,
        EndTime = w.EndTime,
        WarStartTime = w.WarStartTime,
        Clan = w.Opponent,
        Opponent = w.Clan,
    };
}
