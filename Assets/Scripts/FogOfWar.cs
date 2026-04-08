using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Thin builder that creates RoomTile objects and wires their connections.
/// All fog/outline logic lives in RoomTile itself.
/// </summary>
public class FogOfWar : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] float fogElevation   = 0.25f;
    [SerializeField] Color unknownColor   = new Color(0.01f, 0.01f, 0.02f, 1.0f);
    [SerializeField] Color discoveredColor = new Color(0.02f, 0.02f, 0.04f, 0.50f);
    [SerializeField] Color outlineColor   = new Color(0f, 0.85f, 1f, 0.35f);

    HexMapGenerator map;
    Material matUnknown, matDiscovered, matOutline;
    float outlineRadius;

    public Dictionary<Vector2Int, RoomTile> Tiles { get; private set; }
        = new Dictionary<Vector2Int, RoomTile>();

    // ── public API ────────────────────────

    public void Init(HexMapGenerator mapGen)
    {
        map = mapGen;

        // Tile outline = large room radius + half the corridor gap
        float largeR = map.RoomRadius(HexMapGenerator.RoomSize.Large);
        float halfGap = (map.HexRadiusValue * map.GridScaleValue - largeR) * 0.5f;
        outlineRadius = largeR + halfGap;

        CreateMaterials();
        BuildTiles();
        WireConnections();
    }

    public RoomTile GetTile(Vector2Int coord)
    {
        return Tiles.TryGetValue(coord, out var t) ? t : null;
    }

    public void Reveal(Vector2Int coord)
    {
        var tile = GetTile(coord);
        if (tile != null)
            tile.OnDroneEnter();
    }

    // ── build ─────────────────────────────

    void BuildTiles()
    {
        foreach (var room in map.RoomList)
        {
            var go = new GameObject($"Tile_{room.x}_{room.y}");
            go.transform.SetParent(transform, false);

            var tile = go.AddComponent<RoomTile>();
            tile.Init(room, map.RoomSizeMap[room], map, fogElevation, outlineRadius,
                      matUnknown, matDiscovered, matOutline);

            Tiles[room] = tile;
        }
    }

    void WireConnections()
    {
        foreach (var (a, b, type) in map.ConnectionList)
        {
            var tileA = GetTile(a);
            var tileB = GetTile(b);
            if (tileA == null || tileB == null) continue;

            // Passage fog geometry
            var (midA, midB) = map.PassageEndpoints(a, b);
            Vector3 midpoint = (midA + midB) * 0.5f;
            float w = map.PassageWidth(type) + 0.30f;
            float passFogY = type == HexMapGenerator.PassageType.Vent
                ? map.VentTopY(a, b) + fogElevation
                : map.PassageTopY(type) + fogElevation;

            // Each tile gets its half of the passage fog
            var fogA = tileA.BuildPassageFog(midA, midpoint, w, passFogY);
            var fogB = tileB.BuildPassageFog(midpoint, midB, w, passFogY);

            tileA.AddConnection(new TileConnection { neighbor = tileB, passageType = type, fogRenderer = fogA });
            tileB.AddConnection(new TileConnection { neighbor = tileA, passageType = type, fogRenderer = fogB });
        }
    }

    // ── materials ─────────────────────────

    void CreateMaterials()
    {
        matUnknown    = MakeFogMat(unknownColor);
        matDiscovered = MakeFogMat(discoveredColor);
        matOutline    = MakeFogMat(outlineColor);
    }

    Material MakeFogMat(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");

        var mat = new Material(sh);
        mat.color = c;
        mat.SetColor("_BaseColor", c);

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
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
