using System.Globalization;
using System.Windows.Media;
using ClashOverlay.Api.Models;
using ClashOverlay.App.Services;
using ClashOverlay.Core;
using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClashOverlay.App.ViewModels;

internal sealed record AchievementRow(string Name, int Stars, string Progress, double Fraction, string? Info)
{
    public bool Star1 => Stars >= 1;

    public bool Star2 => Stars >= 2;

    public bool Star3 => Stars >= 3;
}

internal sealed record LabelChip(ImageSource? Image, string Name);

/// <summary>
/// Profile & War (Main Village) or Profile (Builder Base): the player card, league and trophies, the clan, the current
/// war or CWL round with attacks left and a countdown, the latest raid weekend, the Gold Pass season and achievements.
/// </summary>
internal sealed partial class ProfileTab : TabViewModel
{
    private readonly TabContext _ctx;
    private readonly PlayerDto? _player;
    private DateTimeOffset? _warDeadline;
    private string _warDeadlineLabel = "";
    private DateTimeOffset? _raidEnd;
    private DateTimeOffset? _goldPassEnd;

    public ProfileTab(TabContext ctx)
    {
        _ctx = ctx;
        _player = ctx.Data.Player?.Value;
        var home = ctx.Village == Village.Home;
        var images = ctx.Images;
        Name = ctx.Data.Name;
        Tag = ctx.Data.Account.Tag;
        HallImage = ctx.Img(ctx.Game.Hall(ctx.Village), ctx.Hall, 160);
        HallText = $"{ctx.Village.HallName()} {ctx.Hall}";
        if (_player is not { } p)
        {
            HasPlayer = false;
            return;
        }

        HasPlayer = true;
        XpText = $"XP level {p.ExpLevel}";
        RoleText = p.Clan is null ? "No clan" : $"{Role(p.Role)} of {p.Clan.Name}";
        foreach (var l in p.Labels)
        {
            Labels.Add(new LabelChip(images.Remote(AssetPaths.PlayerLabel(l.Name), 48), l.Name));
        }

        if (home)
        {
            LeagueName = p.LeagueTier?.Name ?? "Unranked";
            LeagueImage = p.LeagueTier is { } lt ? images.Remote(AssetPaths.LeagueTier(lt.Name), 128) : null;
            Trophies = ctx.N(p.Trophies);
            BestTrophies = $"Best {ctx.N(p.BestTrophies)}";
            Stats.Add(new Stat("War stars", ctx.N(p.WarStars), Icon("icons/Icon_HV_Attack_Star.png")));
            Stats.Add(new Stat("Attack wins", ctx.N(p.AttackWins), Icon("icons/Icon_HV_Sword.png")));
            Stats.Add(new Stat("Defense wins", ctx.N(p.DefenseWins), Icon("icons/Icon_HV_Shield.png")));
            Stats.Add(new Stat("Donated", ctx.N(p.Donations)));
            Stats.Add(new Stat("Received", ctx.N(p.DonationsReceived)));
            Stats.Add(new Stat("Capital gold given", ctx.N(p.ClanCapitalContributions), Icon("icons/Icon_CC_Resource_Capital_Gold_small.png")));
            Stats.Add(new Stat("War preference", p.WarPreference == "in" ? "In" : p.WarPreference == "out" ? "Out" : "–"));
        }
        else
        {
            LeagueName = p.BuilderBaseLeague?.Name ?? "Unranked";
            LeagueImage = p.BuilderBaseLeague is { } bl ? images.Remote(AssetPaths.BuilderLeague(bl.Name), 128) : null;
            Trophies = ctx.N(p.BuilderBaseTrophies);
            BestTrophies = $"Best {ctx.N(p.BestBuilderBaseTrophies)}";
            Stats.Add(new Stat("Builder Hall", p.BuilderHallLevel.ToString(CultureInfo.InvariantCulture)));
            Stats.Add(new Stat("Trophies", ctx.N(p.BuilderBaseTrophies), Icon("icons/Icon_HV_Trophy.png")));
            Stats.Add(new Stat("Best", ctx.N(p.BestBuilderBaseTrophies), Icon("icons/Icon_HV_Trophy_Best.png")));
        }

        TrophyIcon = Icon("icons/Icon_HV_Trophy.png");
        var village = home ? "home" : "builderBase";
        var mine = p.Achievements.Where(a => string.Equals(a.Village, village, StringComparison.OrdinalIgnoreCase)).ToList();
        var stars = mine.Sum(a => a.Stars);
        AchievementText = mine.Count == 0 ? "No achievements reported" : $"{stars} of {mine.Count * 3} stars · {mine.Count(a => a.Stars >= 3)} of {mine.Count} complete";
        AchievementFraction = mine.Count == 0 ? 0 : stars / (mine.Count * 3.0);
        foreach (var a in mine.Where(a => a.Stars < 3).OrderByDescending(a => a.Target == 0 ? 0 : a.Value / (double)a.Target).Take(7))
        {
            Achievements.Add(new AchievementRow(a.Name, a.Stars, $"{ctx.N(a.Value)} / {ctx.N(a.Target)}",
                a.Target == 0 ? 0 : Math.Clamp(a.Value / (double)a.Target, 0, 1), a.Info));
        }

        ClanName = p.Clan?.Name;
        ClanTag = p.Clan?.Tag;
        ClanLevel = p.Clan is { } c ? $"Level {c.ClanLevel}" : null;
        GoldPassIcon = Icon("icons/Icon_HV_Gold_Pass.png");
        WarIcon = Icon("icons/Icon_HV_Clan_War.png");
        RaidIcon = Icon("icons/Icon_HV_Raid_Attack.png");
    }

