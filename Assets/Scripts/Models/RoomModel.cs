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

    /// <summary>The type of station in this room, if any.</summary>
    public StationType Station { get; private set; } = StationType.None;

    /// <summary>Which edge (0-5) the station sits on. -1 if no station.</summary>
    public int StationEdge { get; private set; } = -1;

    /// <summary>What occupies each hex wall (6 edges). Passages tracked separately in Connections.</summary>
    StationType[] wallStations = new StationType[6];

    /// <summary>Place a station on a specific edge.</summary>
    public void SetWallStation(int edge, StationType type)
    {
        wallStations[edge] = type;
        Station = type;
        StationEdge = edge;
    }

    /// <summary>Get the station type on a specific edge.</summary>
    public StationType GetWallStation(int edge) => wallStations[edge];

    /// <summary>Convenience: true if this room has any station.</summary>
    public bool IsStation => Station != StationType.None;

    /// <summary>Legacy convenience accessor.</summary>
    public bool IsRefittingStation => Station == StationType.Refitting;
    public bool IsChargingStation => Station == StationType.Charging;

    // Scanning
    public float ScanDuration { get; set; } = 3f;
    public float ScanElapsed { get; private set; }
    public float ScanProgress => ScanDuration > 0 ? Mathf.Clamp01(ScanElapsed / ScanDuration) : 0f;
    public float ScanTimeLeft => State == FogState.Scanning
        ? Mathf.Max(0f, ScanDuration - ScanElapsed) : 0f;

    // Drone tracking
    public int DroneCount { get; private set; }

    // Connections to neighbors
    public List<RoomConnection> Connections { get; private set; } = new List<RoomConnection>();

    /// <summary>Fired whenever FogState changes. Args: (oldState, newState).</summary>
    public event Action<FogState, FogState> OnStateChanged;

    /// <summary>Fired when scanning completes (transitions from Scanning → Visible).</summary>
    public event Action OnScanComplete;

    // ── Constructor ──────────────────────────

    public RoomModel(Vector2Int coord, RoomSize size, float scanDuration = 3f)
    {
        Coord = coord;
        Size = size;
        ScanDuration = scanDuration;
    }

    // ── Connection management ────────────────

    public void AddConnection(RoomConnection conn)
    {
        Connections.Add(conn);
    }

    // ── Drone interaction ────────────────────

    /// <summary>A drone enters this room (starts heading toward it).</summary>
    public void OnDroneEnter()
    {
        DroneCount++;
    }

    /// <summary>
    /// A drone physically arrives in this room.
    /// Unknown rooms begin scanning only if canScan is true; Discovered rooms go to Visible.
    /// Returns true if scanning started.
    /// </summary>
    public bool OnDroneArrived(bool canScan = true)
    {
        switch (State)
        {
            case FogState.Unknown:
                if (!canScan) return false;
                ScanElapsed = 0f;
                SetState(FogState.Scanning);
                return true;
            case FogState.Scanning:
                return true; // already scanning
            case FogState.Discovered:
                SetState(FogState.Visible);
                return false;
            default:
                return false;
        }
    }

    /// <summary>A drone leaves this room. Demotes to Discovered when last drone leaves.</summary>
    public void OnDroneExit()
    {
        DroneCount = Mathf.Max(0, DroneCount - 1);
        if (DroneCount == 0 && State == FogState.Visible)
            SetState(FogState.Discovered);
    }

    /// <summary>Instantly reveal this room (e.g., starting base).</summary>
    public void RevealImmediate()
    {
        ScanElapsed = ScanDuration;
        SetState(FogState.Visible);
    }

    // ── Scan advancement ─────────────────────

    /// <summary>
    /// Advance scan by dt seconds. Returns true when scanning just completed.
    /// Only advances if state is Scanning and drones are present.
    /// </summary>
    public bool AdvanceScan(float dt)
    {
        if (State != FogState.Scanning || DroneCount <= 0) return false;

        ScanElapsed += dt;
        if (ScanElapsed >= ScanDuration)
        {
            ScanElapsed = ScanDuration;
            SetState(FogState.Visible);
            OnScanComplete?.Invoke();
            return true;
        }
        return false;
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
