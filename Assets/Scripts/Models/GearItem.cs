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
    public int SellPrice { get; }
    public string Icon { get; }
    public SlotSize Size { get; }

    public GearItem(GearType type, string name, string description, int cost, string icon = "⚙", SlotSize size = SlotSize.Small, int sellPrice = -1)
    {
        Type = type;
        Name = name;
        Description = description;
        Cost = cost;
        SellPrice = sellPrice >= 0 ? sellPrice : (cost / 2 > 0 ? cost / 2 : 1);
        Icon = icon;
        Size = size;
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
        "\u25CE", // ◎ bullseye — radar/scan
        SlotSize.Small,
        sellPrice: 1
    );

    public static readonly GearItem Bomb = new GearItem(
        GearType.Bomb,
        "Bomb",
        "Clears rubble-blocked passages but destroys the drone.",
        3,
        "\u2622", // ☢ radioactive — explosive
        SlotSize.Small,
        sellPrice: 2
    );

    public static readonly GearItem FuelCell = new GearItem(
        GearType.Cargo,
        "Fuel Cell",
        "Precious cargo. Deliver to a loading station for points.",
        0,
        "\u2B23", // ⬣ hexagon
        SlotSize.Large,
        sellPrice: 3
    );

    public static GearItem[] All = new GearItem[]
    {
        Scanner,
        Bomb,
    };

    public static GearItem Get(GearType type)
    {
        foreach (var g in All)
            if (g.Type == type) return g;
        return null;
    }
}
