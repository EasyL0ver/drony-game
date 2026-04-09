using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Pure game-logic model for the hex map: topology, spatial math, layout generation, pathfinding.
/// No MonoBehaviour, no meshes, no materials — just data and rules.
/// </summary>
public class MapModel
{
    // ── Configuration ────────────────────────

    public int RoomCount { get; private set; }
    public int Seed { get; private set; }
    public float HexRadius { get; private set; }
    public float GridScale { get; private set; }
    public float MediumScale { get; private set; }
    public float SmallScale { get; private set; }
    public float WallHeight { get; private set; }
    public float CorridorWidth { get; private set; }
    public float DuctWidth { get; private set; }
    public float VentPipeRadius { get; private set; }

    // ── Layout data (populated after Generate) ──

    public List<Vector2Int> RoomList { get; private set; } = new List<Vector2Int>();
    public Dictionary<Vector2Int, RoomSize> RoomSizes { get; private set; }
        = new Dictionary<Vector2Int, RoomSize>();
    public List<Connection> Connections { get; private set; } = new List<Connection>();

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

    public MapModel(int roomCount = 18, int seed = 42,
                    float hexRadius = 5f, float gridScale = 1.35f,
                    float mediumScale = 0.7f, float smallScale = 0.45f,
                    float wallHeight = 2.5f,
                    float corridorWidth = 1.8f, float ductWidth = 1.2f,
                    float ventPipeRadius = 0.22f)
    {
        RoomCount = roomCount;
        Seed = seed;
        HexRadius = hexRadius;
        GridScale = gridScale;
        MediumScale = mediumScale;
        SmallScale = smallScale;
        WallHeight = wallHeight;
        CorridorWidth = corridorWidth;
        DuctWidth = ductWidth;
        VentPipeRadius = ventPipeRadius;
    }

    // ── Layout generation ────────────────────

    public void GenerateLayout()
    {
        var rng = new System.Random(Seed);

        var rooms = new HashSet<Vector2Int>();
        var roomSizes = new Dictionary<Vector2Int, RoomSize>();
        var connections = new List<Connection>();
        var connSet = new HashSet<long>();
        var list = new List<Vector2Int>();

        rooms.Add(Vector2Int.zero);
        roomSizes[Vector2Int.zero] = RoomSize.Large;
        list.Add(Vector2Int.zero);

        int tries = 0;
        while (rooms.Count < RoomCount && tries < RoomCount * 50)
        {
            tries++;
            Vector2Int src = list[rng.Next(list.Count)];
            Vector2Int nb = src + HexDirs[rng.Next(6)];
            if (!rooms.Contains(nb))
            {
                rooms.Add(nb);
                RoomSize sz = RandomRoomSize(rng);
                roomSizes[nb] = sz;
                list.Add(nb);

                PassageType pt = DerivePassageType(roomSizes[src], sz);
                TryAddConn(connections, connSet, src, nb, pt);
            }
        }

        // Extra neighbor connections for loops
        foreach (var r in list)
        {
            for (int d = 0; d < 6; d++)
            {
                Vector2Int nb = r + HexDirs[d];
                if (rooms.Contains(nb) && rng.NextDouble() < 0.20)
                {
                    PassageType pt = DerivePassageType(roomSizes[r], roomSizes[nb]);
                    TryAddConn(connections, connSet, r, nb, pt);
                }
            }
        }

        RoomList = new List<Vector2Int>(rooms);
        RoomSizes = roomSizes;
        Connections = connections;
    }

