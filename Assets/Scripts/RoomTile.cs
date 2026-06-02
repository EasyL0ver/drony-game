using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A connection from this tile to a neighbor, through a specific passage type.
/// Future: hazards, items, blockages in the passage.
/// </summary>
[System.Serializable]
public class TileConnection
{
    public RoomTile neighbor;
    public PassageType passageType;
    public int edgeIndex; // which hex edge (0-5) this neighbor is on
}

/// <summary>
/// Self-contained room tile view. Delegates game state to RoomModel,
/// owns fog/outline visuals and interaction meshes.
/// </summary>
public class RoomTile : MonoBehaviour
{
    // ── Model (pure game logic) ──────────────
    public RoomModel RModel { get; private set; }

    // Convenience accessors that delegate to model
    public Vector2Int Coord => RModel.Coord;
    public RoomSize Size => RModel.Size;
    public FogState State => RModel.State;
    public float ScanProgress => RModel.ScanProgress;
    public float ScanTimeLeft => RModel.ScanTimeLeft;
    public float ScanElapsed => RModel.ScanElapsed;
    public float ScanTotalTime => RModel.ScanDuration;

    public List<TileConnection> Connections { get; private set; } = new List<TileConnection>();

    /// <summary>Get the WallView at the given edge, or null.</summary>
    public WallView GetWallView(int edge)
    {
        foreach (var w in GetComponentsInChildren<WallView>())
            if (w.Model != null && w.Model.EdgeIndex == edge) return w;
        return null;
    }

    /// <summary>
    /// Get the passage WallView for the given neighbor room, or null if none.
    /// </summary>
    public WallView GetPassage(Vector2Int neighbor)
    {
        foreach (var w in GetComponentsInChildren<WallView>())
        {
            if (w.Model == null) continue;
            if (w.Model.Neighbor != null && w.Model.Neighbor.Owner != null
                && w.Model.Neighbor.Owner.Coord == neighbor)
                return w;
        }
        return null;
    }

    // Visuals
    MeshRenderer fogRenderer;
    GameObject[] outlineEdges = new GameObject[6];
    bool outlineShown;
    Material matUnknown, matDiscovered, matOutline, matOutlineHover;

    // Interaction — hover indicators
    GameObject hoverUnknown;    // full hex for unknown rooms
    GameObject hoverCenter;     // small inner hex for move-to-center
    GameObject[] hoverWedges = new GameObject[6]; // per-edge wedge for wall hover
    GameObject moveFlash;
    MeshRenderer flashRenderer;
    Material matHoverUnknown, matHoverCenter, matHoverWall, matFlash;
    bool isHovered;
    int activeWedgeEdge = -1;
    float flashTimer;
    const float flashDuration = 0.5f;
    float fogMeshY;

    // Drone presence indicator (shown on unknown tiles when a drone is inside)
    HashSet<DroneController> dronesPresent = new HashSet<DroneController>();
    GameObject droneLabel;
    TextMesh droneLabelText;

    // World-space center of this room
    public Vector3 Center { get; private set; }

    // Config (set once by builder)
    float fogElevation;
    float outlineRadius;

    // ── setup (called by builder) ────────────

    public void Init(Vector2Int coord, RoomSize size,
                     HexMapGenerator map, float fogElev, float outlineR,
                     Material unknown, Material discovered, Material outline,
                     float scanDur = 3f)
    {
        RModel = new RoomModel(coord, size, scanDur);
        RModel.OnStateChanged += OnModelStateChanged;

        Center = map.HexCenter(coord);
        fogElevation = fogElev;
        outlineRadius = outlineR;
        matUnknown = unknown;
        matDiscovered = discovered;
        matOutline = outline;

        // Bright version of outline for hover
        matOutlineHover = new Material(outline);
        Color hc = outline.color;
        hc = Color.Lerp(hc, Color.white, 0.15f);
        hc.a = Mathf.Min(1f, outline.color.a * 1.4f);
        matOutlineHover.color = hc;
        matOutlineHover.SetColor("_BaseColor", hc);

        BuildFogMesh(map);
        BuildOutlineMesh(map);
        BuildInteractionMeshes(map);
        ApplyVisuals();
    }

