using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Query interface for power state. Used by StationWallModel to check
/// if the owning room is powered without depending on the full network.
/// </summary>
public interface IPowerProvider
{
    bool IsRoomPowered(Vector2Int coord);

    /// <summary>
    /// Attempt to draw energy from the network. Returns actual amount drawn.
    /// May return less than requested if sources are depleted.
    /// </summary>
    float Draw(float amount);
}

/// <summary>
/// A single power source (battery) in the network.
/// </summary>
public class PowerSource
{
    public Vector2Int Room { get; private set; }
    public float MaxEnergy { get; private set; }
    public float CurrentEnergy { get; set; }

    public bool IsAlive => CurrentEnergy > 0f;
    public float EnergyFraction => MaxEnergy > 0f ? CurrentEnergy / MaxEnergy : 0f;

    public PowerSource(Vector2Int room, float maxEnergy)
    {
        Room = room;
        MaxEnergy = maxEnergy;
        CurrentEnergy = maxEnergy;
    }
}

/// <summary>
/// Runtime model for the power cable network.
/// Cables form a separate connection layer on hex edges (coexisting with passages).
/// Multiple power sources can exist; energy draw is round-robined across them.
/// Rooms connected to any live source via cables are "powered".
/// </summary>
public class PowerNetworkModel : IPowerProvider
{
    // ── Power sources ────────────────────────
    readonly List<PowerSource> sources = new List<PowerSource>();
    int roundRobinIndex;

    /// <summary>All power sources in the network.</summary>
    public IReadOnlyList<PowerSource> Sources => sources;

    /// <summary>True if any source still has energy.</summary>
    public bool IsActive
    {
        get
        {
            foreach (var s in sources)
                if (s.IsAlive) return true;
            return false;
        }
    }

    // ── Cable topology ───────────────────────
    readonly HashSet<long> cableEdges = new HashSet<long>();
    readonly Dictionary<Vector2Int, List<Vector2Int>> cableGraph = new Dictionary<Vector2Int, List<Vector2Int>>();

    // ── Connected rooms (recomputed on topology change) ──
    readonly HashSet<Vector2Int> connectedRooms = new HashSet<Vector2Int>();

    /// <summary>Fired when power state changes (source depletes or network topology changes).</summary>
    public event System.Action OnPowerStateChanged;

    // ── Construction ─────────────────────────

    public PowerNetworkModel() { }

    // ── Power source management ──────────────

    /// <summary>Add a power source (battery) at the given room.</summary>
    public PowerSource AddSource(Vector2Int room, float maxEnergy)
    {
        var source = new PowerSource(room, maxEnergy);
        sources.Add(source);
        RecomputeNetwork();
        return source;
    }

    // ── Cable management ─────────────────────

    /// <summary>Add a cable between two adjacent rooms.</summary>
    public void AddCable(Vector2Int roomA, Vector2Int roomB)
    {
        long key = MapModel.ConnKey(roomA, roomB);
        if (!cableEdges.Add(key)) return;

        if (!cableGraph.ContainsKey(roomA)) cableGraph[roomA] = new List<Vector2Int>();
        if (!cableGraph.ContainsKey(roomB)) cableGraph[roomB] = new List<Vector2Int>();
        cableGraph[roomA].Add(roomB);
        cableGraph[roomB].Add(roomA);

        RecomputeNetwork();
    }

    /// <summary>Remove a cable (e.g. broken). Returns true if it existed.</summary>
    public bool RemoveCable(Vector2Int roomA, Vector2Int roomB)
    {
        long key = MapModel.ConnKey(roomA, roomB);
        if (!cableEdges.Remove(key)) return false;

        if (cableGraph.ContainsKey(roomA)) cableGraph[roomA].Remove(roomB);
        if (cableGraph.ContainsKey(roomB)) cableGraph[roomB].Remove(roomA);

        RecomputeNetwork();
        return true;
    }

    /// <summary>Check if a cable exists between two rooms.</summary>
    public bool HasCable(Vector2Int roomA, Vector2Int roomB)
    {
        long key = MapModel.ConnKey(roomA, roomB);
        return cableEdges.Contains(key);
    }

    /// <summary>Get all cable edges as pairs of rooms.</summary>
    public IEnumerable<(Vector2Int, Vector2Int)> GetAllCables()
    {
        var visited = new HashSet<long>();
        foreach (var kvp in cableGraph)
        {
            foreach (var neighbor in kvp.Value)
            {
                long key = MapModel.ConnKey(kvp.Key, neighbor);
                if (visited.Add(key))
                    yield return (kvp.Key, neighbor);
            }
        }
    }

    // ── Power queries ────────────────────────

    /// <summary>A room is powered if it's in the connected set and any source is alive.</summary>
    public bool IsRoomPowered(Vector2Int coord)
    {
        return IsActive && connectedRooms.Contains(coord);
    }

    /// <summary>All rooms connected to the cable network.</summary>
    public IReadOnlyCollection<Vector2Int> ConnectedRooms => connectedRooms;

    // ── Draw energy ──────────────────────────

    /// <summary>
    /// Draw energy from the network. Round-robins across live sources.
    /// Returns the actual amount drawn (may be less if sources are depleted).
    /// </summary>
    public float Draw(float amount)
    {
        if (sources.Count == 0) return 0f;

        float remaining = amount;
        int attempts = 0;

        while (remaining > 0f && attempts < sources.Count)
        {
            roundRobinIndex = roundRobinIndex % sources.Count;
            var source = sources[roundRobinIndex];
            roundRobinIndex++;

            if (!source.IsAlive)
            {
                attempts++;
                continue;
            }

            float drawn = Mathf.Min(remaining, source.CurrentEnergy);
            source.CurrentEnergy -= drawn;
            remaining -= drawn;
            attempts = 0;

            if (!source.IsAlive && !IsActive)
            {
                OnPowerStateChanged?.Invoke();
            }
        }

        return amount - remaining;
    }

    // ── Network computation ──────────────────

    /// <summary>BFS from all source rooms through cable edges to find connected rooms.</summary>
    public void RecomputeNetwork()
    {
        connectedRooms.Clear();

        var queue = new Queue<Vector2Int>();

        // Seed BFS from all power source rooms
        foreach (var source in sources)
        {
            if (connectedRooms.Add(source.Room))
                queue.Enqueue(source.Room);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!cableGraph.TryGetValue(current, out var neighbors)) continue;

            foreach (var nb in neighbors)
            {
                if (connectedRooms.Add(nb))
                    queue.Enqueue(nb);
            }
        }

        OnPowerStateChanged?.Invoke();
    }
}
