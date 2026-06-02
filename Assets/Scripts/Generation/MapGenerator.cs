using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Builder pattern for constructing map layouts step by step.
/// </summary>
public class MapBuilder
{
    readonly MapLayout _layout = new MapLayout();

    public MapBuilder AddRoom(Vector2Int coord, RoomSize size)
    {
        _layout.RoomList.Add(coord);
        _layout.RoomSizes[coord] = size;
        return this;
    }

    public MapBuilder AddConnection(Vector2Int a, Vector2Int b, PassageType type)
    {
        _layout.Connections.Add(new MapModel.Connection { roomA = a, roomB = b, type = type });
        return this;
    }

    public MapBuilder AddObstacle(Vector2Int a, Vector2Int b, WallInteractionConfig interaction)
    {
        _layout.WallInteractions[MapModel.ConnKey(a, b)] = interaction;
        return this;
    }

    public MapBuilder AddLootBarrel(Vector2Int room)
    {
        _layout.LootBarrelRooms.Add(room);
        return this;
    }

    public MapBuilder SetChargingStation(Vector2Int room)
    {
        _layout.ChargingStationRoom = room;
        return this;
    }

    public MapBuilder SetLoadingStation(Vector2Int room)
    {
        _layout.LoadingStationRoom = room;
        return this;
    }

    public MapBuilder SetBattery(Vector2Int room, int maxEnergy)
    {
        _layout.BatteryRoom = room;
        _layout.BatteryMaxEnergy = maxEnergy;
        return this;
    }

    public MapBuilder AddCable(Vector2Int a, Vector2Int b)
    {
        _layout.CableConnections.Add(new MapModel.CableConnection { roomA = a, roomB = b });
        return this;
    }

    public MapLayout Build() => _layout;
}

/// <summary>
/// Output of map generation — consumed by MapModel.ApplyLayout.
/// </summary>
public class MapLayout
{
    public List<Vector2Int> RoomList = new List<Vector2Int>();
    public Dictionary<Vector2Int, RoomSize> RoomSizes = new Dictionary<Vector2Int, RoomSize>();
    public List<MapModel.Connection> Connections = new List<MapModel.Connection>();
    public Dictionary<long, WallInteractionConfig> WallInteractions = new Dictionary<long, WallInteractionConfig>();
    public List<Vector2Int> LootBarrelRooms = new List<Vector2Int>();
    public Vector2Int? LoadingStationRoom;
    public Vector2Int? ChargingStationRoom;
    public List<MapModel.CableConnection> CableConnections = new List<MapModel.CableConnection>();
    public Vector2Int? BatteryRoom;
    public int BatteryMaxEnergy = 100;
}

/// <summary>
/// Static factory methods that use MapBuilder to produce layouts.
/// </summary>
public static class MapGenerator
{
    public static MapLayout GenerateTestLayout(int index)
    {
        switch (index)
        {
            case 0: return TestMap_CrookedVent();
            case 1: return TestMap_AllPassages();
            case 2: return TestMap_SalvageRun();
            default: return TestMap_CrookedVent();
        }
    }

    public static MapLayout GenerateRandom(int roomCount, int seed)
    {
        var rng = new System.Random(seed);
        var builder = new MapBuilder();

        var rooms = new HashSet<Vector2Int>();
        var connSet = new HashSet<long>();
        var list = new List<Vector2Int>();

        rooms.Add(Vector2Int.zero);
        builder.AddRoom(Vector2Int.zero, RoomSize.Large);
        list.Add(Vector2Int.zero);

        var roomSizes = new Dictionary<Vector2Int, RoomSize> { { Vector2Int.zero, RoomSize.Large } };

        int tries = 0;
        while (rooms.Count < roomCount && tries < roomCount * 50)
        {
            tries++;
            Vector2Int src = list[rng.Next(list.Count)];
            Vector2Int nb = src + MapModel.HexDirs[rng.Next(6)];
            if (!rooms.Contains(nb))
            {
                rooms.Add(nb);
                RoomSize sz = RandomRoomSize(rng);
                roomSizes[nb] = sz;
                builder.AddRoom(nb, sz);
                list.Add(nb);

                PassageType pt = MapModel.DerivePassageType(roomSizes[src], sz);
                TryAddConn(builder, connSet, src, nb, pt);
            }
        }

        // Extra neighbor connections for loops
        foreach (var r in list)
        {
            for (int d = 0; d < 6; d++)
            {
                Vector2Int nb = r + MapModel.HexDirs[d];
                if (rooms.Contains(nb) && rng.NextDouble() < 0.20)
                {
                    PassageType pt = MapModel.DerivePassageType(roomSizes[r], roomSizes[nb]);
                    TryAddConn(builder, connSet, r, nb, pt);
                }
            }
        }

        var layout = builder.Build();

        // Randomly convert some vents to crooked vents
        for (int i = 0; i < layout.Connections.Count; i++)
        {
            var c = layout.Connections[i];
            if (c.type == PassageType.Vent && rng.NextDouble() < 0.4)
            {
                c.type = PassageType.CrookedVent;
                layout.Connections[i] = c;
            }
        }

        // Randomly convert some corridors/ducts to rubble
        for (int i = 0; i < layout.Connections.Count; i++)
        {
            var c = layout.Connections[i];
            if (c.type == PassageType.Vent || c.type == PassageType.CrookedVent) continue;
            if (c.roomA == Vector2Int.zero || c.roomB == Vector2Int.zero) continue;
            if (rng.NextDouble() < 0.25)
            {
                var originalType = c.type;
                c.type = PassageType.Rubble;
                layout.Connections[i] = c;
                var interaction = WallInteractionConfig.RubbleClear(GearType.Bomb);
                interaction.ResultingPassageType = originalType;
                layout.WallInteractions[MapModel.ConnKey(c.roomA, c.roomB)] = interaction;
            }
        }

        // Randomly place blast doors on some remaining corridors/ducts
        for (int i = 0; i < layout.Connections.Count; i++)
        {
            var c = layout.Connections[i];
            if (c.type != PassageType.Corridor && c.type != PassageType.Duct) continue;
            if (c.roomA == Vector2Int.zero || c.roomB == Vector2Int.zero) continue;
            if (rng.NextDouble() < 0.15)
            {
                c.type = PassageType.BlastDoor;
                layout.Connections[i] = c;
            }
        }

        return layout;
    }

