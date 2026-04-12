/// <summary>
/// A piece of gear that can be equipped to a drone slot.
/// Immutable data object — create via GearCatalog.
/// </summary>
public class GearItem
{
    public GearType Type { get; }
    public string Name { get; }
    public string Description { get; }
    public int Cost { get; }
    public string Icon { get; }

    public GearItem(GearType type, string name, string description, int cost, string icon = "⚙")
    {
        Type = type;
        Name = name;
        Description = description;
        Cost = cost;
        Icon = icon;
    }
}

/// <summary>
/// Static registry of all gear definitions available in the game.
/// </summary>
public static class GearCatalog
{
    public static readonly GearItem Scanner = new GearItem(
        GearType.Scanner,
        "Scanner",
        "Allows the drone to scan and reveal unknown rooms.",
        2,
        "\u25CE" // ◎ bullseye — radar/scan
    );

    public static readonly GearItem RubbleClearer = new GearItem(
        GearType.RubbleClearer,
        "Rubble Clearer",
        "Allows the drone to clear rubble-blocked passages.",
        3,
        "\u2692" // ⚒ hammer and pick
    );

    public static GearItem[] All = new GearItem[]
    {
        Scanner,
        RubbleClearer,
    };

    public static GearItem Get(GearType type)
    {
        foreach (var g in All)
            if (g.Type == type) return g;
        return null;
    }
}
