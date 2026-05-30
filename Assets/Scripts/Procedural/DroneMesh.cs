using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static builder for the low-poly drone mesh (hex body, arms, rotors, eye).
/// Returns rotor transforms so the caller can spin them.
/// </summary>
public static class DroneMesh
{
    public struct Result
    {
        public Transform[] rotors;
    }

    public static Result Build(Transform parent, float bodyRadius, float armLength,
        Material matHull, Material matArm, Material matGlow)
    {
        float bR = bodyRadius;
        float bH = bR * 0.3f;
        float motorR = bR * 0.28f;
        float motorH = bR * 0.22f;
        float rotorR = bR * 0.6f;
        float rotorT = 0.008f;

        MeshPrimitives.Spawn(parent, "Body", HexPrism(bR, bH, 6), matHull);
        MeshPrimitives.Spawn(parent, "Canopy", Pyramid(bR * 0.45f, bH * 1.5f, bH), matHull);
        MeshPrimitives.Spawn(parent, "GlowRing", Ring(bR * 1.02f, bR * 0.88f, bH * 0.3f, bH * 0.5f, 6), matGlow);
        MeshPrimitives.Spawn(parent, "Eye", Eye(bR, bH), matGlow);
        MeshPrimitives.Spawn(parent, "Belly", HexPrism(bR * 0.2f, 0.005f, 6, -bH), matGlow);

        var rotors = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            float angle = (i * 90f + 45f) * Mathf.Deg2Rad;
            float dx = Mathf.Cos(angle);
            float dz = Mathf.Sin(angle);

            Vector3 armEnd = new Vector3(dx * (bR + armLength), 0, dz * (bR + armLength));
            Vector3 armMid = new Vector3(dx * (bR + armLength * 0.5f), 0, dz * (bR + armLength * 0.5f));

            MeshPrimitives.Spawn(parent, $"Arm{i}", ArmBar(armMid, armLength, bR * 0.12f, bR * 0.1f, angle), matArm);
            MeshPrimitives.Spawn(parent, $"Motor{i}", HexPrism(motorR, motorH, 6, 0, armEnd), matArm);
            MeshPrimitives.Spawn(parent, $"MotorGlow{i}", Ring(motorR * 1.15f, motorR * 0.85f, motorH * 0.3f, motorH * 0.5f, 6, armEnd), matGlow);

            var rotorGO = MeshPrimitives.Spawn(parent, $"Rotor{i}", TwoBlade(rotorR, rotorT, bR * 0.06f), matArm);
            rotorGO.transform.localPosition = armEnd + Vector3.up * (motorH + 0.005f);
            rotors[i] = rotorGO.transform;
        }

        // Skid legs
        for (int i = 0; i < 2; i++)
        {
            float zOff = (i == 0 ? 1f : -1f) * bR * 0.55f;
            Vector3 c = new Vector3(0, -bH - bR * 0.08f, zOff);
            MeshPrimitives.Spawn(parent, $"Skid{i}", MeshPrimitives.Box(c, bR * 0.7f, 0.006f, 0.008f), matArm);
        }

