using UnityEngine;

/// <summary>
/// Top-level game object that spawns the hex map, drone, and camera.
/// Attach to an empty root GameObject or use the menu item.
/// </summary>
[ExecuteAlways]
public class GameManager : MonoBehaviour
{
    [Header("References (auto-created if empty)")]
    public HexMapGenerator hexMap;
    public LowPolyDrone    drone;
    public RTSCamera       rtsCamera;

    void OnEnable()
    {
        if (transform.childCount == 0)
            Setup();
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

        // ── drone in first room (hex 0,0 → world origin) ──
        var droneGO = new GameObject("Drone");
        droneGO.transform.SetParent(transform, false);
        droneGO.transform.localPosition = new Vector3(0f, 1f, 0f);
        drone = droneGO.AddComponent<LowPolyDrone>();

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

        cam.transform.position = new Vector3(0f, 35f, -20f);
        cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
    }
}
