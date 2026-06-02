using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Describes whether/how a drone can traverse a wall.
/// </summary>
public struct WallPassability
{
    public bool CanPass;
    public float Duration;
    public int EnergyCost;
    public string Label;

    public static WallPassability Blocked => new WallPassability { CanPass = false };
}

/// <summary>
/// Base model for a single hex wall (edge).
/// Subclasses define passage and interaction behavior through two virtual methods.
/// </summary>
public abstract class WallModel
{
    /// <summary>Which hex edge (0-5) this wall occupies.</summary>
    public int EdgeIndex { get; private set; }

    /// <summary>The room this wall belongs to.</summary>
    public RoomModel Owner { get; private set; }

    /// <summary>The wall on the other side of this edge, or null if solid.</summary>
    public WallModel Neighbor { get; set; }

    // ── Core interface ───────────────────────

    /// <summary>Can the drone pass through this wall, and if so how fast/expensive?</summary>
    public abstract WallPassability GetPassability(DroneModel drone);

    /// <summary>What interactions are available for this drone at this wall?</summary>
    public abstract List<WallInteractionConfig> GetInteractions(DroneModel drone);

    /// <summary>Called before a drone begins traversing this wall. Override for gating logic (e.g. door opening, power draw).</summary>
    public virtual void BeforeTraversal() { }

    // ── Convenience ─────────────────────────

    // ── Construction ─────────────────────────

    protected WallModel(RoomModel owner, int edgeIndex)
    {
        Owner = owner;
        EdgeIndex = edgeIndex;
    }
}

/// <summary>
/// A corridor wall: passable with a given passage type.
/// </summary>
public class CorridorWallModel : WallModel
{
    public PassageType PassageType { get; set; }

    protected IPowerProvider _powerProvider;

    public CorridorWallModel(RoomModel owner, int edgeIndex, PassageType passageType = PassageType.Corridor)
        : base(owner, edgeIndex)
    {
        PassageType = passageType;
    }

    /// <summary>Inject power dependency.</summary>
    public void SetPowerProvider(IPowerProvider provider)
    {
        _powerProvider = provider;
    }

    /// <summary>Whether either neighboring room currently has power.</summary>
    public bool IsPowered
    {
        get
        {
            if (_powerProvider == null) return false;
            if (_powerProvider.IsRoomPowered(Owner.Coord)) return true;
            if (Neighbor != null && _powerProvider.IsRoomPowered(Neighbor.Owner.Coord)) return true;
            return false;
        }
    }

    public override WallPassability GetPassability(DroneModel drone)
    {
        if (Neighbor == null)
            return WallPassability.Blocked;

        if (!drone.CanTraverse(PassageType))
            return WallPassability.Blocked;

        return new WallPassability
        {
            CanPass = true,
            Duration = PassageBaseDuration(PassageType),
            EnergyCost = PassageEnergyCost(PassageType),
            Label = PassageLabel(PassageType),
        };
    }

    public override List<WallInteractionConfig> GetInteractions(DroneModel drone)
    {
        return new List<WallInteractionConfig>();
    }

    // ── Static helpers ──────────────────────

    public static float PassageBaseDuration(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor:    return 1.0f;
            case PassageType.Duct:        return 2.0f;
            case PassageType.Vent:        return 3.0f;
            case PassageType.CrookedVent: return 4.0f;
            case PassageType.Rubble:      return 2.0f;
            default:                      return 1.0f;
        }
    }

    public static int PassageEnergyCost(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor:    return 1;
            case PassageType.Rubble:      return 1;
            case PassageType.Duct:        return 2;
            case PassageType.Vent:        return 3;
            case PassageType.CrookedVent: return 4;
            default:                      return 1;
        }
    }

    public static string PassageLabel(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor:    return "CORRIDOR";
            case PassageType.Rubble:      return "RUBBLE";
            case PassageType.Duct:        return "DUCT";
            case PassageType.Vent:        return "VENT";
            case PassageType.CrookedVent: return "CROOKED VENT";
            default:                      return "TRAVEL";
        }
    }
}

/// <summary>
/// A corridor blocked by an obstacle (e.g. rubble). Requires an interaction to clear.
/// </summary>
public class ObstacleWallModel : CorridorWallModel
{
    public bool IsBlocked { get; private set; }

    private WallInteractionConfig _interaction;

    public ObstacleWallModel(RoomModel owner, int edgeIndex, PassageType passageType, WallInteractionConfig interaction)
        : base(owner, edgeIndex, passageType)
    {
        _interaction = interaction;
        IsBlocked = true;
    }

    public override WallPassability GetPassability(DroneModel drone)
    {
        if (IsBlocked) return WallPassability.Blocked;
        return base.GetPassability(drone);
    }

    public override List<WallInteractionConfig> GetInteractions(DroneModel drone)
    {
        var list = new List<WallInteractionConfig>();
        if (!IsBlocked || _interaction == null) return list;

        if (_interaction.RequiredGear != GearType.None && !drone.HasGear(_interaction.RequiredGear))
            return list;
        if (_interaction.RequiresPower && !IsPowered)
            return list;

        list.Add(_interaction);
        return list;
    }