        return new Result { rotors = rotors };
    }

    // ── mesh primitives specific to drone ────────────────

    public static Mesh HexPrism(float r, float halfH, int sides, float yOff = 0, Vector3 off = default)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        int n = sides;

        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            float x = Mathf.Cos(a) * r + off.x;
            float z = Mathf.Sin(a) * r + off.z;
            verts.Add(new Vector3(x, halfH + yOff + off.y, z));
            verts.Add(new Vector3(x, -halfH + yOff + off.y, z));
        }
        verts.Add(new Vector3(off.x, halfH + yOff + off.y, off.z));
        verts.Add(new Vector3(off.x, -halfH + yOff + off.y, off.z));

        int tc = 2 * n;
        int bc = 2 * n + 1;

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            tris.Add(tc); tris.Add(i * 2); tris.Add(next * 2);
            tris.Add(bc); tris.Add(next * 2 + 1); tris.Add(i * 2 + 1);
            tris.Add(i * 2); tris.Add(i * 2 + 1); tris.Add(next * 2 + 1);
            tris.Add(i * 2); tris.Add(next * 2 + 1); tris.Add(next * 2);
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    static Mesh Pyramid(float r, float h, float baseY)
    {
        int n = 6;
        var verts = new List<Vector3>();
        var tris = new List<int>();

        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            verts.Add(new Vector3(Mathf.Cos(a) * r, baseY, Mathf.Sin(a) * r));
        }
        verts.Add(new Vector3(0, baseY + h, 0));
        verts.Add(new Vector3(0, baseY, 0));

        int apex = n;
        int bc = n + 1;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            tris.Add(i); tris.Add(apex); tris.Add(next);
            tris.Add(next); tris.Add(bc); tris.Add(i);
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    static Mesh Ring(float outerR, float innerR, float halfH, float yOff, int sides, Vector3 off = default)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        int n = sides;

        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            float co = Mathf.Cos(a), si = Mathf.Sin(a);
            verts.Add(new Vector3(co * outerR + off.x, halfH + yOff + off.y, si * outerR + off.z));
            verts.Add(new Vector3(co * outerR + off.x, -halfH + yOff + off.y, si * outerR + off.z));
            verts.Add(new Vector3(co * innerR + off.x, halfH + yOff + off.y, si * innerR + off.z));
            verts.Add(new Vector3(co * innerR + off.x, -halfH + yOff + off.y, si * innerR + off.z));
        }

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int ot = i * 4, ob = i * 4 + 1, it = i * 4 + 2, ib = i * 4 + 3;
            int not_ = next * 4, nob = next * 4 + 1, nit = next * 4 + 2, nib = next * 4 + 3;

            tris.Add(ot); tris.Add(ob); tris.Add(nob);
            tris.Add(ot); tris.Add(nob); tris.Add(not_);
            tris.Add(nit); tris.Add(nib); tris.Add(ib);
            tris.Add(nit); tris.Add(ib); tris.Add(it);
            tris.Add(ot); tris.Add(not_); tris.Add(nit);
            tris.Add(ot); tris.Add(nit); tris.Add(it);
            tris.Add(nob); tris.Add(ob); tris.Add(ib);
            tris.Add(nob); tris.Add(ib); tris.Add(nib);
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    static Mesh Eye(float bR, float bH)
    {
        float s = bR * 0.12f;
        float fwd = bR * 0.98f;
        Vector3 c = new Vector3(0, bH * 0.4f, fwd);
        var verts = new Vector3[]
        {
            c + new Vector3(-s, 0, 0),
            c + new Vector3(0, s * 0.7f, 0),
            c + new Vector3(s, 0, 0),
            c + new Vector3(0, -s * 0.7f, 0),
        };
        var m = new Mesh { vertices = verts, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
        m.RecalculateNormals();
        return m;
    }

    static Mesh ArmBar(Vector3 center, float len, float w, float h, float angle)
    {
        var verts = new Vector3[8];
        float hl = len * 0.5f, hw = w * 0.5f, hh = h * 0.5f;
        verts[0] = new Vector3(-hl, -hh, -hw);
        verts[1] = new Vector3(hl, -hh, -hw);
        verts[2] = new Vector3(hl, hh, -hw);
        verts[3] = new Vector3(-hl, hh, -hw);
        verts[4] = new Vector3(-hl, -hh, hw);
        verts[5] = new Vector3(hl, -hh, hw);
        verts[6] = new Vector3(hl, hh, hw);
        verts[7] = new Vector3(-hl, hh, hw);

        Quaternion rot = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
        for (int i = 0; i < 8; i++)
            verts[i] = rot * verts[i] + center;

        var tris = new[]
        {
            0,2,1, 0,3,2,
            4,5,6, 4,6,7,
            0,1,5, 0,5,4,
            2,3,7, 2,7,6,
            0,4,7, 0,7,3,
            1,2,6, 1,6,5,
        };

        var m = new Mesh { vertices = verts, triangles = tris };
        m.RecalculateNormals();
        return m;
    }

    static Mesh TwoBlade(float r, float thickness, float bladeW)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        float ht = thickness * 0.5f;
        float hw = bladeW * 0.5f;

        AddBlade(verts, tris, new Vector3(-r, 0, 0), new Vector3(r, 0, 0), hw, ht);
        AddBlade(verts, tris, new Vector3(0, 0, -r), new Vector3(0, 0, r), hw, ht);

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    static void AddBlade(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, float hw, float ht)
    {
        Vector3 dir = (b - a).normalized;
        Vector3 perp = new Vector3(-dir.z, 0, dir.x);

        int v = verts.Count;

        verts.Add(a - perp * hw + Vector3.up * ht);
        verts.Add(a + perp * hw + Vector3.up * ht);
        verts.Add(b + perp * hw + Vector3.up * ht);
        verts.Add(b - perp * hw + Vector3.up * ht);
        verts.Add(a - perp * hw - Vector3.up * ht);
        verts.Add(a + perp * hw - Vector3.up * ht);
        verts.Add(b + perp * hw - Vector3.up * ht);
        verts.Add(b - perp * hw - Vector3.up * ht);

        var idx = new[]
        {
            0,1,2, 0,2,3,
            4,6,5, 4,7,6,
            0,4,5, 0,5,1,
            2,6,7, 2,7,3,
            0,3,7, 0,7,4,
            1,5,6, 1,6,2,
        };

        for (int i = 0; i < idx.Length; i++)
            tris.Add(idx[i] + v);
    }
}