    static RoomSize RandomRoomSize(System.Random rng)
    {
        double r = rng.NextDouble();
        if (r < 0.50) return RoomSize.Large;
        if (r < 0.80) return RoomSize.Medium;
        return RoomSize.Small;
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

    static void TryAddConn(List<Connection> list, HashSet<long> set,
                           Vector2Int a, Vector2Int b, PassageType type)
    {
        long k = ConnKey(a, b);
        if (set.Add(k))
            list.Add(new Connection { roomA = a, roomB = b, type = type });
    }

    public static long ConnKey(Vector2Int a, Vector2Int b)
    {
        if (a.x > b.x || (a.x == b.x && a.y > b.y))
        { var t = a; a = b; b = t; }
        long ax = a.x + 500, ay = a.y + 500;
        long bx = b.x + 500, by = b.y + 500;
        return (ax << 30) | (ay << 20) | (bx << 10) | by;
    }

    // ── Hex math ─────────────────────────────

    public Vector3 HexCenter(Vector2Int h)
    {
        float s = HexRadius * GridScale;
        float x = s * 1.5f * h.x;
        float z = s * Mathf.Sqrt(3f) * (h.y + h.x * 0.5f);
        return new Vector3(x, 0f, z);
    }

    public Vector3 Corner(Vector3 center, int i, float r)
    {
        float a = Mathf.Deg2Rad * 60f * i;
        return center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
    }

    public int EdgeToward(Vector2Int from, Vector2Int to)
    {
        Vector2Int d = to - from;
        for (int i = 0; i < 6; i++)
            if (HexDirs[i].x == d.x && HexDirs[i].y == d.y) return i;
        return 0;
    }

    public float RoomRadius(RoomSize s)
    {
        switch (s)
        {
            case RoomSize.Large:  return HexRadius;
            case RoomSize.Medium: return HexRadius * MediumScale;
            case RoomSize.Small:  return HexRadius * SmallScale;
            default:              return HexRadius;
        }
    }

    public float RoomWallHeight(RoomSize s)
    {
        switch (s)
        {
            case RoomSize.Large:  return WallHeight;
            case RoomSize.Medium: return WallHeight * 0.55f;
            case RoomSize.Small:  return WallHeight * 0.45f;
            default:              return WallHeight;
        }
    }

    public float PassageWidth(PassageType t)
    {
        switch (t)
        {
            case PassageType.Corridor: return CorridorWidth;
            case PassageType.Duct:     return DuctWidth;
            case PassageType.Vent:     return VentPipeRadius * 2f;
            default:                   return CorridorWidth;
        }
    }

    public float PassageWallHeight(PassageType t)
    {
        switch (t)
        {
            case PassageType.Corridor: return WallHeight * 0.88f;
            case PassageType.Duct:     return WallHeight * 0.38f;
            case PassageType.Vent:     return WallHeight * 0.65f;
            default:                   return WallHeight;
        }
    }

    public float PassageTopY(PassageType t) => PassageWallHeight(t);

    public float VentTopY(Vector2Int roomA, Vector2Int roomB)
    {
        float smallerWH = Mathf.Min(RoomWallHeight(RoomSizes[roomA]),
                                    RoomWallHeight(RoomSizes[roomB]));
        float pipeCenter = smallerWH * 0.5f;
        return pipeCenter + VentPipeRadius;
    }

    /// <summary>Returns world-space wall-exit midpoints for a passage between two rooms.</summary>
    public (Vector3 midA, Vector3 midB) PassageEndpoints(Vector2Int roomA, Vector2Int roomB)
    {
        int eA = EdgeToward(roomA, roomB);
        int eB = (eA + 3) % 6;
        Vector3 cA = HexCenter(roomA);
        Vector3 cB = HexCenter(roomB);
        float rA = RoomRadius(RoomSizes[roomA]);
        float rB = RoomRadius(RoomSizes[roomB]);
        Vector3 midA = (Corner(cA, eA, rA) + Corner(cA, (eA + 1) % 6, rA)) * 0.5f;
        Vector3 midB = (Corner(cB, eB, rB) + Corner(cB, (eB + 1) % 6, rB)) * 0.5f;
        return (midA, midB);
    }

    // ── Passage lookup ───────────────────────

    /// <summary>Returns the passage type between two adjacent rooms.</summary>
    public PassageType GetPassageType(Vector2Int from, Vector2Int to)
    {
        long key = ConnKey(from, to);
        foreach (var c in Connections)
        {
            if (ConnKey(c.roomA, c.roomB) == key)
                return c.type;
        }
        return PassageType.Corridor;
    }

    // ── Travel / energy costs ────────────────

    public static float TravelTime(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor: return 2f;
            case PassageType.Duct:     return 4f;
            case PassageType.Vent:     return 6f;
            default: return 2f;
        }
    }

    public static int StepEnergyCost(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor: return 1;
            case PassageType.Duct:     return 2;
            case PassageType.Vent:     return 3;
            default: return 1;
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
                                     Func<Vector2Int, FogState> getRoomState)
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

        // Destination always allowed (scouting)
        traversable.Add(to);

        // Build adjacency (only traversable rooms)
        var adj = new Dictionary<Vector2Int, List<(Vector2Int neighbor, float cost)>>();
        foreach (var room in traversable)
            adj[room] = new List<(Vector2Int, float)>();

        foreach (var c in Connections)
        {
            if (!traversable.Contains(c.roomA) || !traversable.Contains(c.roomB)) continue;
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
