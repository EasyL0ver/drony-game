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

    // ── Convenience ─────────────────────────

    // ── Construction ─────────────────────────

    protected WallModel(RoomModel owner, int edgeIndex)
    {
        Owner = owner;
        EdgeIndex = edgeIndex;
    }
}

/// <summary>
/// A corridor wall: passable with a given passage type. Optionally blocked by rubble.
/// </summary>
public class CorridorWallModel : WallModel
{
    public PassageType PassageType { get; set; }
    public bool IsBlocked { get; set; }

    private WallInteractionConfig _rubbleInteraction;

    public CorridorWallModel(RoomModel owner, int edgeIndex, PassageType passageType = PassageType.Corridor)
        : base(owner, edgeIndex)
    {
        PassageType = passageType;
    }

    public override WallPassability GetPassability(DroneModel drone)
    {
        if (Neighbor == null || IsBlocked)
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
        var list = new List<WallInteractionConfig>();
        if (_rubbleInteraction != null)
        {
            if (_rubbleInteraction.RequiredGear == GearType.None || drone.HasGear(_rubbleInteraction.RequiredGear))
                list.Add(_rubbleInteraction);
        }
        return list;
    }

    /// <summary>Set rubble blocking this corridor.</summary>
    public void SetRubble(WallInteractionConfig rubbleConfig)
    {
        _rubbleInteraction = rubbleConfig;
        IsBlocked = true;
    }

    /// <summary>Clear rubble: unblocks passage, removes interaction, returns new passage type.</summary>
    public PassageType? CompleteInteraction()
    {
        if (_rubbleInteraction == null || !_rubbleInteraction.BlocksPassage) return null;

        var resultType = _rubbleInteraction.ResultingPassageType;
        _rubbleInteraction = null;
        IsBlocked = false;
        PassageType = resultType;
        return resultType;
    }

    // ── Static helpers ──────────────────────

    public static float PassageBaseDuration(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor: return 1.5f;
            case PassageType.Duct:     return 2.5f;
            case PassageType.Vent:     return 3.5f;
            case PassageType.Rubble:   return 2.0f;
            default:                   return 1.5f;
        }
    }

    public static int PassageEnergyCost(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor: return 1;
            case PassageType.Rubble:   return 1;
            case PassageType.Duct:     return 2;
            case PassageType.Vent:     return 3;
            default:                   return 1;
        }
    }

    public static string PassageLabel(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor: return "CORRIDOR";
            case PassageType.Rubble:   return "RUBBLE";
            case PassageType.Duct:     return "DUCT";
            case PassageType.Vent:     return "VENT";
            default:                   return "TRAVEL";
        }
    }
}

/// <summary>
/// A station wall: always passable (corridor), has a station interaction.
/// </summary>
public class StationWallModel : WallModel
{
    private readonly WallInteractionConfig _interaction;

    public StationWallModel(RoomModel owner, int edgeIndex, WallInteractionConfig interaction)
        : base(owner, edgeIndex)
    {
        _interaction = interaction;
    }

    public override WallPassability GetPassability(DroneModel drone)
    {
        return WallPassability.Blocked;
    }

    public override List<WallInteractionConfig> GetInteractions(DroneModel drone)
    {
        var list = new List<WallInteractionConfig>();
        if (_interaction != null)
        {
            if (_interaction.RequiredGear == GearType.None || drone.HasGear(_interaction.RequiredGear))
                list.Add(_interaction);
        }
        return list;
    }
}
