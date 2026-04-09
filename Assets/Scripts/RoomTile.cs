using UnityEngine;
using System.Collections.Generic;

public enum FogState { Unknown, Scanning, Discovered, Visible }

/// <summary>
/// A connection from this tile to a neighbor, through a specific passage type.
/// Future: hazards, items, blockages in the passage.
/// </summary>
[System.Serializable]
public class TileConnection
{
    public RoomTile neighbor;
    public HexMapGenerator.PassageType passageType;
    public int edgeIndex; // which hex edge (0-5) this neighbor is on
}

/// <summary>
/// Self-contained room tile. Owns its fog state, fog/outline visuals,
/// and connections to neighbors. Reveals itself when a drone enters.
/// </summary>
public class RoomTile : MonoBehaviour
{
    public Vector2Int Coord { get; private set; }
    public HexMapGenerator.RoomSize Size { get; private set; }
    public FogState State { get; private set; } = FogState.Unknown;
    public float ScanProgress => scanProgress;
    public float ScanTimeLeft => State == FogState.Scanning
        ? scanDuration * (1f - scanProgress) : 0f;
    public float ScanElapsed => scanDuration * scanProgress;
    public float ScanTotalTime => scanDuration;

    public List<TileConnection> Connections { get; private set; } = new List<TileConnection>();

    // Drone tracking
    int droneCount;

    // Visuals
    MeshRenderer fogRenderer;
    GameObject[] outlineEdges = new GameObject[6]; // one quad per hex edge
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

    // Scanning
    float scanDuration = 3f;
    float scanProgress;

    // Config (set once by builder)
    float fogElevation;
    float outlineRadius;

    // ── setup (called by builder) ────────────

