using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Top-level game object that spawns the hex map, drone, fog of war, and camera.
/// Attach to an empty root GameObject or use the menu item.
/// Runs before RTSCamera so Init() wins over Start().
/// </summary>
[DefaultExecutionOrder(-50)]
public class GameManager : MonoBehaviour
{
    [Header("References (auto-created if empty)")]
    public MapView hexMap;
    public FogOfWar        fog;
    public RTSCamera       rtsCamera;

    [Header("Map Settings")]
    [SerializeField] int testMapIndex = 2;

    [Header("Drone Settings")]
    [SerializeField] int startingDrones = 3;
    [SerializeField] string[] droneNames = { "Hornet-1", "Hornet-2", "Hornet-3", "Hornet-4", "Hornet-5" };

    [Header("Economy")]
    [SerializeField] int startingPoints = 5;

    public List<DroneController> Drones { get; private set; } = new List<DroneController>();
    public PlayerModel Player { get; private set; }
    public PowerNetworkModel PowerNetwork { get; private set; }

    // Rubble barrier GOs keyed by ConnKey for cleanup when interaction completes
    readonly Dictionary<long, GameObject> rubbleBarriers = new Dictionary<long, GameObject>();
    // Per-rubble glow strip renderers (swapped to corridor color on clear)
    readonly Dictionary<long, Renderer> rubbleGlowRenderers = new Dictionary<long, Renderer>();

    void Start()
    {
        if (Application.isPlaying)
            Setup();
    }

    [ContextMenu("Load Map 0 - Small")]
    void LoadMap0() { RestartWithMap(0); }
    [ContextMenu("Load Map 1 - Medium")]
    void LoadMap1() { RestartWithMap(1); }
    [ContextMenu("Load Map 2 - Salvage Run")]
    void LoadMap2() { RestartWithMap(2); }

    public void RestartWithMap(int mapIndex)
    {
        Time.timeScale = 1f;
        testMapIndex = mapIndex;
        Drones.Clear();
        rubbleBarriers.Clear();
        rubbleGlowRenderers.Clear();
        Setup();
    }

    // No per-frame fog update needed — RoomTile handles its own state
    // when DroneController calls OnDroneEnter/OnDroneExit.

    [ContextMenu("Rebuild Game")]
    public void Setup()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        // ── hex map ──
        var mapGO = new GameObject("HexMap");
        mapGO.transform.SetParent(transform, false);
        hexMap = mapGO.AddComponent<MapView>();
        hexMap.SetTestMapIndex(testMapIndex);
        hexMap.Generate();

        // ── fog of war ──
        var fogGO = new GameObject("FogOfWar");
        fogGO.transform.SetParent(transform, false);
        fog = fogGO.AddComponent<FogOfWar>();
        fog.Init(hexMap);
        fog.Reveal(Vector2Int.zero);

        // ── spawn passage wall entities for every connection ──
        foreach (var (a, b, type) in hexMap.ConnectionList)
        {
            if (type == PassageType.CrookedVent)
            {
                // Compute pipe geometry once, matching EmitCrookedVentPipe
                int eA = hexMap.EdgeToward(a, b);
                Vector3 cA = hexMap.HexCenter(a);
                Vector3 cB = hexMap.HexCenter(b);
                float rA = hexMap.RoomRadius(hexMap.RoomSizeMap[a]);
                float rB = hexMap.RoomRadius(hexMap.RoomSizeMap[b]);
                Vector3 midA = (hexMap.Corner(cA, eA, rA) + hexMap.Corner(cA, (eA + 1) % 6, rA)) * 0.5f;
                int eB = (eA + 3) % 6;
                Vector3 midB = (hexMap.Corner(cB, eB, rB) + hexMap.Corner(cB, (eB + 1) % 6, rB)) * 0.5f;

                float extend = hexMap.WallThickness * 0.5f + hexMap.VentPipeRadius * 0.5f;
                Vector3 pipeHoriz = (midB - midA).normalized;
                Vector3 pipeStart = midA - pipeHoriz * extend;
                Vector3 pipeEnd = midB + pipeHoriz * extend;

                float pipeY = Mathf.Min(hexMap.RoomWallHeight(hexMap.RoomSizeMap[a]),
                                        hexMap.RoomWallHeight(hexMap.RoomSizeMap[b])) * 0.5f;
                pipeStart.y = pipeY;
                pipeEnd.y = pipeY;

                int seed = a.x * 73 + a.y * 137 + eA * 31 + 12345;

                SpawnCrookedVent(a, b, eA, pipeStart, pipeEnd, seed);
                SpawnCrookedVent(b, a, eB, pipeStart, pipeEnd, seed);
            }
            else
            {
                SpawnPassage(a, b, type);
                SpawnPassage(b, a, type);
            }
        }

