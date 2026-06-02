using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Pure game-logic model for the hex map: topology, layout state, pathfinding.
/// No MonoBehaviour, no meshes, no materials — just data and rules.
/// </summary>
public class MapModel
{
    // ── Configuration ────────────────────────

    public int RoomCount { get; private set; }
    public int Seed { get; private set; }

    // ── Layout data ──────────────────────────

    public List<Vector2Int> RoomList { get; private set; } = new List<Vector2Int>();
    public Dictionary<Vector2Int, RoomSize> RoomSizes { get; private set; }
        = new Dictionary<Vector2Int, RoomSize>();
    public List<Connection> Connections { get; private set; } = new List<Connection>();

    // Seed data (consumed during RegisterRooms, then discarded)
    Dictionary<long, WallInteractionConfig> wallInteractions = new Dictionary<long, WallInteractionConfig>();

    public List<Vector2Int> lootBarrelRooms { get; private set; } = new List<Vector2Int>();
    public Vector2Int? loadingStationRoom { get; private set; }
    public Vector2Int? chargingStationRoom { get; private set; }
    public List<CableConnection> CableConnections { get; private set; } = new List<CableConnection>();
    public Vector2Int? batteryRoom { get; private set; }
    public int batteryMaxEnergy { get; private set; } = 100;

    /// <summary>A cable edge between two adjacent rooms.</summary>
    public struct CableConnection
    {
        public Vector2Int roomA;
        public Vector2Int roomB;
    }

    /// <summary>Apply a generated layout to this model.</summary>
    public void ApplyLayout(MapLayout layout)
    {
        RoomList = layout.RoomList;
        RoomSizes = layout.RoomSizes;
        Connections = layout.Connections;
        wallInteractions = layout.WallInteractions;
        lootBarrelRooms = layout.LootBarrelRooms;
        loadingStationRoom = layout.LoadingStationRoom;
        chargingStationRoom = layout.ChargingStationRoom;
        CableConnections = layout.CableConnections;
        batteryRoom = layout.BatteryRoom;
        batteryMaxEnergy = layout.BatteryMaxEnergy;
    }

    /// <summary>Read wall interaction from generation data. Used only during initial wiring.</summary>
    public WallInteractionConfig GetSeedWallInteraction(Vector2Int a, Vector2Int b)
    {
        long key = ConnKey(a, b);
        return wallInteractions.TryGetValue(key, out var wi) ? wi : null;
    }

    // Room models — set after tile creation so WallModels become authoritative
    Dictionary<Vector2Int, RoomModel> rooms;

    /// <summary>
    /// Register room models and wire wall connections between them.
    /// Creates appropriate wall model subclasses (ObstacleWallModel, etc.) based on connection data.
    /// </summary>
    public void RegisterRooms(Dictionary<Vector2Int, RoomModel> roomModels)
    {
        rooms = roomModels;

        foreach (var conn in Connections)
        {
            if (!rooms.TryGetValue(conn.roomA, out var roomA)) continue;
            if (!rooms.TryGetValue(conn.roomB, out var roomB)) continue;

            Vector2Int delta = conn.roomB - conn.roomA;
            int edgeAB = Array.FindIndex(HexDirs, dir => dir == delta);
            if (edgeAB < 0) edgeAB = 0;
            int edgeBA = (edgeAB + 3) % 6;

            var wi = GetSeedWallInteraction(conn.roomA, conn.roomB);
            CorridorWallModel wallAB, wallBA;

            if (conn.type == PassageType.BlastDoor)
            {
                var behaviorA = new BlastDoorBehavior();
                var behaviorB = new BlastDoorBehavior();
                wallAB = new ObstacleWallModel<BlastDoorBehavior>(roomA, edgeAB, conn.type, behaviorA);
                wallBA = new ObstacleWallModel<BlastDoorBehavior>(roomB, edgeBA, conn.type, behaviorB);
                roomA.SetWall(edgeAB, wallAB);
                roomB.SetWall(edgeBA, wallBA);
            }
            else if (wi != null && wi.BlocksPassage)
            {
                var behaviorA = new RubbleBehavior(wi);
                var behaviorB = new RubbleBehavior(wi);
                wallAB = new ObstacleWallModel<RubbleBehavior>(roomA, edgeAB, conn.type, behaviorA);
                wallBA = new ObstacleWallModel<RubbleBehavior>(roomB, edgeBA, conn.type, behaviorB);
                roomA.SetWall(edgeAB, wallAB);
                roomB.SetWall(edgeBA, wallBA);
            }
            else
            {
                wallAB = roomA.Walls[edgeAB] as CorridorWallModel;
                wallBA = roomB.Walls[edgeBA] as CorridorWallModel;
            }

            wallAB.Neighbor = wallBA;
            wallAB.PassageType = conn.type;
            wallBA.Neighbor = wallAB;
            wallBA.PassageType = conn.type;
        }
    }

