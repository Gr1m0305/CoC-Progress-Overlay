using System.Text.Json;
using ClashOverlay.Core.Data;

namespace ClashOverlay.Core.State;

/// <summary>
/// "I upgraded this": <paramref name="Count"/> copies of a building (or a unit) went from <paramref name="FromLevel"/>
/// to <paramref name="ToLevel"/> at <paramref name="At"/>, after the last export.
/// </summary>
public sealed record LevelEdit(int ItemId, int FromLevel, int ToLevel, int Count, DateTimeOffset At);

public sealed record LevelEditResult(AccountState State, IReadOnlyList<LevelEdit> Applied, IReadOnlyList<LevelEdit> Obsolete);

/// <summary>
/// Levels the player raises by hand in the overlay, so progress stays current without exporting again. An edit only
/// counts while it is newer than the data it changes: a later export replaces every building edit made before it, and
/// a later API sync replaces unit edits (the API is the official source for troops, spells, heroes, pets and
/// equipment). Edits only ever raise levels.
/// </summary>
public static class LevelEdits
{
    /// <summary>The API can lag a few minutes behind the game, so a sync this soon after an edit does not overrule it.</summary>
    public static readonly TimeSpan ApiGrace = TimeSpan.FromMinutes(10);

    public static LevelEditResult Apply(GameData data, AccountState state, IReadOnlyList<LevelEdit> edits)
    {
        if (edits.Count == 0)
        {
            return new LevelEditResult(state, [], []);
        }

        var applied = new List<LevelEdit>();
        var obsolete = new List<LevelEdit>();
        var home = new Builder(state.Home);
        var bb = new Builder(state.BuilderBase);
        foreach (var edit in edits.OrderBy(e => e.At))
        {
            var item = data.Get(edit.ItemId);
            if (item is null || edit.ToLevel <= edit.FromLevel || edit.ToLevel > item.TopLevel || edit.Count < 1)
            {
                obsolete.Add(edit);
                continue;
            }

            var village = item.Village == Village.Home ? home : bb;
            var ok = item.IsStructure ? village.ApplyStructure(item, edit) : village.ApplyUnit(item, edit);
            (ok ? applied : obsolete).Add(edit);
        }

        return new LevelEditResult(state with { Home = home.Build(), BuilderBase = bb.Build() }, applied, obsolete);
    }

    /// <summary>A new edit for <paramref name="count"/> copies of <paramref name="item"/> at <paramref name="fromLevel"/>, or null if that would pass the top level.</summary>
    public static LevelEdit? Raise(ItemDef item, int fromLevel, int count, DateTimeOffset now) =>
        fromLevel + 1 > item.TopLevel || count < 1 ? null : new LevelEdit(item.Id, fromLevel, fromLevel + 1, count, now);

    /// <summary>The list without the newest edit of <paramref name="itemId"/>.</summary>
    public static IReadOnlyList<LevelEdit> UndoLast(IReadOnlyList<LevelEdit> edits, int itemId)
    {
        var last = edits.Where(e => e.ItemId == itemId).MaxBy(e => e.At);
        return last is null ? edits : edits.Where(e => !ReferenceEquals(e, last)).ToList();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static IReadOnlyList<LevelEdit> Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<LevelEdit>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Save(string path, IReadOnlyList<LevelEdit> edits)
    {
        if (edits.Count == 0)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(edits, Json));
        File.Move(tmp, path, overwrite: true);
    }

    private sealed class Builder(VillageState source)
    {
        private readonly List<StructureInstance> _structures = source.Structures.ToList();
        private readonly Dictionary<int, UnitState> _units = new(source.Units);

        public bool ApplyStructure(ItemDef item, LevelEdit edit)
        {
            if (source.ExportTime is { } exported && exported >= edit.At)
            {
                return false; // a newer export already shows the real level
            }

            // Copies still at the starting level; a running upgrade from that level is the one that finished.
            var candidates = _structures
                .Select((s, i) => (s, i))
                .Where(x => x.s.Item.Id == item.Id && x.s.Level == edit.FromLevel)
                .OrderByDescending(x => x.s.IsUpgrading)
                .Take(edit.Count)
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            foreach (var (s, i) in candidates)
            {
                _structures[i] = new StructureInstance { Item = s.Item, Level = edit.ToLevel, GearedUp = s.GearedUp, Edited = true };
            }

            return true;
        }

        public bool ApplyUnit(ItemDef item, LevelEdit edit)
        {
            _units.TryGetValue(item.Id, out var unit);
            var level = unit?.Level ?? 0;
            if (level >= edit.ToLevel)
            {
                return false; // already there
            }

            if (unit?.ApiLevel is not null && source.ApiTime is { } synced && synced >= edit.At + ApiGrace)
            {
                return false; // a later API sync says otherwise
            }

            if (unit?.Source == LevelSource.Export && source.ExportTime is { } exported && exported >= edit.At)
            {
                return false;
            }

            // Any running upgrade was to a level at or below the new one, so it is done.
            _units[item.Id] = new UnitState { Item = item, Level = edit.ToLevel, Source = LevelSource.Manual, ApiLevel = unit?.ApiLevel };
            return true;
        }

        public VillageState Build() => new()
        {
            Village = source.Village,
            HallLevel = source.HallLevel,
            Structures = _structures,
            Units = _units,
            AsOf = source.AsOf,
            ExportTime = source.ExportTime,
            ApiTime = source.ApiTime,
            HasStructures = source.HasStructures,
            Builders = VillageStateBuilder.CountBuilders(source.Village, _structures),
        };
    }
}
