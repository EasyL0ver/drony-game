using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Low-poly procedural drone that matches the hex-map art style.
/// Tiny geometric shapes — hex body, thin arms, flat 2-blade rotors.
/// </summary>
public class LowPolyDrone : MonoBehaviour
{
    [Header("Scale")]
    [SerializeField] float bodyRadius = 0.15f;
    [SerializeField] float armLength  = 0.18f;
    [SerializeField] float rotorSpeed = 2800f;

    [Header("Colors")]
    [SerializeField] Color hullColor  = new Color(0.12f, 0.12f, 0.15f);
    [SerializeField] Color armColor   = new Color(0.08f, 0.08f, 0.10f);
    [SerializeField] Color glowColor  = new Color(0f, 0.85f, 1f);
    [SerializeField] float glowIntensity = 4f;

    Transform[] rotors;
    Material matHull, matArm, matGlow;
    float baseLocalY;

    // ── lifecycle ──────────────────────────

    void OnEnable()
    {
        if (transform.childCount > 0)
            FindRotors();
        else
        {
            InitMaterials();
            Build();
        }
        baseLocalY = transform.localPosition.y;
    }

    /// <summary>Manual rebuild from editor (right-click → Rebuild Drone, or menu).</summary>
    [ContextMenu("Rebuild Drone")]
    public void Rebuild()
    {
        InitMaterials();
        Build();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (rotors == null) return;

        float dt = Time.deltaTime;
        float[] dirs = { 1, -1, 1, -1 };
        for (int i = 0; i < rotors.Length; i++)
        {
            if (rotors[i] != null)
                rotors[i].Rotate(Vector3.up, dirs[i] * rotorSpeed * dt, Space.Self);
        }

        // gentle hover bob (relative to parent)
        float bob = Mathf.Sin(Time.time * 2.5f) * 0.03f;
        transform.localPosition = new Vector3(
            transform.localPosition.x,
            baseLocalY + bob,
            transform.localPosition.z);
    }

    // ── materials ──────────────────────────

    void InitMaterials()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        matHull = new Material(sh) { color = hullColor };
        matHull.SetFloat("_Smoothness", 0.3f);

        matArm = new Material(sh) { color = armColor };
        matArm.SetFloat("_Smoothness", 0.2f);