    public void AddConnection(TileConnection conn)
    {
        Connections.Add(conn);
    }

    // ── drone interaction ────────────────────

    // ── drone interaction (delegates to model) ──

    public void OnDroneEnter(DroneController drone)
    {
        RModel.OnDroneEnter(drone?.Model);
        if (drone != null) dronesPresent.Add(drone);
        RefreshDroneLabel();
    }

    public void OnDroneArrived(bool canScan = true)
    {
        bool scanStarted = RModel.OnDroneArrived(canScan);
        // Show outlines on unknown neighbors when we reveal/scan
        if (State == FogState.Scanning || State == FogState.Visible)
        {
            foreach (var conn in Connections)
            {
                if (conn.neighbor.State == FogState.Unknown)
                    conn.neighbor.ShowOutline(true);
            }
        }
    }

    public void RevealImmediate()
    {
        RModel.RevealImmediate();
        foreach (var conn in Connections)
        {
            if (conn.neighbor.State == FogState.Unknown)
                conn.neighbor.ShowOutline(true);
        }
    }

    public void OnDroneExit(DroneController drone)
    {
        RModel.OnDroneExit(drone?.Model);
        if (drone != null) dronesPresent.Remove(drone);
        RefreshDroneLabel();
    }

    // ── model state change callback ──────────

    void OnModelStateChanged(FogState oldState, FogState newState)
    {
        ApplyVisuals();
        RefreshDroneLabel();
    }

    void ApplyVisuals()
    {
        if (fogRenderer == null) return;

        switch (State)
        {
            case FogState.Unknown:
                fogRenderer.enabled = true;
                fogRenderer.sharedMaterial = matUnknown;
                ShowOutline(false);
                break;
            case FogState.Scanning:
                fogRenderer.enabled = true;
                fogRenderer.sharedMaterial = matUnknown;
                break;
            case FogState.Discovered:
                fogRenderer.enabled = true;
                fogRenderer.sharedMaterial = matDiscovered;
                ShowOutline(false);
                break;
            case FogState.Visible:
                fogRenderer.enabled = false;
                ShowOutline(false);
                break;
        }


    }

    public void ShowOutline(bool show)
    {
        outlineShown = show;
        RefreshOutlineEdges();

        // Neighbors sharing an edge need to refresh too
        foreach (var conn in Connections)
            if (conn.neighbor.outlineShown)
                conn.neighbor.RefreshOutlineEdges();
    }

    /// <summary>
    /// Activate only edges that don't overlap with an adjacent outlined tile.
    /// For shared edges, the tile with the lower coord draws it.
    /// </summary>
    void RefreshOutlineEdges()
    {
        for (int i = 0; i < 6; i++)
        {
            if (outlineEdges[i] == null) continue;

            if (!outlineShown)
            {
                outlineEdges[i].SetActive(false);
                continue;
            }

            // Check if a neighbor on this edge also has outline shown
            bool neighborOutlined = false;
            foreach (var conn in Connections)
            {
                if (conn.edgeIndex == i && conn.neighbor.outlineShown)
                {
                    neighborOutlined = true;
                    break;
                }
            }

            if (neighborOutlined)
            {
                // Only the tile with the "lower" coord draws the shared edge
                Vector2Int neighborCoord = Vector2Int.zero;
                foreach (var conn in Connections)
                    if (conn.edgeIndex == i) { neighborCoord = conn.neighbor.Coord; break; }

                bool iDraw = (Coord.x < neighborCoord.x) ||
                             (Coord.x == neighborCoord.x && Coord.y < neighborCoord.y);
                outlineEdges[i].SetActive(iDraw);
            }
            else
            {
                outlineEdges[i].SetActive(true);
            }
        }
    }

    // ── interaction ──────────────────────────

