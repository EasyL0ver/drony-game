using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static utility for generating procedural mesh primitives.
/// Used by all visual builders (stations, drones, barriers, etc.)
/// </summary>
public static class MeshPrimitives
{
    /// <summary>Axis-aligned box mesh.</summary>
    public static Mesh Box(Vector3 center, float sizeX, float sizeY, float sizeZ)
    {
        float hx = sizeX * 0.5f, hy = sizeY * 0.5f, hz = sizeZ * 0.5f;
        var verts = new Vector3[]
        {
            center + new Vector3(-hx, -hy, -hz),
            center + new Vector3( hx, -hy, -hz),
            center + new Vector3( hx,  hy, -hz),
            center + new Vector3(-hx,  hy, -hz),
            center + new Vector3(-hx, -hy,  hz),
            center + new Vector3( hx, -hy,  hz),
            center + new Vector3( hx,  hy,  hz),
            center + new Vector3(-hx,  hy,  hz),
        };
        var tris = new[]
        {
            0,2,1, 0,3,2,  4,5,6, 4,6,7,
            0,1,5, 0,5,4,  2,3,7, 2,7,6,
            0,4,7, 0,7,3,  1,2,6, 1,6,5,
        };
        var m = new Mesh { vertices = verts, triangles = tris };
        m.RecalculateNormals();
        return m;
    }

    /// <summary>Box mesh rotated around Z axis by angleDeg.</summary>
    public static Mesh RotatedBox(Vector3 center, float len, float width, float depth, float angleDeg)
    {
        float hl = len * 0.5f, hw = width * 0.5f, hd = depth * 0.5f;
        var verts = new Vector3[8];
        verts[0] = new Vector3(-hl, -hw, -hd);
        verts[1] = new Vector3( hl, -hw, -hd);
        verts[2] = new Vector3( hl,  hw, -hd);
        verts[3] = new Vector3(-hl,  hw, -hd);
        verts[4] = new Vector3(-hl, -hw,  hd);
        verts[5] = new Vector3( hl, -hw,  hd);
        verts[6] = new Vector3( hl,  hw,  hd);
        verts[7] = new Vector3(-hl,  hw,  hd);

        Quaternion rot = Quaternion.Euler(0, 0, angleDeg);
        for (int i = 0; i < 8; i++)
            verts[i] = rot * verts[i] + center;

        var tris = new[]
        {
            0,2,1, 0,3,2,  4,5,6, 4,6,7,
            0,1,5, 0,5,4,  2,3,7, 2,7,6,
            0,4,7, 0,7,3,  1,2,6, 1,6,5,
        };

        var m = new Mesh { vertices = verts, triangles = tris };
        m.RecalculateNormals();
        return m;
    }

    /// <summary>Regular polygon prism (hex, octagon, etc.).</summary>
    public static Mesh Prism(float radius, float halfHeight, int sides, float yOffset = 0f, Vector3 offset = default)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        // Top and bottom cap vertices
        for (int i = 0; i < sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            float x = Mathf.Cos(angle) * radius + offset.x;
            float z = Mathf.Sin(angle) * radius + offset.z;
            verts.Add(new Vector3(x, yOffset + halfHeight + offset.y, z));  // top
            verts.Add(new Vector3(x, yOffset - halfHeight + offset.y, z));  // bottom
        }

        // Side faces
        for (int i = 0; i < sides; i++)
        {
            int t0 = i * 2;
            int b0 = i * 2 + 1;
            int t1 = ((i + 1) % sides) * 2;
            int b1 = ((i + 1) % sides) * 2 + 1;
            tris.AddRange(new[] { t0, t1, b1, t0, b1, b0 });
        }

        // Top cap
        int topCenter = verts.Count;
        verts.Add(new Vector3(offset.x, yOffset + halfHeight + offset.y, offset.z));
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            tris.AddRange(new[] { topCenter, i * 2, next * 2 });
        }

        // Bottom cap
        int botCenter = verts.Count;
        verts.Add(new Vector3(offset.x, yOffset - halfHeight + offset.y, offset.z));
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            tris.AddRange(new[] { botCenter, next * 2 + 1, i * 2 + 1 });
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    /// <summary>Ring (hollow cylinder).</summary>
    public static Mesh Ring(float outerRadius, float innerRadius, float halfHeight, float yOffset, int sides, Vector3 offset = default)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        for (int i = 0; i < sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            // outer top, outer bot, inner top, inner bot
            verts.Add(new Vector3(cos * outerRadius + offset.x, yOffset + halfHeight + offset.y, sin * outerRadius + offset.z));
            verts.Add(new Vector3(cos * outerRadius + offset.x, yOffset - halfHeight + offset.y, sin * outerRadius + offset.z));
            verts.Add(new Vector3(cos * innerRadius + offset.x, yOffset + halfHeight + offset.y, sin * innerRadius + offset.z));
            verts.Add(new Vector3(cos * innerRadius + offset.x, yOffset - halfHeight + offset.y, sin * innerRadius + offset.z));
        }

        for (int i = 0; i < sides; i++)
        {
            int cur = i * 4;
            int next = ((i + 1) % sides) * 4;

            // Outer wall
            tris.AddRange(new[] { cur, next, next + 1, cur, next + 1, cur + 1 });
            // Inner wall
            tris.AddRange(new[] { cur + 2, next + 3, next + 2, cur + 2, cur + 3, next + 3 });
            // Top face
            tris.AddRange(new[] { cur, cur + 2, next + 2, cur, next + 2, next });
            // Bottom face
            tris.AddRange(new[] { cur + 1, next + 3, cur + 3, cur + 1, next + 1, next + 3 });
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    /// <summary>Flat quad mesh (two triangles).</summary>
    public static Mesh Quad(Vector3 center, float width, float height, Vector3 normal)
    {
        Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(Vector3.forward, normal).normalized;
        Vector3 up = Vector3.Cross(normal, right).normalized;

        float hw = width * 0.5f, hh = height * 0.5f;
        var verts = new Vector3[]
        {
            center - right * hw - up * hh,
            center + right * hw - up * hh,
            center + right * hw + up * hh,
            center - right * hw + up * hh,
        };
        var m = new Mesh
        {
            vertices = verts,
            triangles = new[] { 0, 2, 1, 0, 3, 2 }
        };
        m.RecalculateNormals();
        return m;
    }

    /// <summary>Pyramid (cone with flat base).</summary>
    public static Mesh Pyramid(float radius, float height, float baseY, int sides = 6)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        // Apex
        verts.Add(new Vector3(0, baseY + height, 0));

        // Base ring
        for (int i = 0; i < sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            verts.Add(new Vector3(Mathf.Cos(angle) * radius, baseY, Mathf.Sin(angle) * radius));
        }

        // Side faces
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            tris.AddRange(new[] { 0, i + 1, next + 1 });
        }

        // Base cap
        int baseCenter = verts.Count;
        verts.Add(new Vector3(0, baseY, 0));
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            tris.AddRange(new[] { baseCenter, next + 1, i + 1 });
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    /// <summary>Spawn a mesh as a child GameObject with renderer and optional collider.</summary>
    public static GameObject Spawn(Transform parent, string name, Mesh mesh, Material mat, bool addCollider = false)
    {
        if (mesh.vertexCount == 0) return new GameObject(name);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (addCollider)
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }
}