        // ── spawn rubble barriers for blocked connections ──
        foreach (var (a, b, type) in hexMap.ConnectionList)
        {
            if (type == PassageType.BlastDoor)
                SpawnBlastDoorBarrier(a, b);
            else if (hexMap.Model.HasBlockingInteraction(a, b))
                SpawnRubbleBarrier(a, b);
        }

        // ── link blast door barriers to their passages ──
        foreach (var (a, b, type) in hexMap.ConnectionList)
        {
            if (type != PassageType.BlastDoor) continue;
            long key = MapModel.ConnKey(a, b);
            rubbleBarriers.TryGetValue(key, out var barrierGO);
            rubbleGlowRenderers.TryGetValue(key, out var glowRend);

            var passA = fog.GetTile(a)?.GetPassage(b) as BlastDoorPassage;
            var passB = fog.GetTile(b)?.GetPassage(a) as BlastDoorPassage;
            if (passA != null) passA.SetBarrier(barrierGO, glowRend);
            if (passB != null) passB.SetBarrier(barrierGO, glowRend);
        }

        // ── mark starting room as refitting station ──
        var stationTile = fog.GetTile(Vector2Int.zero);

        // ── spawn refitting station building at a free wall ──
        var stationBldgGO = new GameObject("RefittingStation");
        stationBldgGO.transform.SetParent(stationTile.transform, false);
        int refitEdge = PlaceAtWall(stationBldgGO, Vector2Int.zero, stationTile.RModel, WallInteractionConfig.Refitting());
        var refitView = stationBldgGO.AddComponent<RefittingStation>();
        refitView.SetModel(stationTile.RModel.Walls[refitEdge]);

        // ── spawn loading station ──
        {
            Vector2Int loadCoord = hexMap.Model.loadingStationRoom ?? Vector2Int.zero;
            if (loadCoord == Vector2Int.zero)
            {
                // Fallback: find first corridor neighbor
                foreach (var conn in hexMap.ConnectionList)
                {
                    if (conn.type == PassageType.Corridor)
                    {
                        loadCoord = conn.a == Vector2Int.zero ? conn.b : conn.a;
                        break;
                    }
                }
            }
            if (loadCoord != Vector2Int.zero)
            {
                fog.Reveal(loadCoord);
                var loadTile = fog.GetTile(loadCoord);
                var loadGO = new GameObject("LoadingStation");
                loadGO.transform.SetParent(loadTile.transform, false);
                int loadEdge = PlaceAtWall(loadGO, loadCoord, loadTile.RModel, WallInteractionConfig.Unload());
                var loadView = loadGO.AddComponent<LoadingStation>();
                loadView.SetModel(loadTile.RModel.Walls[loadEdge]);
            }
        }

        // ── place charging station ──
        Vector2Int chargingCoord = hexMap.Model.chargingStationRoom ?? Vector2Int.zero;
        if (chargingCoord == Vector2Int.zero)
        {
            // Fallback: first neighbor of starting room
            foreach (var conn in hexMap.ConnectionList)
            {
                if (conn.a == Vector2Int.zero || conn.b == Vector2Int.zero)
                {
                    chargingCoord = conn.a == Vector2Int.zero ? conn.b : conn.a;
                    break;
                }
            }
        }
        if (chargingCoord != Vector2Int.zero)
        {
            fog.Reveal(chargingCoord);
            var chargeTile = fog.GetTile(chargingCoord);

            var chargeBldgGO = new GameObject("ChargingStation");
            chargeBldgGO.transform.SetParent(chargeTile.transform, false);
            int chargeEdge = PlaceAtWall(chargeBldgGO, chargingCoord, chargeTile.RModel, WallInteractionConfig.Charging());
            var chargeView = chargeBldgGO.AddComponent<ChargingStation>();
            chargeView.SetModel(chargeTile.RModel.Walls[chargeEdge]);
        }

        // ── loot barrels ──
        var barrelRooms = hexMap.Model.lootBarrelRooms;
        if (barrelRooms.Count == 0 && hexMap.TestMode && hexMap.ConnectionList.Count > 0)
        {
            // Legacy fallback: single barrel in corridor room
            foreach (var conn in hexMap.ConnectionList)
            {
                if (conn.type == PassageType.Corridor)
                {
                    barrelRooms.Add(conn.a == Vector2Int.zero ? conn.b : conn.a);
                    break;
                }
            }
        }
        foreach (var barrelCoord in barrelRooms)
        {
            var barrelTile = fog.GetTile(barrelCoord);
            if (barrelTile == null) continue;
            var cacheGO = new GameObject("LootCache");
            cacheGO.transform.SetParent(barrelTile.transform, false);

            // Roll random loot content
            GearItem loot = RollLoot();

            int cacheEdge = PlaceLootCache(cacheGO, barrelCoord, barrelTile.RModel, loot);
            if (cacheEdge < 0) { Destroy(cacheGO); continue; }
            var cacheView = cacheGO.AddComponent<LootCache>();
            cacheView.SetContent(loot);
            cacheView.SetModel(barrelTile.RModel.Walls[cacheEdge]);
        }