    public override string Key => "profile";

    public override string Title => _ctx.Village == Village.Home ? "Profile & War" : "Profile";

    public bool IsHome => _ctx.Village == Village.Home;

    public bool HasPlayer { get; }

    public string Name { get; }

    public string Tag { get; }

    public ImageSource? HallImage { get; }

    public string HallText { get; }

    public string? XpText { get; }

    public string? RoleText { get; }

    public List<LabelChip> Labels { get; } = [];

    public string? LeagueName { get; }

    public ImageSource? LeagueImage { get; }

    public ImageSource? TrophyIcon { get; }

    public string? Trophies { get; }

    public string? BestTrophies { get; }

    public List<Stat> Stats { get; } = [];

    public string? AchievementText { get; }

    public double AchievementFraction { get; }

    public List<AchievementRow> Achievements { get; } = [];

    public ImageSource? GoldPassIcon { get; }

    public ImageSource? WarIcon { get; }

    public ImageSource? RaidIcon { get; }

    public string? ClanName { get; }

    public string? ClanTag { get; }

    public string? ClanLevel { get; }

    public bool HasClan => ClanName is not null;

    [ObservableProperty]
    private string? _clanDetail;

    [ObservableProperty]
    private ImageSource? _warLeagueImage;

    [ObservableProperty]
    private string? _warLeagueName;

    [ObservableProperty]
    private string? _capitalLeagueName;

    [ObservableProperty]
    private bool _hasWar;

    [ObservableProperty]
    private string _warTitle = "Clan War";

    [ObservableProperty]
    private string? _warState;

    [ObservableProperty]
    private string? _warCountdown;

    [ObservableProperty]
    private string? _ourClan;

    [ObservableProperty]
    private string? _theirClan;

    [ObservableProperty]
    private string? _ourScore;

    [ObservableProperty]
    private string? _theirScore;

    [ObservableProperty]
    private string? _attacksText;

    [ObservableProperty]
    private bool _attacksLeft;

    [ObservableProperty]
    private string? _warNote;

    [ObservableProperty]
    private bool _hasRaid;

    [ObservableProperty]
    private string? _raidTitle;

    [ObservableProperty]
    private string? _raidCountdown;

    [ObservableProperty]
    private string? _raidAttacks;

    [ObservableProperty]
    private string? _raidLoot;

    [ObservableProperty]
    private string? _raidClan;

    [ObservableProperty]
    private string? _goldPassText;

    [ObservableProperty]
    private bool _profileLoaded;

    private ImageSource? Icon(string path) => _ctx.Images.Remote(path, 48);

    private static string Role(string? role) => role switch
    {
        "leader" => "Leader",
        "coLeader" => "Co-leader",
        "admin" => "Elder",
        _ => "Member",
    };

    public override void OnProfile(ProfileData profile)
    {
        ProfileLoaded = true;
        if (profile.Clan?.Value is { } clan)
        {
            ClanDetail = $"{clan.Members}/50 members · {clan.WarWins} war wins" + (clan.WarWinStreak > 1 ? $" · streak {clan.WarWinStreak}" : "");
            WarLeagueName = clan.WarLeague?.Name;
            WarLeagueImage = clan.WarLeague is { } wl ? _ctx.Images.Remote(AssetPaths.WarLeague(wl.Name), 96) : null;
            CapitalLeagueName = clan.CapitalLeague?.Name is { } cl ? $"Capital: {cl}" : null;
        }

        ShowWar(profile);
        ShowRaid(profile.Raid);
        _goldPassEnd = profile.GoldPass?.EndTime;
        Tick(_ctx.Owner.Host.Time.GetUtcNow());
    }