    /// <summary>Clear obstacle: unblocks passage, removes interaction, returns resulting passage type.</summary>
    public PassageType? CompleteInteraction()
    {
        if (_interaction == null || !_interaction.BlocksPassage) return null;

        var resultType = _interaction.ResultingPassageType;
        _interaction = null;
        IsBlocked = false;
        PassageType = resultType;
        return resultType;
    }
}

/// <summary>
/// A blast door wall: passable only when either neighboring room is powered.
/// Draws energy from the network on each traversal.
/// </summary>
public class BlastDoorWallModel : CorridorWallModel
{
    public const int TraversalCost = 5;

    public BlastDoorWallModel(RoomModel owner, int edgeIndex)
        : base(owner, edgeIndex, PassageType.BlastDoor)
    {
    }

    public override WallPassability GetPassability(DroneModel drone)
    {
        if (Neighbor == null || !IsPowered)
            return WallPassability.Blocked;

        return new WallPassability
        {
            CanPass = true,
            Duration = PassageBaseDuration(PassageType),
            EnergyCost = PassageEnergyCost(PassageType),
            Label = "BLAST DOOR",
        };
    }

    /// <summary>Opens the door, drawing network power.</summary>
    public override void BeforeTraversal()
    {
        if (_powerProvider == null) return;
        _powerProvider.Draw(TraversalCost);
    }
}

/// <summary>
/// A station wall: always passable (corridor), has a station interaction.
/// Requires power to function (if a power provider is set).
/// </summary>
public class StationWallModel : WallModel
{
    private readonly WallInteractionConfig _interaction;
    private IPowerProvider _powerProvider;

    public DroneModel OccupiedBy { get; set; }

    public StationWallModel(RoomModel owner, int edgeIndex, WallInteractionConfig interaction)
        : base(owner, edgeIndex)
    {
        _interaction = interaction;
    }

    /// <summary>Inject power dependency. Station only works when room is powered.</summary>
    public void SetPowerProvider(IPowerProvider provider)
    {
        _powerProvider = provider;
    }

    /// <summary>Whether this station's room currently has power.</summary>
    public bool IsPowered
    {
        get
        {
            if (_powerProvider == null) return true; // no power system = always on
            return _powerProvider.IsRoomPowered(Owner.Coord);
        }
    }

    /// <summary>
    /// Attempt to draw power for one interaction cycle.
    /// Returns true if enough power was drawn (or no power cost).
    /// </summary>
    public bool TryDrawPower(WallInteractionConfig cfg)
    {
        if (cfg == null || cfg.PowerCost <= 0) return true;
        if (_powerProvider == null) return true;
        if (!_powerProvider.IsRoomPowered(Owner.Coord)) return false;

        int drawn = _powerProvider.Draw(cfg.PowerCost);
        return drawn >= cfg.PowerCost;
    }

    public override WallPassability GetPassability(DroneModel drone)
    {
        return WallPassability.Blocked;
    }

    public override List<WallInteractionConfig> GetInteractions(DroneModel drone)
    {
        var list = new List<WallInteractionConfig>();
        if (OccupiedBy != null && OccupiedBy != drone) return list;
        if (!IsPowered) return list;
        if (_interaction != null)
        {
            if (_interaction.RequiredGear != GearType.None && !drone.HasGear(_interaction.RequiredGear))
                return list;
            if (_interaction.RequiredDroneType != null && drone.Type != _interaction.RequiredDroneType.Value)
                return list;
            if (_interaction.LootItem != null && !drone.HasFreeSlot(_interaction.LootItem.Size))
                return list;
            else if (_interaction.LootItem == null && _interaction.CargoReward != CargoType.None && !drone.HasFreeSlot(SlotSize.Large))
                return list;
            list.Add(_interaction);
        }
        return list;
    }
}

/// <summary>
/// Loot cache wall: starts unscanned (unknown content). 
/// Scouts with scanner can scan to reveal. Hauler can pick up.
/// </summary>
public class LootCacheWallModel : WallModel
{
    public GearItem Content { get; private set; }
    public bool IsScanned { get; set; }
    public DroneModel OccupiedBy { get; set; }

    WallInteractionConfig _scanInteraction;
    WallInteractionConfig _pickupInteraction;

    public LootCacheWallModel(RoomModel owner, int edgeIndex, GearItem content)
        : base(owner, edgeIndex)
    {
        Content = content;
        _scanInteraction = WallInteractionConfig.ScanCache();
        _pickupInteraction = WallInteractionConfig.LootPickup(content);
    }

    public override WallPassability GetPassability(DroneModel drone)
    {
        return WallPassability.Blocked;
    }

    public override List<WallInteractionConfig> GetInteractions(DroneModel drone)
    {
        var list = new List<WallInteractionConfig>();
        if (OccupiedBy != null && OccupiedBy != drone) return list;

        // Scan: scout with scanner, cache not yet scanned
        if (!IsScanned && drone.HasGear(GearType.Scanner))
        {
            list.Add(_scanInteraction);
        }

        // Pickup: hauler with free slot of matching size
        if (drone.Type == DroneType.Hauler && drone.HasFreeSlot(Content.Size))
        {
            list.Add(_pickupInteraction);
        }

        return list;
    }
}