        // ── power cable network ──
        SetupPowerNetwork();

        // ── player economy ──
        Player = new PlayerModel(startingPoints);

        // ── drones ──
        Drones = new List<DroneController>();
        for (int i = 0; i < startingDrones; i++)
        {
            string droneName = i < droneNames.Length ? droneNames[i] : $"Drone-{i + 1}";

            var droneGO = new GameObject($"Drone_{i}");
            droneGO.transform.SetParent(transform, false);

            var controller = droneGO.AddComponent<DroneController>();
            controller.Init(hexMap, fog, Vector2Int.zero, droneName, i);

            // Hornet-1 starts with a free Scanner
            if (i == 0)
                controller.Model.Equip(GearCatalog.Scanner);

            // Hornet-2 starts with a Bomb in test mode
            if (i == 1 && hexMap.TestMode)
                controller.Model.Equip(GearCatalog.Bomb);

            var modelGO = new GameObject("Model");
            modelGO.transform.SetParent(droneGO.transform, false);
            modelGO.AddComponent<LowPolyDrone>();

            Drones.Add(controller);

            // Listen for wall interaction completion (rubble clear, etc.)
            controller.OnWallInteractionCompleted += OnWallInteractionCompleted;
            controller.OnDroneDestroyed += OnDroneDestroyed;
            controller.OnRoomChanged += OnDroneRoomChanged;
        }

        // ── hauler drone (test mode) ──
        if (hexMap.TestMode)
        {
            int hIdx = Drones.Count;
            var haulerGO = new GameObject($"Drone_{hIdx}");
            haulerGO.transform.SetParent(transform, false);

            var hController = haulerGO.AddComponent<DroneController>();
            hController.Init(hexMap, fog, Vector2Int.zero, "Mule-1", hIdx, DroneType.Hauler);

            var hModelGO = new GameObject("Model");
            hModelGO.transform.SetParent(haulerGO.transform, false);
            hModelGO.AddComponent<HaulerDrone>();

            Drones.Add(hController);
            hController.OnWallInteractionCompleted += OnWallInteractionCompleted;
            hController.OnDroneDestroyed += OnDroneDestroyed;
            hController.OnRoomChanged += OnDroneRoomChanged;
        }

        // ── selection manager ──
        var selGO = new GameObject("SelectionManager");
        selGO.transform.SetParent(transform, false);
        var sel = selGO.AddComponent<SelectionManager>();
        sel.Init(this);

        // ── RTS camera ──
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("RTS Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
        }

        rtsCamera = cam.GetComponent<RTSCamera>();
        if (rtsCamera == null)
            rtsCamera = cam.gameObject.AddComponent<RTSCamera>();

        rtsCamera.Init(Vector3.zero, 20f, 56f);

