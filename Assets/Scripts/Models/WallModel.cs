using UnityEngine;

/// <summary>
/// Pure game-logic model for a single hex wall (edge).
/// Each room has 6 walls. A wall may be solid, contain a passage (connection to
/// a neighbor), host a station, and/or have a wall interaction (rubble clear, etc.).
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

    // ── Station ──────────────────────────────

    /// <summary>Station type on this wall, if any.</summary>
    public StationType Station { get; set; } = StationType.None;

    /// <summary>True if this wall hosts a station.</summary>
    public bool HasStation => Station != StationType.None;

    // ── Wall Interaction ─────────────────────

    /// <summary>Current wall interaction on this wall, or null if none.</summary>
    public WallInteraction? Interaction { get; set; }

    /// <summary>True if this wall has an active interaction (rubble clear, etc.).</summary>
    public bool HasInteraction => Interaction.HasValue;

    // ── Logic ────────────────────────────────

    /// <summary>
    /// Whether the given drone can perform the wall interaction.
    /// Checks required gear. Does NOT check energy (that depends on travel path).
    /// </summary>
    public bool CanInteract(DroneModel drone)
    {
        if (!HasInteraction) return false;
        return drone.HasGear(Interaction.Value.requiredGear);
    }

    /// <summary>
    /// Whether the given drone can move through this wall.
    /// A passage must exist and not be blocked, OR the drone has gear to pass
    /// through (e.g. future drill that can traverse rubble without clearing it).
    /// </summary>
    public bool CanPass(DroneModel drone)
    {
        if (!HasPassage) return false;
        if (!IsBlocked) return true;
        // Future: check if drone has gear that allows passage through blocked walls
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
    /// Begin a wall interaction (rubble clear, bomb, etc.). Returns a handle to track progress.
    /// </summary>
    public WallAction BeginInteraction(DroneModel drone)
    {
        if (!HasInteraction) return null;
        var inter = Interaction.Value;
        float duration = GetInteractionDuration(drone);
        return new WallAction(this, drone, duration, inter.energyCost, inter.label);
    }

    /// <summary>
    /// Begin a station action (charge/refit). Returns a handle to track one cycle.
    /// The handle's ShouldRepeat delegate encodes whether another cycle is needed.
    /// </summary>
    public WallAction BeginStationAction(DroneModel drone)
    {
        if (!HasStation) return null;
        float duration = GetStationDuration(drone);
        string label = MapModel.StationLabel(Station);
        var action = new WallAction(this, drone, duration, 0, label);

        switch (Station)
        {
            case StationType.Charging:
                action.ShouldRepeat = () =>
                {
                    drone.CurrentEnergy = UnityEngine.Mathf.Min(drone.MaxEnergy, drone.CurrentEnergy + MapModel.ChargeEnergyGain);
                    return drone.CurrentEnergy < drone.MaxEnergy;
                };
                break;

            case StationType.Refitting:
                // Single cycle, no repeat
                break;
        }

        return action;
    }

    // ── Duration Queries ────────────────────

    /// <summary>
    /// How long it takes the given drone to traverse this passage.
    /// Depends on passage type and drone capabilities.
    /// </summary>
    public float GetTraversalDuration(DroneModel drone)
    {
        if (!HasPassage) return 0f;
        float baseDuration = PassageBaseDuration(PassageType);
        // Future: drone gear/stats could modify speed
        return baseDuration;
    }

    /// <summary>
    /// How long it takes the given drone to perform the wall interaction.
    /// Depends on interaction type and drone gear.
    /// </summary>
    public float GetInteractionDuration(DroneModel drone)
    {
        if (!HasInteraction) return 0f;
        return Interaction.Value.duration;
        // Future: drone gear could speed this up
    }

    /// <summary>
    /// How long one cycle of the station action takes for this drone.
    /// Depends on station type and drone capabilities.
    /// </summary>
    public float GetStationDuration(DroneModel drone)
    {
        if (!HasStation) return 0f;
        return StationBaseDuration(Station);
        // Future: drone gear could speed charging/refitting
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

    static float StationBaseDuration(StationType type)
    {
        switch (type)
        {
            case StationType.Charging:  return MapModel.ChargeDuration;
            case StationType.Refitting: return MapModel.RefitDuration;
            default:                    return 0f;
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

        var resultType = Interaction.Value.resultingPassageType;
        Interaction = null;
        IsBlocked = false;
        PassageType = resultType;
        return resultType;
    }
}