    public void SetHovered(bool hovered, WallView hoveredWall = null, int hoverEdge = -1)
    {
        isHovered = hovered;

        // Resolve which edge to highlight
        int wallEdge = hoverEdge;
        if (wallEdge < 0 && hoveredWall != null && hoveredWall.Model != null)
            wallEdge = hoveredWall.Model.EdgeIndex;

        // Determine which hover indicator to show
        if (hovered)
        {
            bool isUnknown = State == FogState.Unknown || State == FogState.Scanning;

            // Unknown room: full hex
            if (hoverUnknown != null)
            {
                hoverUnknown.SetActive(isUnknown);
                if (isUnknown)
                {
                    float yOff = fogMeshY;
                    hoverUnknown.transform.localPosition = new Vector3(0f, yOff, 0f);
                }
            }

            // Visible room center: small inner hex
            if (hoverCenter != null)
                hoverCenter.SetActive(!isUnknown && wallEdge < 0);

            // Wall edge wedge
            if (activeWedgeEdge >= 0 && activeWedgeEdge < 6 && hoverWedges[activeWedgeEdge] != null)
                hoverWedges[activeWedgeEdge].SetActive(false);
            activeWedgeEdge = (!isUnknown && wallEdge >= 0) ? wallEdge : -1;
            if (activeWedgeEdge >= 0 && hoverWedges[activeWedgeEdge] != null)
                hoverWedges[activeWedgeEdge].SetActive(true);
        }
        else
        {
            if (hoverUnknown != null) hoverUnknown.SetActive(false);
            if (hoverCenter != null) hoverCenter.SetActive(false);
            if (activeWedgeEdge >= 0 && activeWedgeEdge < 6 && hoverWedges[activeWedgeEdge] != null)
                hoverWedges[activeWedgeEdge].SetActive(false);
            activeWedgeEdge = -1;
        }

        // Brighten outline edges on hover
        if (outlineShown)
        {
            Material mat = hovered ? matOutlineHover : matOutline;
            ApplyOutlineMaterial(mat);
        }

        // Station structure glow
        foreach (var w in GetComponentsInChildren<WallView>())
        {
            w.SetHoverGlow(hovered && w == hoveredWall);
        }
    }

    void ApplyOutlineMaterial(Material mat)
    {
        for (int i = 0; i < 6; i++)
        {
            if (outlineEdges[i] != null)
                outlineEdges[i].GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    public void FlashMoveTarget()
    {
        flashTimer = flashDuration;
        if (moveFlash != null)
            moveFlash.SetActive(true);
    }

    void RefreshDroneLabel()
    {
        if (droneLabel == null) return;
        bool show = dronesPresent.Count > 0 && State == FogState.Unknown;
        droneLabel.SetActive(show);
        if (show)
        {
            var names = new System.Text.StringBuilder();
            foreach (var d in dronesPresent)
            {
                if (names.Length > 0) names.Append('\n');
                names.Append(d.Model.Name);
            }
            droneLabelText.text = names.ToString();
        }
    }

    public IReadOnlyCollection<DroneController> DronesOnTile => dronesPresent;

    void Update()
    {
        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(flashTimer / flashDuration);
            Color c = new Color(1f, 1f, 1f, t * 0.3f);
            matFlash.color = c;
            matFlash.SetColor("_BaseColor", c);

            if (flashTimer <= 0f && moveFlash != null)
                moveFlash.SetActive(false);
        }

        // Scanning progress — delegate to model
        if (State == FogState.Scanning)
        {
            RModel.AdvanceScan(Time.deltaTime);
        }
    }

    // ── mesh builders ────────────────────────

    void BuildFogMesh(HexMapGenerator map)
    {
        fogMeshY = map.WallHeight + fogElevation;
        Vector3 center = map.HexCenter(Coord);

        var go = new GameObject("Fog");
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        fogRenderer = go.AddComponent<MeshRenderer>();
        var col = go.AddComponent<MeshCollider>();
        mf.sharedMesh = RoomTileMesh.HexLid(center, outlineRadius, fogMeshY);
        col.sharedMesh = mf.sharedMesh;
        fogRenderer.sharedMaterial = matUnknown;
        fogRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fogRenderer.receiveShadows = false;
    }

    void BuildOutlineMesh(HexMapGenerator map)
    {
        float fogY = map.WallHeight + fogElevation + 0.02f;
        Vector3 center = map.HexCenter(Coord);
        float outerR = outlineRadius;
        float innerR = outerR - 0.08f;

        for (int i = 0; i < 6; i++)
        {
            var mesh = RoomTileMesh.EdgeStrip(center, outerR, innerR, fogY, i);

            var go = new GameObject($"Edge_{i}");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = matOutline;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);

            outlineEdges[i] = go;
        }
    }

