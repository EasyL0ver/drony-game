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

        // ── mark starting room as refitting station ──
        var stationTile = fog.GetTile(Vector2Int.zero);
        if (stationTile != null)
            stationTile.RModel.IsRefittingStation = true;

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
            controller.Init(hexMap, fog, Vector2Int.zero, droneName);

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
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.02f, 1f);

        // ── drone status UI ──
        var uiGO = new GameObject("DroneStatusUI");
        uiGO.transform.SetParent(transform, false);
        var statusUI = uiGO.AddComponent<DroneStatusUI>();
        statusUI.Init(this);
    }
}