        // Compute camera bounds from tile positions
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var coord in hexMap.RoomList)
        {
            Vector3 c = hexMap.HexCenter(coord);
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.z < minZ) minZ = c.z;
            if (c.z > maxZ) maxZ = c.z;
        }
        rtsCamera.SetBounds(minX, maxX, minZ, maxZ);

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Palette.CameraBg;

        // ── overlay manager ──
        var overlayGO = new GameObject("OverlayManager");
        overlayGO.transform.SetParent(transform, false);
        var overlay = overlayGO.AddComponent<OverlayManager>();
        overlay.Init(Drones);

        // ── drone status UI ──
        var uiGO = new GameObject("DroneStatusUI");
        uiGO.transform.SetParent(transform, false);
        var statusUI = uiGO.AddComponent<DroneStatusUI>();
        statusUI.Init(this);
    }

    /// <summary>
    /// Position a wall GO at a free hex edge (without a passage),
    /// rotated to face inward. Creates a StationWallModel at that edge.
    /// Returns the edge index used.
    /// </summary>
    int PlaceAtWall(GameObject go, Vector2Int coord, RoomModel model, WallInteractionConfig interaction)
    {
        Vector3 center = hexMap.HexCenter(coord);
        float roomR = hexMap.RoomRadius(hexMap.RoomSizeMap[coord]);

        // Find which edges have passages
        var usedEdges = new HashSet<int>();
        foreach (var (a, b, _) in hexMap.ConnectionList)
        {
            if (a == coord)
                usedEdges.Add(hexMap.EdgeToward(coord, b));
            else if (b == coord)
                usedEdges.Add(hexMap.EdgeToward(coord, a));
        }

        // Pick the first edge without a passage or existing station
        int edge = -1;
        for (int i = 0; i < 6; i++)
        {
            if (!usedEdges.Contains(i) && !(model.Walls[i] is StationWallModel)) { edge = i; break; }
        }
        if (edge < 0) return -1;

        var stationWall = new StationWallModel(model, edge, interaction);
        model.SetWall(edge, stationWall);

        // Edge midpoint sits on the wall
        Vector3 c0 = hexMap.Corner(center, edge, roomR);
        Vector3 c1 = hexMap.Corner(center, (edge + 1) % 6, roomR);
        Vector3 wallMid = (c0 + c1) * 0.5f;

        // Push slightly inward so partially embedded in the wall
        Vector3 inward = (center - wallMid).normalized;
        go.transform.position = wallMid;
        go.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);

        return edge;
    }

    /// <summary>Place a loot cache at a free wall edge with LootCacheWallModel.</summary>
    int PlaceLootCache(GameObject go, Vector2Int coord, RoomModel model, GearItem content)
    {
        Vector3 center = hexMap.HexCenter(coord);
        float roomR = hexMap.RoomRadius(hexMap.RoomSizeMap[coord]);

        var usedEdges = new HashSet<int>();
        foreach (var (a, b, _) in hexMap.ConnectionList)
        {
            if (a == coord) usedEdges.Add(hexMap.EdgeToward(coord, b));
            else if (b == coord) usedEdges.Add(hexMap.EdgeToward(coord, a));
        }

        int edge = -1;
        for (int i = 0; i < 6; i++)
        {
            if (!usedEdges.Contains(i) && !(model.Walls[i] is StationWallModel) && !(model.Walls[i] is LootCacheWallModel))
            { edge = i; break; }
        }
        if (edge < 0) return -1;

        var cacheWall = new LootCacheWallModel(model, edge, content);
        model.SetWall(edge, cacheWall);

        Vector3 c0 = hexMap.Corner(center, edge, roomR);
        Vector3 c1 = hexMap.Corner(center, (edge + 1) % 6, roomR);
        Vector3 wallMid = (c0 + c1) * 0.5f;
        Vector3 inward = (center - wallMid).normalized;
        go.transform.position = wallMid;
        go.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);

        return edge;
    }

    /// <summary>
    /// Create the power cable network from map seed data and inject into stations.
    /// </summary>
    void SetupPowerNetwork()
    {
        var mapModel = hexMap.Model;
        if (!mapModel.batteryRoom.HasValue) { PowerNetwork = null; return; }

        PowerNetwork = new PowerNetworkModel();

        // Add power source
        var source = PowerNetwork.AddSource(mapModel.batteryRoom.Value, mapModel.batteryMaxEnergy);

        // Add cable edges
        foreach (var cable in mapModel.CableConnections)
        {
            PowerNetwork.AddCable(cable.roomA, cable.roomB);
        }

        // Spawn battery building on a wall in the battery room
        Vector2Int battCoord = mapModel.batteryRoom.Value;
        var battTile = fog.GetTile(battCoord);
        if (battTile != null)
        {
            var battGO = new GameObject("BatteryStation");
            battGO.transform.SetParent(battTile.transform, false);
            int battEdge = PlaceAtWall(battGO, battCoord, battTile.RModel, null);
            if (battEdge >= 0)
            {
                var battView = battGO.AddComponent<BatteryStation>();
                battView.SetModel(battTile.RModel.Walls[battEdge]);
                battView.SetPowerSource(source);
            }
            else
            {
                Destroy(battGO);
            }
        }

        // Inject power provider into all station walls
        foreach (var coord in mapModel.RoomList)
        {
            var tile = fog.GetTile(coord);
            if (tile == null) continue;
            for (int e = 0; e < 6; e++)
            {
                var wall = tile.RModel.Walls[e];
                if (wall is StationWallModel station)
                    station.SetPowerProvider(PowerNetwork);
                else if (wall is CorridorWallModel corridor)
                    corridor.SetPowerProvider(PowerNetwork);
            }
        }

        // Visual feedback: dim cable glow when battery dies
        PowerNetwork.OnPowerStateChanged += OnPowerStateChanged;

        // Register any drones that already have PowerTap equipped
        foreach (var drone in Drones)
            if (drone.Model.HasPowerTap)
                PowerNetwork.RegisterDrone(drone.Model);

        // Set initial power state: rooms not on the grid start dark
        foreach (var coord in mapModel.RoomList)
        {
            if (PowerNetwork.IsRoomPowered(coord)) continue;
            var tile = fog.GetTile(coord);
            if (tile == null) continue;
            tile.SetPowered(false);
            foreach (var w in tile.GetComponentsInChildren<WallView>())
                w.SetPowered(false);
        }

        // Force initial blast door glow colors now that providers are injected
        OnPowerStateChanged();
    }

    void OnPowerStateChanged()
    {
        if (hexMap == null || hexMap.CableGlowMaterial == null) return;

        bool active = PowerNetwork.IsActive;

        if (active)
            hexMap.CableGlowMaterial.SetColor("_EmissionColor", Palette.CableGlow * 4f);
        else
            hexMap.CableGlowMaterial.SetColor("_EmissionColor", Palette.CableDead * 0.5f);

        // Toggle room + station glow based on current power state
        foreach (var coord in hexMap.Model.RoomList)
        {
            bool powered = PowerNetwork.IsRoomPowered(coord);
            var tile = fog.GetTile(coord);
            if (tile == null) continue;
            tile.SetPowered(powered);
            foreach (var w in tile.GetComponentsInChildren<WallView>())
                w.SetPowered(powered);
        }

        // Toggle blast door glow color: corridor when powered, impassable when not
        foreach (var (a, b, type) in hexMap.ConnectionList)
        {
            if (type != PassageType.BlastDoor) continue;
            long key = MapModel.ConnKey(a, b);
            if (!rubbleGlowRenderers.TryGetValue(key, out var rend)) continue;
            var wall = hexMap.Model.GetWall(a, b) as CorridorWallModel;
            bool doorPowered = wall != null && wall.IsPowered;
            Color col = doorPowered ? Palette.CorridorGlow : Palette.ImpassableGlow;
            rend.sharedMaterial.SetColor("_EmissionColor", col * 4f);
        }
    }

    static GearItem RollLoot()
    {
        int totalWeight = 0;
        foreach (var (item, weight) in GearCatalog.LootTable)
            totalWeight += weight;

        int roll = Random.Range(0, totalWeight);
        int cumulative = 0;
        foreach (var (item, weight) in GearCatalog.LootTable)
        {
            cumulative += weight;
            if (roll < cumulative) return item;
        }
        return GearCatalog.FuelCell;
    }

    void SpawnPassage(Vector2Int room, Vector2Int neighbor, PassageType type)
    {
        var tile = fog.GetTile(room);
        if (tile == null) return;

        int edge = hexMap.EdgeToward(room, neighbor);
        Vector3 center = hexMap.HexCenter(room);
        float roomR = hexMap.RoomRadius(hexMap.RoomSizeMap[room]);

        Vector3 c0 = hexMap.Corner(center, edge, roomR);
        Vector3 c1 = hexMap.Corner(center, (edge + 1) % 6, roomR);
        Vector3 wallMid = (c0 + c1) * 0.5f;
        Vector3 inward = (center - wallMid).normalized;

        var go = new GameObject($"Passage_{room}_{neighbor}");
        go.transform.position = wallMid;
        go.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
        go.transform.SetParent(tile.transform, true);

        Passage passage;
        if (type == PassageType.BlastDoor)
            passage = go.AddComponent<BlastDoorPassage>();
        else
            passage = go.AddComponent<Passage>();
        passage.Init(room, neighbor, edge, type);
        passage.SetModel(tile.RModel.Walls[edge]);

        // Each side builds its own half of the passage geometry
        Material passGlow;
        if (type == PassageType.Vent)
            passGlow = hexMap.BuildVentGeometry(go.transform, room, neighbor);
        else
            passGlow = hexMap.BuildPassageGeometry(go.transform, room, neighbor, type);
        passage.SetPassageGlow(passGlow);

        // For vent passages, provide pipe geometry for ring animation
        if (type == PassageType.Vent)
        {
            Vector3 nCenter = hexMap.HexCenter(neighbor);
            float nR = hexMap.RoomRadius(hexMap.RoomSizeMap[neighbor]);
            int nEdge = hexMap.EdgeToward(neighbor, room);
            Vector3 nc0 = hexMap.Corner(nCenter, nEdge, nR);
            Vector3 nc1 = hexMap.Corner(nCenter, (nEdge + 1) % 6, nR);
            Vector3 neighborWallMid = (nc0 + nc1) * 0.5f;

            float extend = hexMap.WallThickness * 0.5f + hexMap.VentPipeRadius * 0.5f;
            Vector3 pipeHoriz = (neighborWallMid - wallMid).normalized;
            Vector3 pipeStart = wallMid - pipeHoriz * extend;
            Vector3 pipeEnd = neighborWallMid + pipeHoriz * extend;

            float smallerWH = Mathf.Min(
                hexMap.RoomWallHeight(hexMap.RoomSizeMap[room]),
                hexMap.RoomWallHeight(hexMap.RoomSizeMap[neighbor]));
            float pipeY = smallerWH * 0.5f;
            pipeStart.y = pipeY;
            pipeEnd.y = pipeY;

            passage.SetPipeInfo(pipeStart, pipeEnd, hexMap.VentPipeRadius);
        }

        // Invisible trigger collider so passage is clickable
        float passW = hexMap.PassageWidth(type);
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(passW, 2f, 1f);
        col.center = new Vector3(0f, 1f, -0.5f);
    }

    void SpawnCrookedVent(Vector2Int room, Vector2Int neighbor, int edge, Vector3 pipeStart, Vector3 pipeEnd, int seed)
    {
        var tile = fog.GetTile(room);
        if (tile == null) return;

        Vector3 center = hexMap.HexCenter(room);
        float roomR = hexMap.RoomRadius(hexMap.RoomSizeMap[room]);

        Vector3 c0 = hexMap.Corner(center, edge, roomR);
        Vector3 c1 = hexMap.Corner(center, (edge + 1) % 6, roomR);
        Vector3 wallMid = (c0 + c1) * 0.5f;
        Vector3 inward = (center - wallMid).normalized;

        var go = new GameObject($"Passage_{room}_{neighbor}");
        go.transform.position = wallMid;
        go.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
        go.transform.SetParent(tile.transform, true);

        var crookedVent = go.AddComponent<CrookedVentPassage>();
        crookedVent.Init(room, neighbor, edge, pipeStart, pipeEnd, seed);
        crookedVent.SetModel(tile.RModel.Walls[edge]);

        // Each side builds its own half
        var crookedGlow = hexMap.BuildCrookedVentGeometry(go.transform, room, neighbor);
        crookedVent.SetPassageGlow(crookedGlow);

        float passW = hexMap.PassageWidth(PassageType.CrookedVent);
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(passW, 2f, 1f);
        col.center = new Vector3(0f, 1f, -0.5f);
    }

    void SpawnRubbleBarrier(Vector2Int roomA, Vector2Int roomB)
    {
        var (midA, midB) = hexMap.PassageEndpoints(roomA, roomB);
        Vector3 center = (midA + midB) * 0.5f;
        float passW = hexMap.PassageWidth(PassageType.Rubble);
        float passH = hexMap.PassageWallHeight(PassageType.Rubble);

        Vector3 along = (midB - midA).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, along).normalized;

        var barrier = new GameObject($"RubbleBarrier_{roomA}_{roomB}");
        barrier.transform.position = center;
        barrier.transform.SetParent(transform, true);

        float halfLen = Vector3.Distance(midA, midB) * 0.5f;
        float halfW = passW * 0.5f;

        // Materials
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        var matRubble = new Material(sh);
        matRubble.color = new Color(0.35f, 0.25f, 0.18f);
        matRubble.SetColor("_BaseColor", new Color(0.35f, 0.25f, 0.18f));
        matRubble.SetFloat("_Metallic", 0.1f);
        matRubble.SetFloat("_Smoothness", 0.15f);
        matRubble.SetFloat("_Cull", 0f);

        var matWallChunk = new Material(sh);
        matWallChunk.color = new Color(0.12f, 0.12f, 0.14f);
        matWallChunk.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.14f));
        matWallChunk.SetFloat("_Metallic", 0.65f);
        matWallChunk.SetFloat("_Smoothness", 0.5f);

        var matBrokenGlow = hexMap.MakeEmissive(Palette.ImpassableGlow, 5f);

        RubbleBarrierMesh.Build(barrier.transform, new RubbleBarrierMesh.Params
        {
            center = center,
            along = along,
            across = across,
            halfLen = halfLen,
            halfW = halfW,
            passH = passH,
            seed = roomA.GetHashCode() ^ roomB.GetHashCode()
        }, matRubble, matWallChunk, matBrokenGlow);

        // Per-passage glow strip
        Mesh glowMesh = hexMap.BuildPassageGlowMesh(roomA, roomB, PassageType.Rubble);
        if (glowMesh.vertexCount > 0)
        {
            var glowGO = new GameObject("RubbleGlow");
            glowGO.transform.SetParent(transform, false);
            glowGO.AddComponent<MeshFilter>().sharedMesh = glowMesh;
            var glowMat = hexMap.MakeEmissive(Palette.ImpassableGlow, 4f);
            var rend = glowGO.AddComponent<MeshRenderer>();
            rend.sharedMaterial = glowMat;

            long key = MapModel.ConnKey(roomA, roomB);
            rubbleGlowRenderers[key] = rend;
        }

        long barrierKey = MapModel.ConnKey(roomA, roomB);
        rubbleBarriers[barrierKey] = barrier;
    }

    void SpawnBlastDoorBarrier(Vector2Int roomA, Vector2Int roomB)
    {
        var (midA, midB) = hexMap.PassageEndpoints(roomA, roomB);
        Vector3 center = (midA + midB) * 0.5f;
        float passW = hexMap.PassageWidth(PassageType.BlastDoor);
        float passH = hexMap.PassageWallHeight(PassageType.BlastDoor);

        Vector3 along = (midB - midA).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, along).normalized;

        var barrier = new GameObject($"BlastDoor_{roomA}_{roomB}");
        barrier.transform.position = center;
        barrier.transform.SetParent(transform, true);

        float halfLen = Vector3.Distance(midA, midB) * 0.5f;
        float halfW = passW * 0.5f;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        // Heavy steel door material
        var matDoor = new Material(sh);
        matDoor.color = new Color(0.25f, 0.28f, 0.32f);
        matDoor.SetColor("_BaseColor", new Color(0.25f, 0.28f, 0.32f));
        matDoor.SetFloat("_Metallic", 0.85f);
        matDoor.SetFloat("_Smoothness", 0.6f);

        // Door panel: a flat slab across the passage
        var doorGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        doorGO.name = "DoorPanel";
        doorGO.transform.SetParent(barrier.transform, false);
        doorGO.transform.localPosition = Vector3.up * passH * 0.5f;
        doorGO.transform.localScale = new Vector3(passW, passH, 0.15f);
        doorGO.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
        doorGO.GetComponent<Renderer>().sharedMaterial = matDoor;

        // Warning stripe emissive
        var matStripe = hexMap.MakeEmissive(new Color(0.9f, 0.4f, 0.05f), 3f);
        var stripeGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stripeGO.name = "WarningStripe";
        stripeGO.transform.SetParent(barrier.transform, false);
        stripeGO.transform.localPosition = Vector3.up * passH * 0.75f;
        stripeGO.transform.localScale = new Vector3(passW * 0.9f, passH * 0.08f, 0.16f);
        stripeGO.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
        stripeGO.GetComponent<Renderer>().sharedMaterial = matStripe;

        // Per-passage glow strip (starts corridor color; OnPowerStateChanged will update if unpowered)
        Mesh glowMesh = hexMap.BuildPassageGlowMesh(roomA, roomB, PassageType.BlastDoor);
        if (glowMesh.vertexCount > 0)
        {
            var glowGO = new GameObject("BlastDoorGlow");
            glowGO.transform.SetParent(transform, false);
            glowGO.AddComponent<MeshFilter>().sharedMesh = glowMesh;
            var glowMat = hexMap.MakeEmissive(Palette.CorridorGlow, 4f);
            var rend = glowGO.AddComponent<MeshRenderer>();
            rend.sharedMaterial = glowMat;

            long key = MapModel.ConnKey(roomA, roomB);
            rubbleGlowRenderers[key] = rend;
        }

        long barrierKey = MapModel.ConnKey(roomA, roomB);
        rubbleBarriers[barrierKey] = barrier;
    }

    static void AddRock(List<Vector3> verts, List<int> tris, Vector3 pos, float size, System.Random rng)
    {
        // Angular rock — heavily deformed octahedron with non-uniform axes
        int baseIdx = verts.Count;
        float J() => 0.6f + (float)rng.NextDouble() * 0.8f;

        // Random stretch per axis for slab/column/chunky variety
        float sx = size * J();
        float sy = size * J();
        float sz = size * J();

        Vector3 top    = pos + Vector3.up    * sy;
        Vector3 bottom = pos - Vector3.up    * sy * (0.3f + (float)rng.NextDouble() * 0.5f);
        Vector3 front  = pos + Vector3.forward * sz;
        Vector3 back   = pos - Vector3.forward * sz;
        Vector3 left   = pos - Vector3.right   * sx;
        Vector3 right  = pos + Vector3.right   * sx;

        // Extra jitter to break symmetry
        for (int j = 0; j < 3; j++)
        {
            float dx = ((float)rng.NextDouble() - 0.5f) * size * 0.3f;
            float dz = ((float)rng.NextDouble() - 0.5f) * size * 0.3f;
            top    += new Vector3(dx, 0, dz);
            bottom += new Vector3(-dx, 0, -dz);
        }

        verts.Add(top);    // 0
        verts.Add(bottom); // 1
        verts.Add(front);  // 2
        verts.Add(back);   // 3
        verts.Add(left);   // 4
        verts.Add(right);  // 5

        int T = baseIdx;
        tris.AddRange(new[] {
            T+0, T+2, T+5,  T+0, T+5, T+3,
            T+0, T+3, T+4,  T+0, T+4, T+2,
            T+1, T+5, T+2,  T+1, T+3, T+5,
            T+1, T+4, T+3,  T+1, T+2, T+4,
        });
    }

    /// <summary>
    /// Elongated angular rock that protrudes outward from the wall surface.
    /// Uses a stretched pentahedron shape for a shard/spike look.
    /// </summary>
    static void AddProtrudingRock(List<Vector3> verts, List<int> tris,
        Vector3 pos, float size, float stretch, Vector3 outDir, Vector3 sideDir, System.Random rng)
    {
        int baseIdx = verts.Count;
        float J() => 0.7f + (float)rng.NextDouble() * 0.6f;

        float sx = size * J();
        float sy = size * J();

        // Tip extends outward
        Vector3 tip = pos + outDir.normalized * size * stretch;
        // Add randomness to tip
        tip += sideDir * ((float)rng.NextDouble() - 0.5f) * size * 0.3f;
        tip += Vector3.up * ((float)rng.NextDouble() - 0.5f) * size * 0.4f;

        // Base vertices form a rough quad around the attachment point
        Vector3 up = Vector3.up * sy;
        Vector3 side = sideDir * sx;

        Vector3 b0 = pos + up + side;
        Vector3 b1 = pos + up - side;
        Vector3 b2 = pos - up * 0.6f - side;
        Vector3 b3 = pos - up * 0.6f + side;

        // Jitter base vertices
        for (int i = 0; i < 1; i++)
        {
            float jx = ((float)rng.NextDouble() - 0.5f) * size * 0.2f;
            float jy = ((float)rng.NextDouble() - 0.5f) * size * 0.2f;
            Vector3 jitter = sideDir * jx + Vector3.up * jy;
            b0 += jitter; b1 -= jitter; b2 += jitter * 0.5f; b3 -= jitter * 0.5f;
        }

        verts.Add(tip); // 0
        verts.Add(b0);  // 1
        verts.Add(b1);  // 2
        verts.Add(b2);  // 3
        verts.Add(b3);  // 4

        int T = baseIdx;
        tris.AddRange(new[] {
            T+0, T+1, T+2,  // top face
            T+0, T+2, T+3,  // right face
            T+0, T+3, T+4,  // bottom face
            T+0, T+4, T+1,  // left face
            T+1, T+3, T+2,  // base tri 1
            T+1, T+4, T+3,  // base tri 2
        });
    }

    void OnWallInteractionCompleted(Vector2Int roomA, Vector2Int roomB)
    {
        long key = MapModel.ConnKey(roomA, roomB);

        // Destroy barrier GO
        if (rubbleBarriers.TryGetValue(key, out var barrier))
        {
            Destroy(barrier);
            rubbleBarriers.Remove(key);
        }

        // Swap glow from impassable red → corridor cyan
        if (rubbleGlowRenderers.TryGetValue(key, out var rend))
        {
            var newType = hexMap.Model.GetPassageType(roomA, roomB);
            Color glowColor = newType == PassageType.Duct ? Palette.DuctGlow : Palette.CorridorGlow;
            rend.sharedMaterial = hexMap.MakeEmissive(glowColor, 4f);
            rubbleGlowRenderers.Remove(key);
        }

        // Update both Passage entities to reflect the new type
        var type = hexMap.Model.GetPassageType(roomA, roomB);
        UpdatePassageType(roomA, roomB, type);
        UpdatePassageType(roomB, roomA, type);
    }

    void UpdatePassageType(Vector2Int room, Vector2Int neighbor, PassageType newType)
    {
        var tile = fog.GetTile(room);
        if (tile == null) return;
        var passage = tile.GetPassage(neighbor) as Passage;
        if (passage != null)
            passage.UpdateType(newType);
    }

    void OnDroneDestroyed(DroneController drone)
    {
        drone.OnWallInteractionCompleted -= OnWallInteractionCompleted;
        drone.OnDroneDestroyed -= OnDroneDestroyed;
        drone.OnRoomChanged -= OnDroneRoomChanged;
        if (PowerNetwork != null)
            PowerNetwork.UnregisterDrone(drone.Model);
        Drones.Remove(drone);
    }

    void OnDroneRoomChanged(DroneController drone, Vector2Int newRoom)
    {
        if (PowerNetwork == null) return;
        if (!drone.Model.HasPowerTap) return;
        PowerNetwork.NotifyDroneMoved(drone.Model);
    }
}