    void BuildInteractionMeshes(HexMapGenerator map)
    {
        Vector3 center = map.HexCenter(Coord);
        float fullR = outlineRadius * 0.97f;
        Mesh fullHex = RoomTileMesh.FlatHex(center, fullR, 0.03f);

        // ── Unknown room hover: full hex (subtle white) ──
        matHoverUnknown = MakeInteractionMat(Palette.HoverUnknown);
        hoverUnknown = MakeHoverObject("HoverUnknown", fullHex, matHoverUnknown);

        // ── Center hover: small inner hex (blue tint) ──
        Mesh innerHex = RoomTileMesh.FlatHex(center, fullR * 0.5f, 0.03f);
        matHoverCenter = MakeInteractionMat(Palette.HoverCenter);
        hoverCenter = MakeHoverObject("HoverCenter", innerHex, matHoverCenter);

        // ── Wall hover: per-edge wedge (amber) ──
        matHoverWall = MakeInteractionMat(Palette.HoverWall);
        float wedgeOuter = fullR;
        float wedgeInner = fullR * 0.55f;
        for (int i = 0; i < 6; i++)
        {
            Mesh wedge = RoomTileMesh.EdgeWedge(center, wedgeOuter, wedgeInner, 0.03f, i);
            hoverWedges[i] = MakeHoverObject($"HoverWedge_{i}", wedge, matHoverWall);
        }

        // ── Move flash ──
        moveFlash = new GameObject("MoveFlash");
        moveFlash.transform.SetParent(transform, false);
        var mf2 = moveFlash.AddComponent<MeshFilter>();
        flashRenderer = moveFlash.AddComponent<MeshRenderer>();
        mf2.sharedMesh = fullHex;
        matFlash = MakeInteractionMat(new Color(1f, 1f, 1f, 0.3f));
        flashRenderer.sharedMaterial = matFlash;
        flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flashRenderer.receiveShadows = false;
        moveFlash.SetActive(false);

        // Drone name label (world-space text on top of fog)
        droneLabel = new GameObject("DroneLabel");
        droneLabel.transform.SetParent(transform, false);
        droneLabel.transform.position = new Vector3(center.x, fogMeshY + 0.05f, center.z);
        droneLabel.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        droneLabelText = droneLabel.AddComponent<TextMesh>();
        droneLabelText.fontSize = 32;
        droneLabelText.characterSize = 0.12f;
        droneLabelText.anchor = TextAnchor.MiddleCenter;
        droneLabelText.alignment = TextAlignment.Center;
        droneLabelText.color = Palette.WithAlpha(Palette.DroneIdle, 0.9f);
        droneLabelText.text = "";
        droneLabel.SetActive(false);
    }

