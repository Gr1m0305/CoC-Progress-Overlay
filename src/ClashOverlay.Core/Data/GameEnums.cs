namespace ClashOverlay.Core.Data;

public enum Village
{
    Home,
    BuilderBase,
}

public enum ItemKind
{
    Building,
    Trap,
    Wall,
    TownHall,
    BuilderHall,
    Troop,
    Siege,
    Spell,
    Hero,
    Pet,
    Equipment,
    Guardian,
    Helper,
}

/// <summary>Progress categories. Builder Base items use the same values with <see cref="Village.BuilderBase"/>.</summary>
public enum Category
{
    None,
    Defenses,
    ArmyBuildings,
    Resources,
    Traps,
    Walls,
    Heroes,
    Troops,
    Spells,
    SiegeMachines,
    Pets,
    Guardians,
    Equipment,
    Helpers,
}

/// <summary>Which queue an upgrade occupies.</summary>
public enum UpgradeTrack
{
    None,
    Builder,
    Laboratory,
    PetHouse,
    Blacksmith,
    Gems,
    BuilderBaseBuilder,
    StarLaboratory,
}

public enum Resource
{
    None,
    Gold,
    Elixir,
    DarkElixir,
    BuilderGold,
    BuilderElixir,
    Gems,
    ShinyOre,
    GlowyOre,
    StarryOre,
}

public static class GameEnumText
{
    public static Resource ParseResource(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "gold" => Resource.Gold,
        "elixir" => Resource.Elixir,
        "dark elixir" or "dark_elixir" => Resource.DarkElixir,
        "builder gold" or "builder_gold" => Resource.BuilderGold,
        "builder elixir" or "builder_elixir" => Resource.BuilderElixir,
        "gems" or "gem" => Resource.Gems,
        "shiny ore" or "shiny_ore" => Resource.ShinyOre,
        "glowy ore" or "glowy_ore" => Resource.GlowyOre,
        "starry ore" or "starry_ore" => Resource.StarryOre,
        _ => Resource.None,
    };

    public static string DisplayName(this Resource r) => r switch
    {
        Resource.Gold => "Gold",
        Resource.Elixir => "Elixir",
        Resource.DarkElixir => "Dark Elixir",
        Resource.BuilderGold => "Builder Gold",
        Resource.BuilderElixir => "Builder Elixir",
        Resource.Gems => "Gems",
        Resource.ShinyOre => "Shiny Ore",
        Resource.GlowyOre => "Glowy Ore",
        Resource.StarryOre => "Starry Ore",
        _ => string.Empty,
    };

    public static string DisplayName(this Category c, Village village = Village.Home) => c switch
    {
        Category.Defenses => "Defenses",
        Category.ArmyBuildings => "Army buildings",
        Category.Resources => "Resources",
        Category.Traps => "Traps",
        Category.Walls => "Walls",
        Category.Heroes => "Heroes",
        Category.Troops => "Troops",
        Category.Spells => "Spells",
        Category.SiegeMachines => "Siege Machines",
        Category.Pets => "Pets",
        Category.Guardians => "Guardians",
        Category.Equipment => "Hero Equipment",
        Category.Helpers => "Helpers",
        _ => string.Empty,
    };

    public static string HallName(this Village village) => village == Village.Home ? "Town Hall" : "Builder Hall";

    public static string HallShort(this Village village) => village == Village.Home ? "TH" : "BH";
}
