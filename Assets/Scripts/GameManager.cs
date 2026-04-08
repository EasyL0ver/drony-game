using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Top-level game object that spawns the hex map, drone, fog of war, and camera.
/// Attach to an empty root GameObject or use the menu item.
/// </summary>
[ExecuteAlways]
public class GameManager : MonoBehaviour
{
    [Header("References (auto-created if empty)")]
    public HexMapGenerator hexMap;
    public LowPolyDrone    drone;
    public FogOfWar        fog;
    public RTSCamera       rtsCamera;

    // Drone's current hex room (for fog updates)
    Vector2Int droneRoom = Vector2Int.zero;

    void Start()
    {
        // Only rebuild automatically when entering play mode
        if (Application.isPlaying)
            Setup();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (fog == null || hexMap == null) return;

        fog.UpdateVisibility(new List<Vector2Int> { droneRoom });
    }

    [ContextMenu("Rebuild Game")]
    public void Setup()
    {
        // clean existing children
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        // ── hex map ──
        var mapGO = new GameObject("HexMap");
        mapGO.transform.SetParent(transform, false);
        hexMap = mapGO.AddComponent<HexMapGenerator>();
        // Generate() fires automatically via OnEnable

        // ── fog of war ──
        var fogGO = new GameObject("FogOfWar");
        fogGO.transform.SetParent(transform, false);
        fog = fogGO.AddComponent<FogOfWar>();
        fog.Init(hexMap);
        fog.Reveal(Vector2Int.zero); // starting room visible

        // ── drone in first room (hex 0,0 → world origin) ──
        var droneGO = new GameObject("Drone");
        droneGO.transform.SetParent(transform, false);
        droneGO.transform.localPosition = new Vector3(0f, 1f, 0f);
        drone = droneGO.AddComponent<LowPolyDrone>();
        droneRoom = Vector2Int.zero;

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

        // Center camera on room 0 at origin
        rtsCamera.Init(Vector3.zero, 20f, 85f);

        // Match background to fog so unknown rooms are truly invisible
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.02f, 1f);
    }
}