    GameObject MakeHoverObject(string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    Material MakeInteractionMat(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");

        var mat = new Material(sh);
        mat.color = c;
        mat.SetColor("_BaseColor", c);
        mat.SetFloat("_Surface", 1f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_Cull", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }

    // ── Drone navigation within room ────────────

    /// <summary>
    /// Smoothly move a drone from its current position to a target point within this room.
    /// Uses smooth-step easing. Calls onComplete when done.
    /// </summary>
    public void NavigateDrone(Transform drone, Vector3 target, float duration, System.Action onComplete)
    {
        StartCoroutine(RunNavigate(drone, target, duration, onComplete));
    }

    /// <summary>Navigate drone to this room's center point at the given hover height.</summary>
    public void NavigateDroneToCenter(Transform drone, float hoverY, float duration, System.Action onComplete)
    {
        Vector3 target = new Vector3(Center.x, hoverY, Center.z);
        NavigateDrone(drone, target, duration, onComplete);
    }

    /// <summary>Navigate drone to a passage's park point.</summary>
    public void NavigateDroneToPassage(Transform drone, Passage passage, float hoverY, float duration, System.Action onComplete)
    {
        Vector3 target = passage.DroneParkPoint;
        target.y = hoverY;
        NavigateDrone(drone, target, duration, onComplete);
    }

    System.Collections.IEnumerator RunNavigate(Transform drone, Vector3 target, float duration, System.Action onComplete)
    {
        Vector3 start = drone.position;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            drone.position = Vector3.Lerp(start, target, t);
            UpdateLineConsumed(t);

            Vector3 dir = (target - start);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);

            elapsed += Time.deltaTime;
            yield return null;
        }
        drone.position = target;
        HideLine();
        onComplete?.Invoke();
    }

    // ── dashed route line ────────────────────────────────────────

    const float lineY = 0.06f;
    const float lineWidth = 0.12f;
    const float lineDash = 0.30f;
    const float lineGap = 0.15f;

    GameObject lineGO;
    MeshFilter lineMF;
    MeshRenderer lineMR;
    Material lineMat;
    Mesh lineMesh;
    readonly List<Vector3> lineWaypoints = new List<Vector3>();
    readonly List<float> lineCumulDist = new List<float>();

    /// <summary>Show a dashed line segment between two points.</summary>
    public void ShowLine(Vector3 from, Vector3 to, Color color)
    {
        EnsureLine();
        lineGO.SetActive(true);

        lineWaypoints.Clear();
        lineCumulDist.Clear();
        lineWaypoints.Add(new Vector3(from.x, lineY, from.z));
        lineWaypoints.Add(new Vector3(to.x, lineY, to.z));
        lineCumulDist.Add(0f);
        lineCumulDist.Add(Vector3.Distance(lineWaypoints[0], lineWaypoints[1]));

        lineMat.color = color;
        lineMat.SetColor("_BaseColor", color);
        DashedRibbon.Build(lineMesh, lineWaypoints, lineCumulDist, 0f, lineWidth, lineDash, lineGap);
    }

    /// <summary>Update consumed distance on the line (for journey animation).</summary>
    public void UpdateLineConsumed(float t)
    {
        if (lineGO == null || !lineGO.activeSelf) return;
        float totalDist = lineCumulDist.Count > 1 ? lineCumulDist[lineCumulDist.Count - 1] : 0f;
        float consumed = t * totalDist;
        DashedRibbon.Build(lineMesh, lineWaypoints, lineCumulDist, consumed, lineWidth, lineDash, lineGap);
    }

    /// <summary>Hide the dashed line.</summary>
    public void HideLine()
    {
        if (lineGO != null) lineGO.SetActive(false);
    }

    void EnsureLine()
    {
        if (lineGO != null) return;

        lineGO = new GameObject("RoomRouteLine");
        lineGO.transform.SetParent(transform, true);
        lineGO.transform.localPosition = Vector3.zero;
        lineMF = lineGO.AddComponent<MeshFilter>();
        lineMR = lineGO.AddComponent<MeshRenderer>();

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        lineMat = new Material(sh);
        lineMat.SetFloat("_Surface", 1f);
        lineMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        lineMat.SetOverrideTag("RenderType", "Transparent");
        lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMat.SetInt("_ZWrite", 0);
        lineMat.SetFloat("_Cull", 0f);
        lineMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
        lineMR.sharedMaterial = lineMat;
        lineMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        lineMesh = new Mesh { name = "RoomRouteLine" };
        lineMF.sharedMesh = lineMesh;
        lineGO.SetActive(false);
    }
}
