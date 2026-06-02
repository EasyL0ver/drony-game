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

    // Wall interactions (rubble, etc.) — keyed by ConnKey (used during generation only)
    readonly Dictionary<long, WallInteractionConfig> wallInteractions = new Dictionary<long, WallInteractionConfig>();

    // Rooms that should have loot barrels placed (set by test maps)
    public List<Vector2Int> lootBarrelRooms { get; private set; } = new List<Vector2Int>();

    // Room for loading station (null = auto-pick)
    public Vector2Int? loadingStationRoom { get; private set; }

    // Room for charging station (null = auto-pick)
    public Vector2Int? chargingStationRoom { get; private set; }

    /// <summary>Read wall interaction from generation data. Used only during initial wiring.</summary>
    public WallInteractionConfig GetSeedWallInteraction(Vector2Int a, Vector2Int b)
    {
        long key = ConnKey(a, b);
        return wallInteractions.TryGetValue(key, out var wi) ? wi : null;
    }

    /// <summary>Read blocked state from generation data. Used only during initial wiring.</summary>
    public bool IsSeedBlocked(Vector2Int a, Vector2Int b)
    {
        long key = ConnKey(a, b);
        return wallInteractions.TryGetValue(key, out var wi) && wi.BlocksPassage;
    }

    // Room models — set after tile creation so WallModels become authoritative
    Dictionary<Vector2Int, RoomModel> rooms;

    /// <summary>
    /// Register room models so that IsBlocked/GetWallInteraction/GetPassageType
    /// delegate to WallModel as the single source of truth.
    /// </summary>
    public void RegisterRooms(Dictionary<Vector2Int, RoomModel> roomModels)
    {
        rooms = roomModels;
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

    /// <summary>
    /// Generate a test layout by index. Each index is a hand-crafted scenario.
    /// </summary>
    public void GenerateTestLayout(int index = 0)
    {
        wallInteractions.Clear();
        lootBarrelRooms.Clear();
        switch (index)
        {
            case 0: TestMap_CrookedVent(); break;
            case 1: TestMap_AllPassages(); break;
            case 2: TestMap_SalvageRun(); break;
            default: TestMap_CrookedVent(); break;
        }
    }

    /// <summary>Two rooms connected by a crooked vent.</summary>
    void TestMap_CrookedVent()
    {
        var roomA = Vector2Int.zero;
        var roomB = HexDirs[0];
        var roomC = HexDirs[3];

        RoomList = new List<Vector2Int> { roomA, roomB, roomC };
        RoomSizes = new Dictionary<Vector2Int, RoomSize>
        {
            { roomA, RoomSize.Large },
            { roomB, RoomSize.Small },
            { roomC, RoomSize.Small },
        };
        Connections = new List<Connection>
        {
            new Connection { roomA = roomA, roomB = roomB, type = PassageType.CrookedVent },
            new Connection { roomA = roomA, roomB = roomC, type = PassageType.Vent },
        };
    }

    /// <summary>Central room connected to all passage types + stations.</summary>
    void TestMap_AllPassages()
    {
        var roomA = Vector2Int.zero;
        var roomB = HexDirs[0]; // corridor
        var roomC = HexDirs[1]; // duct
        var roomD = HexDirs[2]; // vent
        var roomE = HexDirs[3]; // crooked vent
        var roomF = HexDirs[4]; // rubble

        RoomList = new List<Vector2Int> { roomA, roomB, roomC, roomD, roomE, roomF };
        RoomSizes = new Dictionary<Vector2Int, RoomSize>
        {
            { roomA, RoomSize.Large },
            { roomB, RoomSize.Large },
            { roomC, RoomSize.Medium },
            { roomD, RoomSize.Small },
            { roomE, RoomSize.Small },
            { roomF, RoomSize.Large },
        };
        Connections = new List<Connection>
        {
            new Connection { roomA = roomA, roomB = roomB, type = PassageType.Corridor },
            new Connection { roomA = roomA, roomB = roomC, type = PassageType.Duct },
            new Connection { roomA = roomA, roomB = roomD, type = PassageType.Vent },
            new Connection { roomA = roomA, roomB = roomE, type = PassageType.CrookedVent },
            new Connection { roomA = roomA, roomB = roomF, type = PassageType.Rubble },
        };

        wallInteractions[ConnKey(roomA, roomF)] = WallInteractionConfig.RubbleClear(GearType.Bomb);
    }

    /// <summary>
    /// Salvage Run — a multi-room map designed to use all mechanics:
    /// corridors for hauler, rubble to bomb-clear, vents/ducts for scouts,
    /// multiple loot barrels, charging/loading stations, energy management.
    /// Win condition: sell 10 pts of cargo at the loading station.
    /// </summary>
    void TestMap_SalvageRun()
    {
        // Room coordinates
        var hub     = Vector2Int.zero;                         // start — refit station
        var east    = new Vector2Int(1, 0);                    // charging station
        var farEast = new Vector2Int(2, 0);                    // barrel #1
        var south   = new Vector2Int(0, -1);                   // loading station (sell)
        var sEast   = new Vector2Int(1, -1);                   // barrel #2
        var north   = new Vector2Int(0, 1);                    // scout peek room
        var nWest   = new Vector2Int(-1, 1);                   // dead end (empty)
        var west    = new Vector2Int(-1, 0);                   // behind rubble
        var farWest = new Vector2Int(-2, 0);                   // barrel #3
        var fwNorth = new Vector2Int(-2, 1);                   // barrel #4

        RoomList = new List<Vector2Int>
        {
            hub, east, farEast, south, sEast, north, nWest, west, farWest, fwNorth
        };

        RoomSizes = new Dictionary<Vector2Int, RoomSize>
        {
            { hub,     RoomSize.Large },
            { east,    RoomSize.Large },
            { farEast, RoomSize.Medium },
            { south,   RoomSize.Large },
            { sEast,   RoomSize.Medium },
            { north,   RoomSize.Small },
            { nWest,   RoomSize.Small },
            { west,    RoomSize.Large },
            { farWest, RoomSize.Medium },
            { fwNorth, RoomSize.Medium },
        };

        Connections = new List<Connection>
        {
            // Hauler highway (corridors)
            new Connection { roomA = hub,     roomB = east,    type = PassageType.Corridor },
            new Connection { roomA = east,    roomB = farEast, type = PassageType.Corridor },
            new Connection { roomA = hub,     roomB = south,   type = PassageType.Corridor },
            new Connection { roomA = south,   roomB = sEast,   type = PassageType.Corridor },
            // Bombed path opens west wing
            new Connection { roomA = hub,     roomB = west,    type = PassageType.Rubble },
            new Connection { roomA = west,    roomB = farWest, type = PassageType.Corridor },
            new Connection { roomA = farWest, roomB = fwNorth, type = PassageType.Corridor },
            // Scout-only paths
            new Connection { roomA = hub,     roomB = north,   type = PassageType.Duct },
            new Connection { roomA = north,   roomB = nWest,   type = PassageType.Vent },
            // Shortcut vent from north to west (scouts can peek behind rubble)
            new Connection { roomA = nWest,   roomB = west,    type = PassageType.Vent },
        };

        // Rubble interaction
        wallInteractions[ConnKey(hub, west)] = WallInteractionConfig.RubbleClear(GearType.Bomb);

        // Barrel placements
        lootBarrelRooms.Add(farEast);
        lootBarrelRooms.Add(sEast);
        lootBarrelRooms.Add(farWest);
        lootBarrelRooms.Add(fwNorth);

        // Station placements
        chargingStationRoom = east;
        loadingStationRoom = south;
    }

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

        // Randomly convert some vents to crooked vents
        for (int i = 0; i < Connections.Count; i++)
        {
            var c = Connections[i];
            if (c.type == PassageType.Vent && rng.NextDouble() < 0.4)
            {
                c.type = PassageType.CrookedVent;
                Connections[i] = c;
            }
        }

        // Randomly convert some corridors/ducts to rubble
        wallInteractions.Clear();
        for (int i = 0; i < Connections.Count; i++)
        {
            var c = Connections[i];
            if (c.type == PassageType.Vent || c.type == PassageType.CrookedVent) continue;
            if (c.roomA == Vector2Int.zero || c.roomB == Vector2Int.zero) continue;
            if (rng.NextDouble() < 0.25)
            {
                var originalType = c.type;
                c.type = PassageType.Rubble;
                Connections[i] = c;
                var interaction = WallInteractionConfig.RubbleClear(GearType.Bomb);
                interaction.ResultingPassageType = originalType;
                wallInteractions[ConnKey(c.roomA, c.roomB)] = interaction;
            }
        }
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

    /// <summary>
    /// Returns the hex edge index (0-5) nearest to a world-space point
    /// relative to a given hex cell. Uses angle from hex center.
    /// </summary>
    public int NearestEdge(Vector3 worldPoint, Vector2Int coord)
    {
        Vector3 center = HexCenter(coord);
        float dx = worldPoint.x - center.x;
        float dz = worldPoint.z - center.z;
        float angle = Mathf.Atan2(dz, dx) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        return Mathf.FloorToInt(angle / 60f) % 6;
    }

    /// <summary>
    /// Returns the world-space midpoint of a hex wall edge for a given room.
    /// </summary>
    public Vector3 WallMidpoint(Vector2Int coord, int edge, RoomSize size)
    {
        Vector3 center = HexCenter(coord);
        float r = RoomRadius(size);
        Vector3 c0 = Corner(center, edge, r);
        Vector3 c1 = Corner(center, (edge + 1) % 6, r);
        return (c0 + c1) * 0.5f;
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
            case PassageType.Corridor:    return CorridorWidth;
            case PassageType.Rubble:      return CorridorWidth;
            case PassageType.Duct:        return DuctWidth;
            case PassageType.Vent:        return VentPipeRadius * 2f;
            case PassageType.CrookedVent: return VentPipeRadius * 2f;
            default:                      return CorridorWidth;
        }
    }

    public float PassageWallHeight(PassageType t)
    {
        switch (t)
        {
            case PassageType.Corridor:    return WallHeight * 0.88f;
            case PassageType.Rubble:      return WallHeight * 0.88f;
            case PassageType.Duct:        return WallHeight * 0.38f;
            case PassageType.Vent:        return WallHeight * 0.65f;
            case PassageType.CrookedVent: return WallHeight * 0.65f;
            default:                      return WallHeight;
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

    /// <summary>Get the WallModel for the edge from room 'a' toward room 'b', or null.</summary>
    WallModel GetWall(Vector2Int a, Vector2Int b)
    {
        if (rooms == null || !rooms.TryGetValue(a, out var room)) return null;
        int edge = EdgeToward(a, b);
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
        if (wall is CorridorWallModel cw) return cw.IsBlocked;
        return false;
    }

    public WallInteractionConfig GetWallInteraction(Vector2Int a, Vector2Int b, DroneModel drone)
    {
        var wall = GetWall(a, b);
        if (wall == null) return null;
        var interactions = wall.GetInteractions(drone);
        return interactions.Count > 0 ? interactions[0] : null;
    }

    /// <summary>Check if a connection has any rubble (no drone context needed — structural query for spawning).</summary>
    public bool HasBlockingInteraction(Vector2Int a, Vector2Int b)
    {
        var wall = GetWall(a, b);
        if (wall is CorridorWallModel cw) return cw.IsBlocked;
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
        (wallAB as CorridorWallModel)?.CompleteInteraction();
        (wallBA as CorridorWallModel)?.CompleteInteraction();

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