    public void Init(Vector2Int coord, HexMapGenerator.RoomSize size,
                     HexMapGenerator map, float fogElev, float outlineR,
                     Material unknown, Material discovered, Material outline,
                     float scanDur = 3f)
    {
        Coord = coord;
        Size = size;
        fogElevation = fogElev;
        outlineRadius = outlineR;
        scanDuration = scanDur;
        matUnknown = unknown;
        matDiscovered = discovered;
        matOutline = outline;

        // Bright version of outline for hover
        matOutlineHover = new Material(outline);
        Color hc = outline.color;
        hc = Color.Lerp(hc, Color.white, 0.35f);
        hc.a = Mathf.Min(1f, outline.color.a * 2.2f);
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

    /// <summary>
    /// Call when a drone enters this room.
    /// Reveals this tile and shows outlines on unknown neighbors.
    /// </summary>
    public void OnDroneEnter()
    {
        droneCount++;
    }

    /// <summary>
    /// Call when a drone physically arrives in this room (not just heading toward it).
    /// Unknown rooms begin scanning; Discovered rooms go straight to Visible.
    /// </summary>
    public void OnDroneArrived()
    {
        switch (State)
        {
            case FogState.Unknown:
                scanProgress = 0f;
                SetState(FogState.Scanning);
                foreach (var conn in Connections)
                {
                    if (conn.neighbor.State == FogState.Unknown)
                        conn.neighbor.ShowOutline(true);
                }
                break;
            case FogState.Scanning:
                // Already scanning — additional drone helps (resumes if paused)
                break;
            case FogState.Discovered:
                SetState(FogState.Visible);
                foreach (var conn in Connections)
                {
                    if (conn.neighbor.State == FogState.Unknown)
                        conn.neighbor.ShowOutline(true);
                }
                break;
        }
    }

    /// <summary>
    /// Instantly reveal this tile — used for the starting base room.
    /// </summary>
    public void RevealImmediate()
    {
        scanProgress = 1f;
        SetState(FogState.Visible);
        foreach (var conn in Connections)
        {
            if (conn.neighbor.State == FogState.Unknown)
                conn.neighbor.ShowOutline(true);
        }
    }

    /// <summary>
    /// Call when a drone leaves this room.
    /// Only demotes to Discovered when the last drone leaves.
    /// </summary>
    public void OnDroneExit()
    {
        droneCount = Mathf.Max(0, droneCount - 1);
        if (droneCount == 0 && State == FogState.Visible)
            SetState(FogState.Discovered);
    }

    // ── state management ─────────────────────

    void SetState(FogState newState)
    {
        State = newState;
        ApplyVisuals();
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

    public void SetHovered(bool hovered)
    {
        isHovered = hovered;
        if (hoverHighlight != null)
        {
            hoverHighlight.SetActive(hovered);
            if (hovered)
            {
                // Above fog when fog visible, at floor level when revealed
                float yOff = (fogRenderer != null && fogRenderer.enabled)
                    ? fogMeshY : 0f;
                hoverHighlight.transform.localPosition = new Vector3(0f, yOff, 0f);
            }
        }

        // Brighten outline edges on hover
        if (outlineShown)
        {
            Material mat = hovered ? matOutlineHover : matOutline;
            for (int i = 0; i < 6; i++)
            {
                if (outlineEdges[i] != null)
                    outlineEdges[i].GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }
    }

    public void FlashMoveTarget()
    {
        flashTimer = flashDuration;
        if (moveFlash != null)
            moveFlash.SetActive(true);
    }

    void Update()
    {
        if (isHovered && matHover != null)
        {
            Color c = new Color(1f, 1f, 1f, 0.18f);
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

        // Scanning progress
        if (State == FogState.Scanning)
        {
            if (droneCount > 0)
            {
                scanProgress += Time.deltaTime / scanDuration;
                if (scanProgress >= 1f)
                {
                    scanProgress = 1f;
                    SetState(FogState.Visible);
                    return;
                }
            }
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
        mf.sharedMesh = MakeHexLid(center, outlineRadius, fogMeshY);
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
            float a1 = Mathf.Deg2Rad * 60f * i;
            float a2 = Mathf.Deg2Rad * 60f * ((i + 1) % 6);
            float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
            float c2 = Mathf.Cos(a2), s2 = Mathf.Sin(a2);

            Vector3 o1 = new Vector3(center.x + c1 * outerR, fogY, center.z + s1 * outerR);
            Vector3 o2 = new Vector3(center.x + c2 * outerR, fogY, center.z + s2 * outerR);
            Vector3 i1 = new Vector3(center.x + c1 * innerR, fogY, center.z + s1 * innerR);
            Vector3 i2 = new Vector3(center.x + c2 * innerR, fogY, center.z + s2 * innerR);

            var mesh = new Mesh { name = $"Edge{i}" };
            mesh.SetVertices(new List<Vector3> { o1, i1, i2, o2 });
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

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

    // ── hex lid mesh ─────────────────────────

    Mesh MakeHexLid(Vector3 center, float r, float y)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        verts.Add(new Vector3(center.x, y, center.z));
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * 60f * i;
            verts.Add(new Vector3(
                center.x + Mathf.Cos(a) * r, y,
                center.z + Mathf.Sin(a) * r));
        }

        for (int i = 0; i < 6; i++)
        {
            tris.Add(0);
            tris.Add(((i + 1) % 6) + 1);
            tris.Add(i + 1);
        }

        float floorY = -0.05f;
        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            Vector3 topA = verts[i + 1];
            Vector3 topB = verts[next + 1];
            Vector3 botA = new Vector3(topA.x, floorY, topA.z);
            Vector3 botB = new Vector3(topB.x, floorY, topB.z);

            int v = verts.Count;
            verts.Add(topA); verts.Add(botA); verts.Add(botB); verts.Add(topB);
            tris.Add(v);     tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v);     tris.Add(v + 2); tris.Add(v + 3);
        }

        var m = new Mesh { name = "FogHex" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ── interaction meshes ───────────────────

    void BuildInteractionMeshes(HexMapGenerator map)
    {
        Vector3 center = map.HexCenter(Coord);
        Mesh hex = MakeFlatHex(center, outlineRadius * 0.97f, 0.03f);

        // Hover highlight
        hoverHighlight = new GameObject("Hover");
        hoverHighlight.transform.SetParent(transform, false);
        var mf1 = hoverHighlight.AddComponent<MeshFilter>();
        hoverRenderer = hoverHighlight.AddComponent<MeshRenderer>();
        mf1.sharedMesh = hex;
        matHover = MakeInteractionMat(new Color(1f, 1f, 1f, 0.18f));
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
    }

    Mesh MakeFlatHex(Vector3 center, float r, float y)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        verts.Add(new Vector3(center.x, y, center.z));
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * 60f * i;
            verts.Add(new Vector3(
                center.x + Mathf.Cos(a) * r, y,
                center.z + Mathf.Sin(a) * r));
        }

        for (int i = 0; i < 6; i++)
        {
            tris.Add(0);
            tris.Add(((i + 1) % 6) + 1);
            tris.Add(i + 1);
        }

        var m = new Mesh { name = "FlatHex" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
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
