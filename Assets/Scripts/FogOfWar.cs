using UnityEngine;
using System.Collections.Generic;

public enum FogState { Unknown, Discovered, Visible }

/// <summary>
/// Fog of war overlay for the hex map.
/// Three states: Unknown (opaque dark lid), Discovered (semi-transparent), Visible (hidden).
/// Units reveal their room + the near half of every connected passage.
/// </summary>
public class FogOfWar : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] float fogElevation   = 0.25f;
    [SerializeField] Color unknownColor   = new Color(0.01f, 0.01f, 0.02f, 1.0f);
    [SerializeField] Color discoveredColor = new Color(0.02f, 0.02f, 0.04f, 0.50f);

    HexMapGenerator map;
    Material matUnknown, matDiscovered;

    // per-room state + renderer
    Dictionary<Vector2Int, FogState> states = new Dictionary<Vector2Int, FogState>();
    Dictionary<Vector2Int, MeshRenderer> roomFog = new Dictionary<Vector2Int, MeshRenderer>();

    // passage-half fog: key = (thisRoom, neighbor)
    Dictionary<(Vector2Int, Vector2Int), MeshRenderer> passFog =
        new Dictionary<(Vector2Int, Vector2Int), MeshRenderer>();

    // ── public API ────────────────────────

    public void Init(HexMapGenerator mapGen)
    {
        map = mapGen;
        CreateMaterials();
        BuildOverlays();
    }

    public FogState GetState(Vector2Int room)
    {
        return states.TryGetValue(room, out var s) ? s : FogState.Unknown;
    }

    /// <summary>
    /// Call each frame (or when units move) with the hex coords of all unit positions.
    /// Visible rooms become Discovered, then unit rooms become Visible.
    /// </summary>
    public void UpdateVisibility(IEnumerable<Vector2Int> unitRooms)
    {
        // demote all currently Visible → Discovered
        var prev = new List<Vector2Int>();
        foreach (var kv in states)
            if (kv.Value == FogState.Visible) prev.Add(kv.Key);
        foreach (var r in prev)
            ApplyState(r, FogState.Discovered);

        // promote unit rooms → Visible
        foreach (var r in unitRooms)
            if (states.ContainsKey(r))
                ApplyState(r, FogState.Visible);
    }

    /// <summary>Directly set a room to Visible (e.g. starting room).</summary>
    public void Reveal(Vector2Int room)
    {
        if (states.ContainsKey(room))
            ApplyState(room, FogState.Visible);
    }

    // ── internals ─────────────────────────

    void ApplyState(Vector2Int room, FogState state)
    {
        states[room] = state;

        // room overlay
        if (roomFog.TryGetValue(room, out var mr))
            SetRenderer(mr, state);

        // this room's halves of connected passages
        foreach (var (a, b, _) in map.ConnectionList)
        {
            Vector2Int neighbor;
            if      (a == room) neighbor = b;
            else if (b == room) neighbor = a;
            else continue;

            if (passFog.TryGetValue((room, neighbor), out var pmr))
                SetRenderer(pmr, state);
        }
    }

    void SetRenderer(MeshRenderer mr, FogState state)
    {
        switch (state)
        {
            case FogState.Unknown:
                mr.enabled = true;
                mr.sharedMaterial = matUnknown;
                break;
            case FogState.Discovered:
                mr.enabled = true;
                mr.sharedMaterial = matDiscovered;
                break;
            case FogState.Visible:
                mr.enabled = false;
                break;
        }
    }

    // ── build overlays ────────────────────

    void BuildOverlays()
    {
        float fogY = map.WallHeight + fogElevation;

        foreach (var room in map.RoomList)
        {
            states[room] = FogState.Unknown;

            float r = map.RoomRadius(map.RoomSizeMap[room]) + 0.20f;
            Vector3 c = map.HexCenter(room);

            var go = SpawnFog($"FogRoom_{room.x}_{room.y}",
                              MakeHexLid(c, r, fogY), matUnknown);
            roomFog[room] = go.GetComponent<MeshRenderer>();
        }

        foreach (var (a, b, type) in map.ConnectionList)
        {
            var (midA, midB) = map.PassageEndpoints(a, b);
            Vector3 midpoint = (midA + midB) * 0.5f;
            float w = map.PassageWidth(type) + 0.30f;

            // half A (room A side)
            var goA = SpawnFog($"FogPass_{a.x}_{a.y}_{b.x}_{b.y}",
                               MakeRectLid(midA, midpoint, w, fogY), matUnknown);
            passFog[(a, b)] = goA.GetComponent<MeshRenderer>();

            // half B (room B side)
            var goB = SpawnFog($"FogPass_{b.x}_{b.y}_{a.x}_{a.y}",
                               MakeRectLid(midpoint, midB, w, fogY), matUnknown);
            passFog[(b, a)] = goB.GetComponent<MeshRenderer>();
        }
    }

    GameObject SpawnFog(string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    // ── fog mesh builders ─────────────────

    /// <summary>Hex top face + vertical skirt down to floor (blocks angled peeking).</summary>
    Mesh MakeHexLid(Vector3 center, float r, float y)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        // --- top hex (7 verts) ---
        verts.Add(new Vector3(center.x, y, center.z)); // 0 = center
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * 60f * i;
            verts.Add(new Vector3(
                center.x + Mathf.Cos(a) * r, y,
                center.z + Mathf.Sin(a) * r));
        }

        // top face triangles (upward-facing)
        for (int i = 0; i < 6; i++)
        {
            tris.Add(0);
            tris.Add(((i + 1) % 6) + 1);
            tris.Add(i + 1);
        }

        // --- skirt panels from y down to floor ---
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

            // outward-facing quad
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

    /// <summary>Rectangle top face + 4-side skirt for passage fog.</summary>
    Mesh MakeRectLid(Vector3 from, Vector3 to, float width, float y)
    {
        Vector3 dir  = (to - from); dir.y = 0; dir = dir.normalized;
        Vector3 perp = Vector3.Cross(Vector3.up, dir).normalized;
        float hw = width * 0.5f;

        Vector3 a = new Vector3(from.x, y, from.z);
        Vector3 b = new Vector3(to.x,   y, to.z);

        // top quad (4 verts): same winding as map floor quads
        Vector3 v0 = a + perp * hw;
        Vector3 v1 = a - perp * hw;
        Vector3 v2 = b - perp * hw;
        Vector3 v3 = b + perp * hw;

        var verts = new List<Vector3> { v0, v1, v2, v3 };
        var tris  = new List<int> { 0, 1, 2, 0, 2, 3 };

        // skirt on 4 edges
        float floorY = -0.05f;
        AddSkirtEdge(verts, tris, v0, v3, floorY); // +perp side
        AddSkirtEdge(verts, tris, v2, v1, floorY); // -perp side
        AddSkirtEdge(verts, tris, v1, v0, floorY); // from-end
        AddSkirtEdge(verts, tris, v3, v2, floorY); // to-end

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

    // ── materials ─────────────────────────

    void CreateMaterials()
    {
        matUnknown    = MakeFogMat(unknownColor);
        matDiscovered = MakeFogMat(discoveredColor);
    }

    Material MakeFogMat(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");

        var mat = new Material(sh);
        mat.color = c;
        mat.SetColor("_BaseColor", c);

        // Transparent so overlapping fog from neighbors doesn't Z-fight
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_Cull", 0); // double-sided
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        return mat;
    }
}
