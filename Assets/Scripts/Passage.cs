using UnityEngine;

/// <summary>
/// Wall entity representing a corridor/duct/vent passage on one side of a room.
/// Placed at the wall midpoint, facing into the room (+Z = inward).
/// Each connection spawns two Passage instances (one per room).
/// </summary>
public class Passage : WallEntity
{
    public override float ParkOffset => 0.5f;

    public PassageType Type { get; private set; }
    public Vector2Int Room { get; private set; }
    public Vector2Int Neighbor { get; private set; }
    public int Edge { get; private set; }

    public void Init(Vector2Int room, Vector2Int neighbor, int edge, PassageType type)
    {
        Room = room;
        Neighbor = neighbor;
        Edge = edge;
        Type = type;
    }

    public void UpdateType(PassageType newType)
    {
        Type = newType;
    }
}
