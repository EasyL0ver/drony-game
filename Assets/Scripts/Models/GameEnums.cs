/// <summary>
/// Shared game enums — no Unity dependencies beyond basic types.
/// </summary>

public enum FogState { Unknown, Scanning, Discovered, Visible }

public enum PassageType { Corridor, Duct, Vent, Rubble, CrookedVent, BlastDoor }

public enum RoomSize { Large, Medium, Small }

public enum GearType { None, Scanner, Bomb, Cargo, Battery, PowerTap }

public enum SlotSize { Small, Medium, Large }

public enum DroneType { Scout, Hauler }

public enum CargoType { None, FuelCell }

/// <summary>
/// Describes a wall interaction: anything a drone can do at a wall
/// (charge, refit, clear rubble, bomb, etc.). Configured per-wall.
/// </summary>
public class WallInteractionConfig
{
    /// <summary>UI label (e.g. "CHARGE", "REFIT", "CLEAR").</summary>
    public string Label { get; set; }

    /// <summary>Base duration of one cycle in seconds.</summary>
    public float BaseDuration { get; set; }

    /// <summary>Energy cost applied on completion (0 for stations).</summary>
    public int EnergyCost { get; set; }

    /// <summary>Required gear to perform this interaction (None = no gear needed).</summary>
    public GearType RequiredGear { get; set; } = GearType.None;

    /// <summary>Required drone type (null = any drone can do it).</summary>
    public DroneType? RequiredDroneType { get; set; }

    /// <summary>If true, this interaction blocks passage until completed.</summary>
    public bool BlocksPassage { get; set; }

    /// <summary>If true, interaction requires the room to be powered.</summary>
    public bool RequiresPower { get; set; }

    /// <summary>Passage type after interaction completes (only if BlocksPassage).</summary>
    public PassageType ResultingPassageType { get; set; }

    /// <summary>If true, the drone is destroyed on completion (e.g. bomb).</summary>
    public bool DestroysDrone { get; set; }

    /// <summary>If true, completing this enables gear management (refit).</summary>
    public bool EnablesRefit { get; set; }

    /// <summary>If true, completing this enables selling equipped gear for points.</summary>
    public bool EnablesSell { get; set; }

    /// <summary>Energy gained per cycle (e.g. charging gives +5).</summary>
    public int EnergyGainPerCycle { get; set; }

    /// <summary>
    /// Whether this interaction repeats after a cycle.
    /// Called with the drone model — return true to go again.
    /// If null, interaction is one-shot.
    /// </summary>
    public System.Func<DroneModel, bool> RepeatCondition { get; set; }

    /// <summary>Cargo type awarded on pickup (if any).</summary>
    public CargoType CargoReward { get; set; } = CargoType.None;

    /// <summary>If true, drone must be carrying cargo to interact.</summary>
    public bool RequiresCargo { get; set; }

    /// <summary>Points awarded to player on completion.</summary>
    public int PointsReward { get; set; }

    /// <summary>Power drawn from the room's power network per cycle.</summary>
    public int PowerCost { get; set; }

    /// <summary>Specific loot item awarded on pickup (overrides generic CargoReward).</summary>
    public GearItem LootItem { get; set; }

    // ── Factory presets ─────────────────────

    public static WallInteractionConfig Charging() => new WallInteractionConfig
    {
        Label = "CHARGE",
        BaseDuration = 0.6f,
        EnergyGainPerCycle = 5,
        PowerCost = 3,
        RepeatCondition = drone => drone.CurrentEnergy < drone.MaxEnergy,
    };

    public static WallInteractionConfig Refitting() => new WallInteractionConfig
    {
        Label = "REFIT",
        BaseDuration = 2f,
        EnablesRefit = true,
        PowerCost = 5,
    };

    public static WallInteractionConfig RubbleClear(GearType gear) => new WallInteractionConfig
    {
        Label = "CLEAR",
        BaseDuration = 3f,
        EnergyCost = 2,
        RequiredGear = gear,
        BlocksPassage = true,
        ResultingPassageType = PassageType.Corridor,
        DestroysDrone = gear == GearType.Bomb,
    };

    public static WallInteractionConfig LootPickup(GearItem loot = null) => new WallInteractionConfig
    {
        Label = "PICK UP",
        BaseDuration = 1.5f,
        RequiredDroneType = DroneType.Hauler,
        CargoReward = CargoType.FuelCell,
        LootItem = loot ?? GearCatalog.FuelCell,
    };

    public static WallInteractionConfig ScanCache() => new WallInteractionConfig
    {
        Label = "SCAN",
        BaseDuration = 1f,
        RequiredGear = GearType.Scanner,
    };

    public static WallInteractionConfig Unload() => new WallInteractionConfig
    {
        Label = "SELL",
        BaseDuration = 1f,
        EnablesSell = true,
    };
}
