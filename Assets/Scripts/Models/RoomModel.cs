using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Pure game-logic model for a single room.
/// Owns fog state, scan progress, drone tracking, and connections.
/// No MonoBehaviour, no visuals.
/// </summary>
public class RoomModel
{
    public Vector2Int Coord { get; private set; }
    public RoomSize Size { get; private set; }
    public FogState State { get; private set; } = FogState.Unknown;

    /// <summary>The 6 wall models for this room's hex edges.</summary>
    public WallModel[] Walls { get; private set; }

    // Drone tracking
    public HashSet<DroneModel> Drones { get; private set; } = new HashSet<DroneModel>();
    public int DroneCount => Drones.Count;

    // Connections to neighbors
    public List<RoomConnection> Connections { get; private set; } = new List<RoomConnection>();

    /// <summary>Fired whenever FogState changes. Args: (oldState, newState).</summary>
    public event Action<FogState, FogState> OnStateChanged;

    // ── Constructor ──────────────────────────

    public RoomModel(Vector2Int coord, RoomSize size)
    {
        Coord = coord;
        Size = size;

        Walls = new WallModel[6];
        for (int i = 0; i < 6; i++)
            Walls[i] = new CorridorWallModel(this, i);
    }

    // ── Wall management ────────────────────

    /// <summary>Replace the wall at the given edge with a new model (e.g. StationWallModel).</summary>
    public void SetWall(int edge, WallModel wall)
    {
        // Preserve neighbor link
        var oldNeighbor = Walls[edge].Neighbor;
        Walls[edge] = wall;
        wall.Neighbor = oldNeighbor;
        if (oldNeighbor != null)
            oldNeighbor.Neighbor = wall;
    }

    // ── Connection management ────────────────

    public void AddConnection(RoomConnection conn)
    {
        Connections.Add(conn);
    }

    // ── Drone interaction ────────────────────

    /// <summary>A drone enters this room (starts heading toward it).</summary>
    public void OnDroneEnter(DroneModel drone)
    {
        Drones.Add(drone);
    }

    /// <summary>
    /// A drone physically arrives in this room.
    /// Discovered rooms go to Visible.
    /// </summary>
    public void OnDroneArrived()
    {
        if (State == FogState.Discovered)
            SetState(FogState.Visible);
    }

    /// <summary>Begin scanning (sets state to Scanning for visual feedback).</summary>
    public void BeginScan()
    {
        if (State == FogState.Unknown)
            SetState(FogState.Scanning);
    }

    /// <summary>Complete scanning (transitions to Visible).</summary>
    public void CompleteScan()
    {
        SetState(FogState.Visible);
    }

    /// <summary>A drone leaves this room. Demotes to Discovered when last drone leaves.</summary>
    public void OnDroneExit(DroneModel drone)
    {
        Drones.Remove(drone);
        if (Drones.Count == 0 && State == FogState.Visible)
            SetState(FogState.Discovered);
    }

    /// <summary>Instantly reveal this room (e.g., starting base).</summary>
    public void RevealImmediate()
    {
        SetState(FogState.Visible);
    }

    // ── State management ─────────────────────

    void SetState(FogState newState)
    {
        if (State == newState) return;
        var old = State;
        State = newState;
        OnStateChanged?.Invoke(old, newState);
    }
}

/// <summary>
/// A connection from one room to a neighbor through a passage.
/// </summary>
[System.Serializable]
public class RoomConnection
{
    public RoomModel neighbor;
    public PassageType passageType;
    public int edgeIndex; // which hex edge (0-5)
}