    // ── Test maps ────────────────────────────

    static MapLayout TestMap_CrookedVent()
    {
        var d = MapModel.HexDirs;
        return new MapBuilder()
            .AddRoom(Vector2Int.zero, RoomSize.Large)
            .AddRoom(d[0], RoomSize.Small)
            .AddRoom(d[3], RoomSize.Small)
            .AddConnection(Vector2Int.zero, d[0], PassageType.CrookedVent)
            .AddConnection(Vector2Int.zero, d[3], PassageType.Vent)
            .Build();
    }

    static MapLayout TestMap_AllPassages()
    {
        var d = MapModel.HexDirs;
        return new MapBuilder()
            .AddRoom(Vector2Int.zero, RoomSize.Large)
            .AddRoom(d[0], RoomSize.Large)
            .AddRoom(d[1], RoomSize.Medium)
            .AddRoom(d[2], RoomSize.Small)
            .AddRoom(d[3], RoomSize.Small)
            .AddRoom(d[4], RoomSize.Large)
            .AddConnection(Vector2Int.zero, d[0], PassageType.Corridor)
            .AddConnection(Vector2Int.zero, d[1], PassageType.Duct)
            .AddConnection(Vector2Int.zero, d[2], PassageType.Vent)
            .AddConnection(Vector2Int.zero, d[3], PassageType.CrookedVent)
            .AddConnection(Vector2Int.zero, d[4], PassageType.Rubble)
            .AddObstacle(Vector2Int.zero, d[4], WallInteractionConfig.RubbleClear(GearType.Bomb))
            .Build();
    }

    static MapLayout TestMap_SalvageRun()
    {
        var hub     = Vector2Int.zero;
        var east    = new Vector2Int(1, 0);
        var farEast = new Vector2Int(2, 0);
        var south   = new Vector2Int(0, -1);
        var sEast   = new Vector2Int(1, -1);
        var north   = new Vector2Int(0, 1);
        var nWest   = new Vector2Int(-1, 1);
        var west    = new Vector2Int(-1, 0);
        var farWest = new Vector2Int(-2, 0);
        var fwNorth = new Vector2Int(-2, 1);

        return new MapBuilder()
            .AddRoom(hub, RoomSize.Large)
            .AddRoom(east, RoomSize.Large)
            .AddRoom(farEast, RoomSize.Large)
            .AddRoom(south, RoomSize.Large)
            .AddRoom(sEast, RoomSize.Large)
            .AddRoom(north, RoomSize.Small)
            .AddRoom(nWest, RoomSize.Small)
            .AddRoom(west, RoomSize.Large)
            .AddRoom(farWest, RoomSize.Large)
            .AddRoom(fwNorth, RoomSize.Medium)
            .AddConnection(hub, east, PassageType.Corridor)
            .AddConnection(east, farEast, PassageType.BlastDoor)
            .AddConnection(hub, south, PassageType.Corridor)
            .AddConnection(south, sEast, PassageType.Corridor)
            .AddConnection(hub, west, PassageType.Rubble)
            .AddConnection(west, farWest, PassageType.Corridor)
            .AddConnection(farWest, fwNorth, PassageType.Duct)
            .AddConnection(hub, north, PassageType.Vent)
            .AddConnection(north, nWest, PassageType.Vent)
            .AddConnection(nWest, west, PassageType.Vent)
            .AddObstacle(hub, west, WallInteractionConfig.RubbleClear(GearType.Bomb))
            .AddLootBarrel(farEast)
            .AddLootBarrel(sEast)
            .AddLootBarrel(farWest)
            .AddLootBarrel(fwNorth)
            .SetChargingStation(east)
            .SetLoadingStation(south)
            .SetBattery(hub, 120)
            .AddCable(hub, east)
            .AddCable(hub, south)
            .Build();
    }

    // ── Helpers ──────────────────────────────

    static RoomSize RandomRoomSize(System.Random rng)
    {
        double r = rng.NextDouble();
        if (r < 0.50) return RoomSize.Large;
        if (r < 0.80) return RoomSize.Medium;
        return RoomSize.Small;
    }

    static void TryAddConn(MapBuilder builder, HashSet<long> set,
                           Vector2Int a, Vector2Int b, PassageType type)
    {
        long k = MapModel.ConnKey(a, b);
        if (set.Add(k))
            builder.AddConnection(a, b, type);
    }
}
