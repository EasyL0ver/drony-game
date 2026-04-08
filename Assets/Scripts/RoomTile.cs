using UnityEngine;
using System.Collections.Generic;

public enum FogState { Unknown, Discovered, Visible }

/// <summary>
/// A connection from this tile to a neighbor, through a specific passage type.
/// Future: hazards, items, blockages in the passage.
/// </summary>
[System.Serializable]
public class TileConnection
{
    public RoomTile neighbor;
    public HexMapGenerator.PassageType passageType;

    // Fog mesh covering this tile's half of the passage
    public MeshRenderer fogRenderer;
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

    public List<TileConnection> Connections { get; private set; } = new List<TileConnection>();

    // Drone tracking
    int droneCount;

    // Visuals
    MeshRenderer fogRenderer;
    GameObject outlineObject;
    Material matUnknown, matDiscovered, matOutline;

    // Config (set once by builder)
    float fogElevation;
    float outlineRadius;

    // ── setup (called by builder) ────────────

    public void Init(Vector2Int coord, HexMapGenerator.RoomSize size,
                     HexMapGenerator map, float fogElev, float outlineR,
                     Material unknown, Material discovered, Material outline)
    {
        Coord = coord;
        Size = size;
        fogElevation = fogElev;
        outlineRadius = outlineR;
        matUnknown = unknown;
        matDiscovered = discovered;
        matOutline = outline;

        BuildFogMesh(map);
        BuildOutlineMesh(map);
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

        // Update this tile's passage fog halves
        foreach (var conn in Connections)
            SetPassageFog(conn);
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

    void SetPassageFog(TileConnection conn)
    {
        if (conn.fogRenderer == null) return;

        switch (State)
        {
            case FogState.Unknown:
                conn.fogRenderer.enabled = true;
                conn.fogRenderer.sharedMaterial = matUnknown;
                break;
            case FogState.Discovered:
                conn.fogRenderer.enabled = true;
                conn.fogRenderer.sharedMaterial = matDiscovered;
                break;
            case FogState.Visible:
                conn.fogRenderer.enabled = false;
                break;
        }
    }

    public void ShowOutline(bool show)
    {
        if (outlineObject != null)
            outlineObject.SetActive(show);
    }

    // ── mesh builders ────────────────────────

    void BuildFogMesh(HexMapGenerator map)
    {
        float fogY = map.WallHeight + fogElevation;
        float r = map.RoomRadius(Size) + 0.20f;
        Vector3 center = map.HexCenter(Coord);

        var go = new GameObject("Fog");
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        fogRenderer = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = MakeHexLid(center, r, fogY);
        fogRenderer.sharedMaterial = matUnknown;
        fogRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fogRenderer.receiveShadows = false;
    }

    void BuildOutlineMesh(HexMapGenerator map)
    {
        float fogY = map.WallHeight + fogElevation + 0.02f;
        Vector3 center = map.HexCenter(Coord);
        float r = outlineRadius;
        float thickness = 0.08f;

        outlineObject = new GameObject("Outline");
        outlineObject.transform.SetParent(transform, false);
        var mf = outlineObject.AddComponent<MeshFilter>();
        var mr = outlineObject.AddComponent<MeshRenderer>();
        mf.sharedMesh = MakeHexRing(center, r, r - thickness, fogY);
        mr.sharedMaterial = matOutline;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        outlineObject.SetActive(false);
    }

    /// <summary>
    /// Creates a passage fog half-piece. Called by the builder for each connection.
    /// Returns the MeshRenderer to store on TileConnection.
    /// </summary>
    public MeshRenderer BuildPassageFog(Vector3 from, Vector3 to, float width, float y)
    {
        var go = new GameObject($"PassFog_{Coord.x}_{Coord.y}");
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = MakeRectLid(from, to, width, y);
        mr.sharedMaterial = matUnknown;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    // ── hex lid mesh (same as old FogOfWar) ──

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

    // ── hex ring (outline) ───────────────────

    Mesh MakeHexRing(Vector3 center, float outerR, float innerR, float y)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        int n = 6;

        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Deg2Rad * 60f * i;
            float co = Mathf.Cos(a), si = Mathf.Sin(a);
            verts.Add(new Vector3(center.x + co * outerR, y, center.z + si * outerR));
            verts.Add(new Vector3(center.x + co * innerR, y, center.z + si * innerR));
        }

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int o = i * 2, inn = i * 2 + 1;
            int no = next * 2, ni = next * 2 + 1;
            tris.Add(o);   tris.Add(no);  tris.Add(inn);
            tris.Add(inn); tris.Add(no);  tris.Add(ni);
        }

        var m = new Mesh { name = "OutlineHex" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ── rect lid (passage fog) ───────────────

    Mesh MakeRectLid(Vector3 from, Vector3 to, float width, float y)
    {
        Vector3 dir  = (to - from); dir.y = 0; dir = dir.normalized;
        Vector3 perp = Vector3.Cross(Vector3.up, dir).normalized;
        float hw = width * 0.5f;

        Vector3 a = new Vector3(from.x, y, from.z);
        Vector3 b = new Vector3(to.x,   y, to.z);

        Vector3 v0 = a + perp * hw;
        Vector3 v1 = a - perp * hw;
        Vector3 v2 = b - perp * hw;
        Vector3 v3 = b + perp * hw;

        var verts = new List<Vector3> { v0, v1, v2, v3 };
        var tris  = new List<int> { 0, 1, 2, 0, 2, 3 };

        float floorY = -0.05f;
        AddSkirtEdge(verts, tris, v0, v3, floorY);
        AddSkirtEdge(verts, tris, v2, v1, floorY);
        AddSkirtEdge(verts, tris, v1, v0, floorY);
        AddSkirtEdge(verts, tris, v3, v2, floorY);

        var m = new Mesh { name = "FogRect" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void AddSkirtEdge(List<Vector3> verts, List<int> tris, Vector3 topA, Vector3 topB, float floorY)
    {
        int v = verts.Count;
        Vector3 botA = new Vector3(topA.x, floorY, topA.z);
        Vector3 botB = new Vector3(topB.x, floorY, topB.z);
        verts.Add(topA); verts.Add(botA); verts.Add(botB); verts.Add(topB);
        tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
        tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
    }
}
