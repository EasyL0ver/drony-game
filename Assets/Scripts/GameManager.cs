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

    // Rubble barrier GOs keyed by ConnKey for cleanup when interaction completes
    readonly Dictionary<long, GameObject> rubbleBarriers = new Dictionary<long, GameObject>();
    // Per-rubble glow strip renderers (swapped to corridor color on clear)
    readonly Dictionary<long, Renderer> rubbleGlowRenderers = new Dictionary<long, Renderer>();

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
        if (hexMap.Model == null)
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

        // ── spawn rubble barriers for blocked connections ──
        foreach (var (a, b, type) in hexMap.ConnectionList)
        {
            if (hexMap.Model.GetWallInteraction(a, b).HasValue)
                SpawnRubbleBarrier(a, b);
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

            // Listen for wall interaction completion (rubble clear, etc.)
            controller.OnWallInteractionCompleted += OnWallInteractionCompleted;
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

        // Invisible trigger collider so passage is clickable
        float passW = hexMap.Model.PassageWidth(type);
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(passW, 2f, 1f);
        col.center = new Vector3(0f, 1f, -0.5f);
    }

    void SpawnRubbleBarrier(Vector2Int roomA, Vector2Int roomB)
    {
        var (midA, midB) = hexMap.Model.PassageEndpoints(roomA, roomB);
        Vector3 center = (midA + midB) * 0.5f;
        float passW = hexMap.Model.PassageWidth(PassageType.Rubble);
        float passH = hexMap.Model.PassageWallHeight(PassageType.Rubble);

        Vector3 along = (midB - midA).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, along).normalized;

        var barrier = new GameObject($"RubbleBarrier_{roomA}_{roomB}");
        barrier.transform.position = center;
        barrier.transform.SetParent(transform, true);

        // Build rubble mesh: dense wall of rocks filling the passage floor to ceiling
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var rng = new System.Random(roomA.GetHashCode() ^ roomB.GetHashCode());

        float halfLen = Vector3.Distance(midA, midB) * 0.35f;
        float halfW = passW * 0.45f;

        // Pack rocks densely across width, depth, and height
        int layers = 4;
        for (int layer = 0; layer < layers; layer++)
        {
            float yBase = (layer / (float)layers) * passH;
            float yTop  = ((layer + 1) / (float)layers) * passH;
            int count = 6 + rng.Next(4);
            for (int c = 0; c < count; c++)
            {
                float rx = (float)(rng.NextDouble() * 2 - 1) * halfLen;
                float rz = (float)(rng.NextDouble() * 2 - 1) * halfW;
                float ry = yBase + (float)rng.NextDouble() * (yTop - yBase);
                Vector3 rockPos = along * rx + across * rz + Vector3.up * ry;

                // Rocks sized to overlap and fill gaps
                float size = 0.2f + (float)rng.NextDouble() * 0.35f;
                AddRock(verts, tris, rockPos, size, rng);
            }
        }

        // Extra large boulders to seal gaps
        int boulderCount = 3 + rng.Next(3);
        for (int b = 0; b < boulderCount; b++)
        {
            float rx = (float)(rng.NextDouble() * 2 - 1) * halfLen * 0.5f;
            float rz = (float)(rng.NextDouble() * 2 - 1) * halfW * 0.4f;
            float ry = (float)rng.NextDouble() * passH * 0.8f;
            Vector3 rockPos = along * rx + across * rz + Vector3.up * ry;
            float size = 0.4f + (float)rng.NextDouble() * 0.4f;
            AddRock(verts, tris, rockPos, size, rng);
        }

        var mesh = new Mesh { name = "RubbleMesh" };
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var meshGO = new GameObject("RubbleMesh");
        meshGO.transform.SetParent(barrier.transform, false);
        meshGO.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(0.18f, 0.16f, 0.14f);
        mat.SetColor("_BaseColor", new Color(0.18f, 0.16f, 0.14f));
        mat.SetFloat("_Metallic", 0.05f);
        mat.SetFloat("_Smoothness", 0.15f);
        meshGO.AddComponent<MeshRenderer>().sharedMaterial = mat;

        // Per-passage glow strip (not baked, so we can swap on clear)
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
        var passage = tile.GetPassage(neighbor);
        if (passage != null)
            passage.UpdateType(newType);
    }
}
