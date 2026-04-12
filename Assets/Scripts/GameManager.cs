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
    public HexMapGenerator hexMap;
    public FogOfWar        fog;
    public RTSCamera       rtsCamera;

    [Header("Drone Settings")]
    [SerializeField] int startingDrones = 3;
    [SerializeField] string[] droneNames = { "Hornet-1", "Hornet-2", "Hornet-3", "Hornet-4", "Hornet-5" };

    [Header("Economy")]
    [SerializeField] int startingPoints = 5;

    public List<DroneController> Drones { get; private set; } = new List<DroneController>();
    public PlayerModel Player { get; private set; }

    void Start()
    {
        if (Application.isPlaying)
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
        hexMap = mapGO.AddComponent<HexMapGenerator>();
        if (hexMap.RoomList == null)
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
            SpawnPassage(a, b, type);
            SpawnPassage(b, a, type);
        }

        // ── mark starting room as refitting station ──
        var stationTile = fog.GetTile(Vector2Int.zero);

        // ── spawn refitting station building at a free wall ──
        var stationBldgGO = new GameObject("RefittingStation");
        stationBldgGO.transform.SetParent(stationTile.transform, false);
        PlaceAtWall(stationBldgGO, Vector2Int.zero, stationTile.RModel, StationType.Refitting);
        stationBldgGO.AddComponent<RefittingStation>();

        // ── place charging station on a neighbor of the starting room ──
        Vector2Int chargingCoord = Vector2Int.zero;
        foreach (var conn in hexMap.ConnectionList)
        {
            if (conn.a == Vector2Int.zero || conn.b == Vector2Int.zero)
            {
                Vector2Int neighbor = conn.a == Vector2Int.zero ? conn.b : conn.a;
                chargingCoord = neighbor;
                break;
            }
        }
        if (chargingCoord != Vector2Int.zero)
        {
            fog.Reveal(chargingCoord);
            var chargeTile = fog.GetTile(chargingCoord);

            var chargeBldgGO = new GameObject("ChargingStation");
            chargeBldgGO.transform.SetParent(chargeTile.transform, false);
            PlaceAtWall(chargeBldgGO, chargingCoord, chargeTile.RModel, StationType.Charging);
            chargeBldgGO.AddComponent<ChargingStation>();
        }

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

            var modelGO = new GameObject("Model");
            modelGO.transform.SetParent(droneGO.transform, false);
            modelGO.AddComponent<LowPolyDrone>();

            Drones.Add(controller);
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
    /// Position a station GO at a free hex wall (edge without a passage),
    /// rotated to face inward. Records the edge on the RoomModel.
    /// </summary>
    void PlaceAtWall(GameObject go, Vector2Int coord, RoomModel model, StationType stationType)
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

        // Pick the first edge without a passage
        int edge = 0;
        for (int i = 0; i < 6; i++)
        {
            if (!usedEdges.Contains(i)) { edge = i; break; }
        }

        // Record on model
        model.SetWallStation(edge, stationType);

        // Edge midpoint sits on the wall
        Vector3 c0 = hexMap.Corner(center, edge, roomR);
        Vector3 c1 = hexMap.Corner(center, (edge + 1) % 6, roomR);
        Vector3 wallMid = (c0 + c1) * 0.5f;

        // Push slightly inward so the station is partially embedded in the wall
        Vector3 inward = (center - wallMid).normalized;
        go.transform.position = wallMid;
        go.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
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

        var passage = go.AddComponent<Passage>();
        passage.Init(room, neighbor, edge, type);
    }
}
