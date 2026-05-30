using UnityEngine;

/// <summary>
/// Pure game-logic model for a single hex wall (edge).
/// Each room has 6 walls. A wall may be solid, contain a passage (connection to
/// a neighbor), and/or have an interaction (charge, refit, rubble clear, etc.).
/// </summary>
public class WallModel
{
    /// <summary>Which hex edge (0-5) this wall occupies.</summary>
    public int EdgeIndex { get; private set; }

    /// <summary>The room this wall belongs to.</summary>
    public RoomModel Owner { get; private set; }

    /// <summary>The wall on the other side of this edge, or null if solid (no neighbor room).</summary>
    public WallModel Neighbor { get; set; }

    // ── Passage ──────────────────────────────

    /// <summary>True if this wall has a passage to a neighbor.</summary>
    public bool HasPassage => Neighbor != null;

    /// <summary>Type of passage through this wall (only meaningful if HasPassage).</summary>
    public PassageType PassageType { get; set; } = PassageType.Corridor;

    /// <summary>True if passage is currently blocked (e.g. rubble that hasn't been cleared).</summary>
    public bool IsBlocked { get; set; }

    /// <summary>True if a drone can move through this wall right now.</summary>
    public bool IsPassable => HasPassage && !IsBlocked;

    // ── Interaction ─────────────────────────

    /// <summary>Interaction config on this wall, or null if none.</summary>
    public WallInteractionConfig Interaction { get; set; }

    /// <summary>True if this wall has an interaction a drone can perform.</summary>
    public bool HasInteraction => Interaction != null;

    // ── Logic ────────────────────────────────

    /// <summary>
    /// Whether the given drone can perform the wall interaction.
    /// Checks required gear. Does NOT check energy (that depends on travel path).
    /// </summary>
    public bool CanInteract(DroneModel drone)
    {
        if (!HasInteraction) return false;
        if (Interaction.RequiredGear == GearType.None) return true;
        return drone.HasGear(Interaction.RequiredGear);
    }

    /// <summary>
    /// Whether the given drone can move through this wall.
    /// </summary>
    public bool CanPass(DroneModel drone)
    {
        if (!HasPassage) return false;
        if (!IsBlocked) return true;
        return false;
    }

    // ── Action Factory ─────────────────────

    /// <summary>
    /// Begin a traversal through this wall. Returns a handle to track progress.
    /// </summary>
    public WallAction BeginTraversal(DroneModel drone)
    {
        float duration = GetTraversalDuration(drone);
        int cost = MapModel.StepEnergyCost(PassageType);
        string label = MapModel.PassageLabel(PassageType);
        return new WallAction(this, drone, duration, cost, label);
    }

    /// <summary>
    /// Begin the wall interaction. Returns a handle to track progress.
    /// The handle's ShouldRepeat delegate is set from the config's RepeatCondition.
    /// </summary>
    public WallAction BeginInteraction(DroneModel drone)
    {
        if (!HasInteraction) return null;
        var cfg = Interaction;
        float duration = GetInteractionDuration(drone);
        var action = new WallAction(this, drone, duration, cfg.EnergyCost, cfg.Label);

        if (cfg.RepeatCondition != null)
        {
            action.ShouldRepeat = () =>
            {
                // Apply per-cycle effects
                if (cfg.EnergyGainPerCycle > 0)
                    drone.CurrentEnergy = Mathf.Min(drone.MaxEnergy, drone.CurrentEnergy + cfg.EnergyGainPerCycle);
                return cfg.RepeatCondition(drone);
            };
        }

        return action;
    }

    // ── Duration Queries ────────────────────

    /// <summary>
    /// How long it takes the given drone to traverse this passage.
    /// </summary>
    public float GetTraversalDuration(DroneModel drone)
    {
        if (!HasPassage) return 0f;
        return PassageBaseDuration(PassageType);
    }

    /// <summary>
    /// How long it takes the given drone to perform the interaction.
    /// </summary>
    public float GetInteractionDuration(DroneModel drone)
    {
        if (!HasInteraction) return 0f;
        return Interaction.BaseDuration;
    }

    static float PassageBaseDuration(PassageType type)
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

    // ── Construction ─────────────────────────

    public WallModel(RoomModel owner, int edgeIndex)
    {
        Owner = owner;
        EdgeIndex = edgeIndex;
    }

    /// <summary>
    /// Complete the wall interaction: clears it and unblocks the passage.
    /// Returns the resulting passage type, or null if no interaction was present.
    /// </summary>
    public PassageType? CompleteInteraction()
    {
        if (!HasInteraction) return null;
        if (!Interaction.BlocksPassage) return null;

        var resultType = Interaction.ResultingPassageType;
        Interaction = null;
        IsBlocked = false;
        PassageType = resultType;
        return resultType;
    }
}
