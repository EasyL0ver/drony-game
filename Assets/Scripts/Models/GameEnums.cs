/// <summary>
/// Shared game enums — no Unity dependencies beyond basic types.
/// </summary>

public enum FogState { Unknown, Scanning, Discovered, Visible }

public enum PassageType { Corridor, Duct, Vent, Rubble }

public enum RoomSize { Large, Medium, Small }

public enum GearType { Scanner, RubbleClearer }

public enum StationType { None, Refitting, Charging }

/// <summary>
/// Generic interaction condition on a passage (wall entity).
/// Model owns these; entities reflect them.
/// </summary>
public struct WallInteraction
{
    public GearType requiredGear;
    public float duration;
    public int energyCost;
    public string label;
    public bool blocksPassage;
    public PassageType resultingPassageType; // passage type after interaction completes
}