    /// <summary>One directional passage between two rooms.</summary>
    public struct Connection
    {
        public Vector2Int roomA;
        public Vector2Int roomB;
        public PassageType type;
    }

    // Flat-top hex: 6 axial neighbor directions
    public static readonly Vector2Int[] HexDirs =
    {
        new Vector2Int( 1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int(-1,  1),
        new Vector2Int(-1,  0),
        new Vector2Int( 0, -1),
        new Vector2Int( 1, -1),
    };

    // ── Constructor ──────────────────────────

    public MapModel(int roomCount = 18, int seed = 42)
    {
        RoomCount = roomCount;
        Seed = seed;
    }

    public static long ConnKey(Vector2Int a, Vector2Int b)
    {
        if (a.x > b.x || (a.x == b.x && a.y > b.y))
        { var t = a; a = b; b = t; }
        long ax = a.x + 500, ay = a.y + 500;
        long bx = b.x + 500, by = b.y + 500;
        return (ax << 30) | (ay << 20) | (bx << 10) | by;
    }

    /// <summary>Passage type determined by smallest room on either end.</summary>
    public static PassageType DerivePassageType(RoomSize a, RoomSize b)
    {
        RoomSize smallest = (RoomSize)Mathf.Max((int)a, (int)b);
        switch (smallest)
        {
            case RoomSize.Large:  return PassageType.Corridor;
            case RoomSize.Medium: return PassageType.Duct;
            case RoomSize.Small:  return PassageType.Vent;
            default:              return PassageType.Corridor;
        }
    }

    // ── Passage lookup ───────────────────────

    /// <summary>Get the WallModel for the edge from room 'a' toward room 'b', or null.</summary>
    public WallModel GetWall(Vector2Int a, Vector2Int b)
    {
        if (rooms == null || !rooms.TryGetValue(a, out var room)) return null;
        Vector2Int delta = b - a;
        int edge = Array.FindIndex(HexDirs, dir => dir == delta);
        if (edge < 0) edge = 0;
        return room.Walls[edge];
    }

    /// <summary>Returns passability info for a drone at the wall between two rooms.</summary>
    public WallPassability GetPassability(Vector2Int from, Vector2Int to, DroneModel drone)
    {
        var wall = GetWall(from, to);
        if (wall == null) return WallPassability.Blocked;
        return wall.GetPassability(drone);
    }

    /// <summary>Returns the passage type between two rooms (for legacy/display use).</summary>
    public PassageType GetPassageType(Vector2Int from, Vector2Int to)
    {
        var wall = GetWall(from, to);
        if (wall is CorridorWallModel cw) return cw.PassageType;
        return PassageType.Corridor;
    }

    public bool IsBlocked(Vector2Int a, Vector2Int b)
    {
        var wall = GetWall(a, b);
        if (wall is IObstacleWall obs) return obs.IsBlocking;
        return false;
    }

    public WallInteractionConfig GetWallInteraction(Vector2Int a, Vector2Int b, DroneModel drone)
    {
        var wall = GetWall(a, b);
        if (wall == null) return null;
        var interactions = wall.GetInteractions(drone);
        return interactions.Count > 0 ? interactions[0] : null;
    }

    /// <summary>Check if a connection has any obstacle (structural query for spawning).</summary>
    public bool HasBlockingInteraction(Vector2Int a, Vector2Int b)
    {
        var wall = GetWall(a, b);
        if (wall is IObstacleWall obs) return obs.IsBlocking;
        return false;
    }

    /// <summary>
    /// Complete a wall interaction: remove it and apply its resulting passage type.
    /// Updates both the legacy data structures and WallModels.
    /// Returns true if an interaction was found and completed.
    /// </summary>
    public bool CompleteWallInteraction(Vector2Int a, Vector2Int b)
    {
        // Update WallModels (authoritative source)
        var wallAB = GetWall(a, b);
        var wallBA = GetWall(b, a);
        (wallAB as IObstacleWall)?.CompleteInteraction();
        (wallBA as IObstacleWall)?.CompleteInteraction();

        // Update legacy data structures
        long key = ConnKey(a, b);
        wallInteractions.Remove(key);

        for (int i = 0; i < Connections.Count; i++)
        {
            if (ConnKey(Connections[i].roomA, Connections[i].roomB) == key)
            {
                var c = Connections[i];
                c.type = (wallAB is CorridorWallModel cwAB) ? cwAB.PassageType : PassageType.Corridor;
                Connections[i] = c;
                break;
            }
        }
        return true;
    }

    // ── Travel / energy costs ────────────────

    public static float TravelTime(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor:    return 2f;
            case PassageType.Rubble:      return 2f;
            case PassageType.BlastDoor:   return 2f;
            case PassageType.Duct:        return 4f;
            case PassageType.Vent:        return 6f;
            case PassageType.CrookedVent: return 8f;
            default: return 2f;
        }
    }