    private void ShowWar(ProfileData profile)
    {
        WarNote = profile.WarProblem;
        if (profile.War is not { } info || info.War.Clan is not { } us || info.War.Opponent is not { } them)
        {
            HasWar = false;
            WarNote ??= "Not in a war right now.";
            return;
        }

        var w = info.War;
        HasWar = true;
        WarTitle = info.IsCwl ? $"CWL · Round {info.Round} of {info.Rounds}" : $"Clan War · {w.TeamSize} v {w.TeamSize}";
        OurClan = us.Name;
        TheirClan = them.Name;
        OurScore = $"★ {us.Stars}  ·  {us.DestructionPercentage.ToString("0.#", CultureInfo.InvariantCulture)}%";
        TheirScore = $"★ {them.Stars}  ·  {them.DestructionPercentage.ToString("0.#", CultureInfo.InvariantCulture)}%";
        (WarState, _warDeadline, _warDeadlineLabel) = w.State switch
        {
            "preparation" => ("Preparation day", w.StartTime, "Battle starts in"),
            "inWar" => ("Battle day", w.EndTime, "Ends in"),
            "warEnded" => (us.Stars > them.Stars || (us.Stars == them.Stars && us.DestructionPercentage > them.DestructionPercentage) ? "Won" : us.Stars == them.Stars && Math.Abs(us.DestructionPercentage - them.DestructionPercentage) < 0.001 ? "Tie" : "Lost",
                (DateTimeOffset?)null, ""),
            _ => (w.State, null, ""),
        };

        var perMember = w.AttacksPerMember > 0 ? w.AttacksPerMember : info.IsCwl ? 1 : 2;
        var me = us.Members.FirstOrDefault(m => PlayerTag.Normalize(m.Tag) == PlayerTag.Normalize(_ctx.Data.Account.Tag));
        if (me is null)
        {
            AttacksText = "You are not in this war";
            AttacksLeft = false;
        }
        else
        {
            var left = Math.Max(0, perMember - me.Attacks.Count);
            AttacksLeft = left > 0 && w.State != "warEnded";
            AttacksText = w.State == "warEnded"
                ? $"You used {me.Attacks.Count} of {perMember} · ★ {me.Attacks.Sum(a => a.Stars)}"
                : left == 0 ? $"All {perMember} attacks used · ★ {me.Attacks.Sum(a => a.Stars)}" : $"{left} of {perMember} attacks left · #{me.MapPosition} on the map";
        }
    }

    private void ShowRaid(RaidSeasonDto? raid)
    {
        if (raid is null)
        {
            HasRaid = false;
            return;
        }

        HasRaid = true;
        var ongoing = raid.State == "ongoing";
        RaidTitle = ongoing ? "Raid Weekend · in progress" : "Last Raid Weekend";
        _raidEnd = raid.EndTime;
        var me = raid.Members.FirstOrDefault(m => PlayerTag.Normalize(m.Tag) == PlayerTag.Normalize(_ctx.Data.Account.Tag));
        RaidAttacks = me is null ? "You did not raid" : $"{me.Attacks} of {me.AttackLimit + me.BonusAttackLimit} attacks";
        RaidLoot = me is null ? null : $"{_ctx.N(me.CapitalResourcesLooted)} Capital Gold looted";
        RaidClan = $"Clan: {_ctx.N(raid.CapitalTotalLoot)} looted · {raid.RaidsCompleted} raids · {raid.EnemyDistrictsDestroyed} districts";
    }

    public override void Tick(DateTimeOffset now)
    {
        if (_warDeadline is { } d)
        {
            WarCountdown = d > now ? $"{_warDeadlineLabel} {Fmt.Duration(d - now)}" : "Ending…";
        }
        else
        {
            WarCountdown = null;
        }

        if (_raidEnd is { } r)
        {
            RaidCountdown = r > now ? $"Ends in {Fmt.Duration(r - now)}" : $"Ended {Fmt.Ago(now - r)}";
        }

        if (_goldPassEnd is { } g)
        {
            GoldPassText = g > now ? $"Season ends in {Fmt.Duration(g - now)}" : "Season ended";
        }
    }
}