        matGlow = new Material(sh) { color = glowColor };
        matGlow.EnableKeyword("_EMISSION");
        matGlow.SetColor("_EmissionColor", glowColor * glowIntensity);
        matGlow.SetFloat("_Smoothness", 0.9f);
    }

    // ── build ──────────────────────────────

    void Build()
    {
        // destroy old children
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        float bR = bodyRadius;
        float bH = bR * 0.3f;           // body half-height
        float motorR = bR * 0.28f;
        float motorH = bR * 0.22f;
        float rotorR = bR * 0.6f;
        float rotorT = 0.008f;           // blade thickness

        // ── body: hex prism ──
        Spawn("Body", HexPrism(bR, bH, 6), matHull);

        // ── canopy: small pyramid on top ──
        Spawn("Canopy", Pyramid(bR * 0.45f, bH * 1.5f, bH), matHull);

        // ── glow ring around body ──
        Spawn("GlowRing", Ring(bR * 1.02f, bR * 0.88f, bH * 0.3f, bH * 0.5f, 6), matGlow);

        // ── glow eye (front) ──
        Spawn("Eye", Eye(bR, bH), matGlow);

        // ── underbelly glow dot ──
        Spawn("Belly", HexPrism(bR * 0.2f, 0.005f, 6, -bH), matGlow);

        // ── 4 arms + motors + rotors ──
        rotors = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            float angle = (i * 90f + 45f) * Mathf.Deg2Rad;
            float dx = Mathf.Cos(angle);
            float dz = Mathf.Sin(angle);

            Vector3 armEnd = new Vector3(dx * (bR + armLength), 0, dz * (bR + armLength));
            Vector3 armMid = new Vector3(dx * (bR + armLength * 0.5f), 0, dz * (bR + armLength * 0.5f));

            // arm strut
            Spawn($"Arm{i}", ArmBar(armMid, armLength, bR * 0.12f, bR * 0.1f, angle), matArm);

            // motor housing
            Spawn($"Motor{i}", HexPrism(motorR, motorH, 6, 0, armEnd), matArm);

            // motor glow ring
            Spawn($"MotorGlow{i}", Ring(motorR * 1.15f, motorR * 0.85f, motorH * 0.3f, motorH * 0.5f, 6, armEnd), matGlow);

            // rotor (2-blade)
            var rotorGO = Spawn($"Rotor{i}", TwoBlade(rotorR, rotorT, bR * 0.06f), matArm);
            rotorGO.transform.localPosition = armEnd + Vector3.up * (motorH + 0.005f);
            rotors[i] = rotorGO.transform;
        }

        // ── skid legs (2 short bars underneath) ──
        for (int i = 0; i < 2; i++)
        {
            float zOff = (i == 0 ? 1f : -1f) * bR * 0.55f;
            Vector3 c = new Vector3(0, -bH - bR * 0.08f, zOff);
            Spawn($"Skid{i}", Box(c, bR * 0.7f, 0.006f, 0.008f), matArm);
        }
    }

    // ── mesh primitives ────────────────────

    // Hex (or n-gon) prism centered at origin, optional Y offset and XZ offset
    Mesh HexPrism(float r, float halfH, int sides, float yOff = 0, Vector3 off = default)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        int n = sides;
        // top & bottom ring
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            float x = Mathf.Cos(a) * r + off.x;
            float z = Mathf.Sin(a) * r + off.z;
            verts.Add(new Vector3(x, halfH + yOff + off.y, z));   // top ring [0..n-1]
            verts.Add(new Vector3(x, -halfH + yOff + off.y, z));  // bot ring [n..2n-1]
        }
        // top center, bottom center
        verts.Add(new Vector3(off.x, halfH + yOff + off.y, off.z));   // 2n
        verts.Add(new Vector3(off.x, -halfH + yOff + off.y, off.z));  // 2n+1

        int tc = 2 * n;
        int bc = 2 * n + 1;

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            // top face
            tris.Add(tc); tris.Add(i * 2); tris.Add(next * 2);
            // bottom face
            tris.Add(bc); tris.Add(next * 2 + 1); tris.Add(i * 2 + 1);
            // side quad
            tris.Add(i * 2); tris.Add(i * 2 + 1); tris.Add(next * 2 + 1);
            tris.Add(i * 2); tris.Add(next * 2 + 1); tris.Add(next * 2);
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    // Pyramid (n-gon base + apex)
    Mesh Pyramid(float r, float h, float baseY)
    {
        int n = 6;
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            verts.Add(new Vector3(Mathf.Cos(a) * r, baseY, Mathf.Sin(a) * r));
        }
        verts.Add(new Vector3(0, baseY + h, 0)); // apex
        verts.Add(new Vector3(0, baseY, 0));      // base center

        int apex = n;
        int bc = n + 1;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            // side
            tris.Add(i); tris.Add(apex); tris.Add(next);
            // base
            tris.Add(next); tris.Add(bc); tris.Add(i);
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    // Ring (outer hex minus inner hex at a Y band)
    Mesh Ring(float outerR, float innerR, float halfH, float yOff, int sides, Vector3 off = default)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        int n = sides;

        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            float co = Mathf.Cos(a), si = Mathf.Sin(a);

            verts.Add(new Vector3(co * outerR + off.x, halfH + yOff + off.y, si * outerR + off.z));   // outer top
            verts.Add(new Vector3(co * outerR + off.x, -halfH + yOff + off.y, si * outerR + off.z));  // outer bot
            verts.Add(new Vector3(co * innerR + off.x, halfH + yOff + off.y, si * innerR + off.z));   // inner top
            verts.Add(new Vector3(co * innerR + off.x, -halfH + yOff + off.y, si * innerR + off.z));  // inner bot
        }

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int ot = i * 4, ob = i * 4 + 1, it = i * 4 + 2, ib = i * 4 + 3;
            int not_ = next * 4, nob = next * 4 + 1, nit = next * 4 + 2, nib = next * 4 + 3;

            // outer face
            tris.Add(ot); tris.Add(ob); tris.Add(nob);
            tris.Add(ot); tris.Add(nob); tris.Add(not_);
            // inner face
            tris.Add(nit); tris.Add(nib); tris.Add(ib);
            tris.Add(nit); tris.Add(ib); tris.Add(it);
            // top face
            tris.Add(ot); tris.Add(not_); tris.Add(nit);
            tris.Add(ot); tris.Add(nit); tris.Add(it);
            // bottom face
            tris.Add(nob); tris.Add(ob); tris.Add(ib);
            tris.Add(nob); tris.Add(ib); tris.Add(nib);
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    // Front-facing glow eye (small diamond quad)
    Mesh Eye(float bR, float bH)
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
        var m = new Mesh { vertices = verts, triangles = new[] { 0,1,2, 0,2,3 } };
        m.RecalculateNormals();
        return m;
    }

    // Arm bar — oriented along the angle
    Mesh ArmBar(Vector3 center, float len, float w, float h, float angle)
    {
        // build an axis-aligned box then rotate
        var verts = new Vector3[8];
        float hl = len * 0.5f, hw = w * 0.5f, hh = h * 0.5f;
        verts[0] = new Vector3(-hl, -hh, -hw);
        verts[1] = new Vector3( hl, -hh, -hw);
        verts[2] = new Vector3( hl,  hh, -hw);
        verts[3] = new Vector3(-hl,  hh, -hw);
        verts[4] = new Vector3(-hl, -hh,  hw);
        verts[5] = new Vector3( hl, -hh,  hw);
        verts[6] = new Vector3( hl,  hh,  hw);
        verts[7] = new Vector3(-hl,  hh,  hw);

        // rotate about Y
        Quaternion rot = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
        for (int i = 0; i < 8; i++)
            verts[i] = rot * verts[i] + center;

        var tris = new[]
        {
            0,2,1, 0,3,2,  // front
            4,5,6, 4,6,7,  // back
            0,1,5, 0,5,4,  // bottom
            2,3,7, 2,7,6,  // top
            0,4,7, 0,7,3,  // left
            1,2,6, 1,6,5,  // right
        };

        var m = new Mesh { vertices = verts, triangles = tris };
        m.RecalculateNormals();
        return m;
    }

    // Two-blade rotor (2 thin rectangles crossing)
    Mesh TwoBlade(float r, float thickness, float bladeW)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        float ht = thickness * 0.5f;
        float hw = bladeW * 0.5f;

        // blade 1 along X
        AddBlade(verts, tris, new Vector3(-r, 0, 0), new Vector3(r, 0, 0), hw, ht);
        // blade 2 along Z
        AddBlade(verts, tris, new Vector3(0, 0, -r), new Vector3(0, 0, r), hw, ht);

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    void AddBlade(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, float hw, float ht)
    {
        // direction perpendicular to blade in XZ plane
        Vector3 dir = (b - a).normalized;
        Vector3 perp = new Vector3(-dir.z, 0, dir.x);

        int v = verts.Count;

        // 8 verts — 4 top, 4 bottom
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
            0,1,2, 0,2,3,  // top
            4,6,5, 4,7,6,  // bottom
            0,4,5, 0,5,1,  // side a
            2,6,7, 2,7,3,  // side b
            0,3,7, 0,7,4,  // side c
            1,5,6, 1,6,2,  // side d
        };

        for (int i = 0; i < idx.Length; i++)
            tris.Add(idx[i] + v);
    }

    // Simple box centered at c
    Mesh Box(Vector3 c, float sx, float sy, float sz)
    {
        float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;
        var verts = new Vector3[]
        {
            c + new Vector3(-hx, -hy, -hz), c + new Vector3( hx, -hy, -hz),
            c + new Vector3( hx,  hy, -hz), c + new Vector3(-hx,  hy, -hz),
            c + new Vector3(-hx, -hy,  hz), c + new Vector3( hx, -hy,  hz),
            c + new Vector3( hx,  hy,  hz), c + new Vector3(-hx,  hy,  hz),
        };
        var tris = new[]
        {
            0,2,1, 0,3,2, 4,5,6, 4,6,7,
            0,1,5, 0,5,4, 2,3,7, 2,7,6,
            0,4,7, 0,7,3, 1,2,6, 1,6,5,
        };
        var m = new Mesh { vertices = verts, triangles = tris };
        m.RecalculateNormals();
        return m;
    }

    // ── helpers ─────────────────────────────

    GameObject Spawn(string name, Mesh mesh, Material mat)
    {
        if (mesh.vertexCount == 0) return new GameObject(name);

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = mat;
        return go;
    }

    void FindRotors()
    {
        rotors = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            var t = transform.Find($"Rotor{i}");
            if (t != null) rotors[i] = t;
        }
    }
}