    public static int StepEnergyCost(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor:    return 1;
            case PassageType.Rubble:      return 1;
            case PassageType.BlastDoor:   return 1;
            case PassageType.Duct:        return 2;
            case PassageType.Vent:        return 3;
            case PassageType.CrookedVent: return 4;
            default: return 1;
        }
    }

    public static string PassageLabel(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor:    return "CORRIDOR";
            case PassageType.Rubble:      return "RUBBLE";
            case PassageType.BlastDoor:   return "BLAST DOOR";
            case PassageType.Duct:        return "DUCT";
            case PassageType.Vent:        return "VENT";
            case PassageType.CrookedVent: return "CROOKED VENT";
            default:                      return "TRAVEL";
        }
    }

    public const int ScanEnergyCost = 2;

    // ── Pathfinding (Dijkstra) ───────────────

    /// <summary>
    /// Fog-aware pathfinding. Returns room sequence (excluding start) or null if unreachable.
    /// </summary>
    /// <param name="from">Start room (always traversable).</param>
    /// <param name="to">Destination (always allowed).</param>
    /// <param name="getRoomState">Returns FogState for a given room coord.</param>
    public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to,
                                     Func<Vector2Int, FogState> getRoomState,
                                     DroneModel drone = null)
    {
        if (from == to) return null;

        // Classify rooms
        var knownRooms = new HashSet<Vector2Int>();
        var unknownRooms = new HashSet<Vector2Int>();

        foreach (var room in RoomList)
        {
            FogState state = getRoomState(room);
            if (state != FogState.Unknown)
                knownRooms.Add(room);
            else
                unknownRooms.Add(room);
        }

        var traversable = new HashSet<Vector2Int>(knownRooms);

        // Inferred rooms: unknown but connected to 2+ known rooms
        foreach (var room in unknownRooms)
        {
            int knownNeighbors = 0;
            foreach (var c in Connections)
            {
                if (c.roomA == room && knownRooms.Contains(c.roomB)) knownNeighbors++;
                else if (c.roomB == room && knownRooms.Contains(c.roomA)) knownNeighbors++;
                if (knownNeighbors >= 2) break;
            }
            if (knownNeighbors >= 2)
                traversable.Add(room);
        }

        // Start always traversable (drone is already there)
        traversable.Add(from);
        // Destination always allowed (scouting)
        traversable.Add(to);

        // Build adjacency (only traversable rooms)
        var adj = new Dictionary<Vector2Int, List<(Vector2Int neighbor, float cost)>>();
        foreach (var room in traversable)
            adj[room] = new List<(Vector2Int, float)>();

        foreach (var c in Connections)
        {
            if (!traversable.Contains(c.roomA) || !traversable.Contains(c.roomB)) continue;
            if (IsBlocked(c.roomA, c.roomB)) continue;
            if (drone != null && !drone.CanTraverse(c.type)) continue;
            float cost = TravelTime(c.type);
            adj[c.roomA].Add((c.roomB, cost));
            adj[c.roomB].Add((c.roomA, cost));
        }

        // Dijkstra
        var dist = new Dictionary<Vector2Int, float>();
        var prev = new Dictionary<Vector2Int, Vector2Int?>();
        var visited = new HashSet<Vector2Int>();
        var open = new List<(float cost, Vector2Int room)>();

        foreach (var room in traversable)
        {
            dist[room] = float.MaxValue;
            prev[room] = null;
        }

        dist[from] = 0f;
        open.Add((0f, from));

        while (open.Count > 0)
        {
            int minIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].cost < open[minIdx].cost) minIdx = i;

            var (curCost, cur) = open[minIdx];
            open.RemoveAt(minIdx);

            if (visited.Contains(cur)) continue;
            visited.Add(cur);
            if (cur == to) break;

            if (!adj.ContainsKey(cur)) continue;
            foreach (var (neighbor, edgeCost) in adj[cur])
            {
                if (visited.Contains(neighbor)) continue;
                float nd = curCost + edgeCost;
                if (nd < dist[neighbor])
                {
                    dist[neighbor] = nd;
                    prev[neighbor] = cur;
                    open.Add((nd, neighbor));
                }
            }
        }

        if (!prev.ContainsKey(to) || prev[to] == null) return null;

        var result = new List<Vector2Int>();
        var step = to;
        while (step != from)
        {
            result.Add(step);
            step = prev[step].Value;
        }
        result.Reverse();
        return result;
    }
}
