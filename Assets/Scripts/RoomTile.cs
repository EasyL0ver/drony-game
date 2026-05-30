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
    /// Get the Passage wall entity for the given neighbor room, or null if none.
    /// </summary>
    public Passage GetPassage(Vector2Int neighbor)
    {
        foreach (var p in GetComponentsInChildren<Passage>())
            if (p.Neighbor == neighbor) return p;
        return null;
    }

    // Visuals
    MeshRenderer fogRenderer;
    GameObject[] outlineEdges = new GameObject[6];
    bool outlineShown;
    Material matUnknown, matDiscovered, matOutline, matOutlineHover;

    // Interaction
    GameObject hoverHighlight;
    GameObject moveFlash;
    MeshRenderer hoverRenderer;
    MeshRenderer flashRenderer;
    Material matHover, matFlash;
    bool isHovered;
    float flashTimer;
    const float flashDuration = 0.5f;
    float fogMeshY;

    // Drone presence indicator (shown on unknown tiles when a drone is inside)
    HashSet<DroneController> dronesPresent = new HashSet<DroneController>();
    GameObject droneLabel;
    TextMesh droneLabelText;

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

    public void SetHovered(bool hovered, WallView hoveredWall = null)
    {
        isHovered = hovered;
        if (hoverHighlight != null)
        {
            hoverHighlight.SetActive(hovered);
            if (hovered)
            {
                // Above fog for hidden tiles, at floor level for discovered/visible
                bool opaqueHidden = State == FogState.Unknown || State == FogState.Scanning;
                float yOff = opaqueHidden ? fogMeshY : 0f;
                hoverHighlight.transform.localPosition = new Vector3(0f, yOff, 0f);
            }
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
        if (isHovered && matHover != null)
        {
            Color c = new Color(1f, 1f, 1f, 0.08f);
            matHover.color = c;
            matHover.SetColor("_BaseColor", c);
        }

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
        Mesh hex = RoomTileMesh.FlatHex(center, outlineRadius * 0.97f, 0.03f);

        // Hover highlight
        hoverHighlight = new GameObject("Hover");
        hoverHighlight.transform.SetParent(transform, false);
        var mf1 = hoverHighlight.AddComponent<MeshFilter>();
        hoverRenderer = hoverHighlight.AddComponent<MeshRenderer>();
        mf1.sharedMesh = hex;
        matHover = MakeInteractionMat(new Color(1f, 1f, 1f, 0.08f));
        hoverRenderer.sharedMaterial = matHover;
        hoverRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        hoverRenderer.receiveShadows = false;
        hoverHighlight.SetActive(false);

        // Move flash
        moveFlash = new GameObject("MoveFlash");
        moveFlash.transform.SetParent(transform, false);
        var mf2 = moveFlash.AddComponent<MeshFilter>();
        flashRenderer = moveFlash.AddComponent<MeshRenderer>();
        mf2.sharedMesh = hex;
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

}
