using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static builders for RoomTile meshes: fog lid, hex outline edges, flat hex, interaction highlight.
/// </summary>
public static class RoomTileMesh
{
    /// <summary>Hex lid mesh (top cap + side skirt down to floor).</summary>
    public static Mesh HexLid(Vector3 center, float radius, float y)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        verts.Add(new Vector3(center.x, y, center.z));
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * 60f * i;
            verts.Add(new Vector3(
                center.x + Mathf.Cos(a) * radius, y,
                center.z + Mathf.Sin(a) * radius));
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

    /// <summary>Single hex edge strip mesh for outline rendering.</summary>
    public static Mesh EdgeStrip(Vector3 center, float outerR, float innerR, float y, int edgeIndex)
    {
        float a1 = Mathf.Deg2Rad * 60f * edgeIndex;
        float a2 = Mathf.Deg2Rad * 60f * ((edgeIndex + 1) % 6);
        float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
        float c2 = Mathf.Cos(a2), s2 = Mathf.Sin(a2);

        Vector3 o1 = new Vector3(center.x + c1 * outerR, y, center.z + s1 * outerR);
        Vector3 o2 = new Vector3(center.x + c2 * outerR, y, center.z + s2 * outerR);
        Vector3 i1 = new Vector3(center.x + c1 * innerR, y, center.z + s1 * innerR);
        Vector3 i2 = new Vector3(center.x + c2 * innerR, y, center.z + s2 * innerR);

        var mesh = new Mesh { name = $"Edge{edgeIndex}" };
        mesh.SetVertices(new List<Vector3> { o1, i1, i2, o2 });
        mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Wider edge wedge for wall-hover highlight (covers one hex sector).</summary>
    public static Mesh EdgeWedge(Vector3 center, float outerR, float innerR, float y, int edgeIndex)
    {
        // Wedge covers the sector between two adjacent hex vertices with slight angular padding
        float a1 = Mathf.Deg2Rad * 60f * edgeIndex;
        float a2 = Mathf.Deg2Rad * 60f * ((edgeIndex + 1) % 6);
        float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
        float c2 = Mathf.Cos(a2), s2 = Mathf.Sin(a2);

        Vector3 o1 = new Vector3(center.x + c1 * outerR, y, center.z + s1 * outerR);
        Vector3 o2 = new Vector3(center.x + c2 * outerR, y, center.z + s2 * outerR);
        Vector3 i1 = new Vector3(center.x + c1 * innerR, y, center.z + s1 * innerR);
        Vector3 i2 = new Vector3(center.x + c2 * innerR, y, center.z + s2 * innerR);

        var mesh = new Mesh { name = $"Wedge{edgeIndex}" };
        mesh.SetVertices(new List<Vector3> { o1, i1, i2, o2 });
        mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Flat hex mesh for hover/flash interaction overlays.</summary>
    public static Mesh FlatHex(Vector3 center, float radius, float y)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        verts.Add(new Vector3(center.x, y, center.z));
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * 60f * i;
            verts.Add(new Vector3(
                center.x + Mathf.Cos(a) * radius, y,
                center.z + Mathf.Sin(a) * radius));
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
}
