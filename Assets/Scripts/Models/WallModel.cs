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
