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
    public static readonly ScannerItem Scanner = new ScannerItem();

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
        "Standard cargo. Worth 3 points at loading station.",
        0,
        "\u2B23", // ⬣ hexagon
        SlotSize.Large,
        sellPrice: 3
    );

    public static readonly GearItem DataCore = new GearItem(
        GearType.Cargo,
        "Data Core",
        "Compact salvage. Worth 2 points at loading station.",
        0,
        "\u25C8", // ◈ diamond
        SlotSize.Small,
        sellPrice: 2
    );

    public static readonly GearItem HeavySalvage = new GearItem(
        GearType.Cargo,
        "Heavy Salvage",
        "Valuable heavy cargo. Worth 5 points at loading station.",
        0,
        "\u2B22", // ⬢ filled hexagon
        SlotSize.Large,
        sellPrice: 5
    );

    /// <summary>All cargo types that can appear in loot caches, with relative weights.</summary>
    public static readonly (GearItem item, int weight)[] LootTable = new[]
    {
        (DataCore, 3),
        (FuelCell, 4),
        (HeavySalvage, 1),
    };

    public static readonly GearItem BatteryS = new GearItem(
        GearType.Battery,
        "Battery S",
        "Small battery pack. +3 max energy.",
        2,
        "\u26A1", // ⚡ lightning
        SlotSize.Small,
        sellPrice: 1
    );

    public static readonly GearItem BatteryM = new GearItem(
        GearType.Battery,
        "Battery M",
        "Medium battery pack. +5 max energy.",
        4,
        "\u26A1", // ⚡ lightning
        SlotSize.Medium,
        sellPrice: 2
    );

    public static readonly GearItem BatteryL = new GearItem(
        GearType.Battery,
        "Battery L",
        "Large battery pack. +8 max energy.",
        6,
        "\u26A1", // ⚡ lightning
        SlotSize.Large,
        sellPrice: 3
    );

    public static readonly GearItem PowerTap = new GearItem(
        GearType.PowerTap,
        "Power Tap",
        "Feeds the drone's energy into the power network while stationary.",
        5,
        "\u2301", // ⌁ electric arrow
        SlotSize.Medium,
        sellPrice: 3
    );

    public static readonly EnergyLinkItem EnergyLink = new EnergyLinkItem();

    public static GearItem[] All = new GearItem[]
    {
        Scanner,
        Bomb,
        BatteryS,
        BatteryM,
        BatteryL,
        PowerTap,
        EnergyLink,
    };

    public static GearItem Get(GearType type)
    {
        foreach (var g in All)
            if (g.Type == type) return g;
        return null;
    }
}
