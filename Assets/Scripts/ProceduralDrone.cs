using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Procedurally generates a sci-fi drone entirely from code-built meshes.
/// Attach to an empty GameObject — the drone builds immediately in the editor
/// and animates (rotor spin + hover) at runtime.
/// </summary>
[ExecuteAlways]
public class ProceduralDrone : MonoBehaviour
{
    [Header("Design")]
    [SerializeField] float scale = 1f;
    [SerializeField] float rotorSpeed = 2200f;

    [Header("Colors")]
    [SerializeField] Color primaryColor   = new Color(0.10f, 0.10f, 0.13f);
    [SerializeField] Color secondaryColor = new Color(0.18f, 0.18f, 0.22f);
    [SerializeField] Color accentGlow     = new Color(0f, 0.85f, 1f);
    [SerializeField] float glowIntensity  = 5f;

    Transform[] rotors;
    readonly float[] rotorDir = { 1, -1, 1, -1 };
    Vector3 origin;
    bool originCaptured;
    Material matPri, matSec, matGlow, matBlade, matDark;

    // ───────────────────────────────────────
    //  LIFECYCLE
    // ───────────────────────────────────────

    void OnEnable()
    {
        if (transform.childCount > 0)
            FindRotors();
        else if (Application.isPlaying)
        {
            InitMaterials();
            Build();
        }
    }

    /// <summary>Manual rebuild from editor (right-click → Rebuild Drone, or menu).</summary>
    [ContextMenu("Rebuild Drone")]
    public void Rebuild()
    {
        InitMaterials();
        Build();
    }

    void Start()
    {
        origin = transform.position;
        originCaptured = true;
    }

    void Update()
    {
        // Only animate during play mode
        if (!Application.isPlaying) return;
        if (rotors == null) return;

        if (!originCaptured)
        {
            origin = transform.position;
            originCaptured = true;
        }

        float dt = Time.deltaTime;
        for (int i = 0; i < 4; i++)
            if (rotors[i]) rotors[i].Rotate(0, rotorSpeed * rotorDir[i] * dt, 0, Space.Self);

        // subtle hover bob + tilt
        float t = Time.time;
        transform.position = origin + Vector3.up * Mathf.Sin(t * 1.5f) * 0.025f * scale;
        transform.localRotation = Quaternion.Euler(
            Mathf.Sin(t * 0.7f) * 0.6f,
            transform.localRotation.eulerAngles.y,
            Mathf.Cos(t * 0.9f) * 0.4f);
    }

    void FindRotors()
    {
        rotors = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            Transform r = transform.Find("Rotor" + i);
            if (r != null) rotors[i] = r;
        }
    }

    [ContextMenu("Rebuild Drone")]
    void Rebuild()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);
        InitMaterials();
        Build();
    }

    // ───────────────────────────────────────
    //  MATERIALS
    // ───────────────────────────────────────

    void InitMaterials()
    {
        matPri   = MakeMat(primaryColor,   0.75f, 0.60f);
        matSec   = MakeMat(secondaryColor, 0.55f, 0.50f);
        matGlow  = MakeGlowMat(accentGlow, glowIntensity);
        matBlade = MakeMat(new Color(0.04f, 0.04f, 0.06f), 0.80f, 0.70f);
        matDark  = MakeMat(new Color(0.03f, 0.03f, 0.04f), 0.30f, 0.20f);
    }

    Material MakeMat(Color c, float metal, float smooth)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        Material m = new Material(sh);
        m.color = c;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    Material MakeGlowMat(Color c, float intensity)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        Material m = new Material(sh);
        Color surface = c * 0.35f;
        m.color = surface;
        m.SetColor("_BaseColor", surface);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * intensity);
        m.SetFloat("_Metallic", 0.3f);
        m.SetFloat("_Smoothness", 0.85f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return m;
    }

    // ───────────────────────────────────────
    //  DRONE ASSEMBLY
    // ───────────────────────────────────────

    void Build()
    {
        float s = scale;
        rotors = new Transform[4];

        // ── Central Hull ──
        AddPart("Hull_Lower", OctDisc(0.50f*s, 0.065f*s, 0.020f*s), matPri, V3(0, 0, 0));
        AddPart("Hull_Upper", OctDisc(0.32f*s, 0.045f*s, 0.015f*s), matSec, V3(0, 0.040f*s, 0));
        AddPart("Hull_Ring",  MakeRing(0.31f*s, 0.33f*s, 0.012f*s, 16), matGlow, V3(0, 0.040f*s, 0));

        // ── Sensor dome + antenna ──
        AddPart("Dome",    MakeHemisphere(0.09f*s, 12, 6), matGlow, V3(0, 0.063f*s, 0));
        AddPart("Antenna", MakeBox(0.007f*s, 0.11f*s, 0.007f*s), matSec, V3(0, 0.130f*s, 0));
        AddPart("AntTip",  MakeHemisphere(0.013f*s, 8, 4),  matGlow, V3(0, 0.190f*s, 0));

        // ── Bottom sensor eye ──
        AddPart("BotEye", MakeDisc(0.10f*s, 12, 0.004f*s), matGlow, V3(0, -0.035f*s, 0));

        // ── Four arms, motors, rotors, landing gear ──
        float[] angles = { 45f, 135f, 225f, 315f };
        for (int i = 0; i < 4; i++)
        {
            float a   = angles[i] * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            float mDist  = 0.95f * s;
            float armLen = 0.65f * s;
            Vector3 mPos = dir * mDist;
            Vector3 armC = dir * (mDist * 0.5f + 0.15f * s);

            // arm beam (tapered)
            GameObject arm = AddPart("Arm" + i,
                MakeTaperedBox(armLen, 0.04f*s, 0.065f*s, 0.040f*s), matPri, armC);
            arm.transform.localRotation = Quaternion.Euler(0, -angles[i], 0);

            // arm glow strip
            GameObject ag = AddPart("ArmG" + i,
                MakeBox(armLen * 0.75f, 0.004f*s, 0.012f*s), matGlow,
                armC + V3(0, 0.022f*s, 0));
            ag.transform.localRotation = Quaternion.Euler(0, -angles[i], 0);

            // motor pod
            AddPart("Motor" + i, MakeCylinder(0.11f*s, 0.07f*s, 8), matDark, mPos);

            // motor shroud
            AddPart("Shroud" + i, MakeRing(0.12f*s, 0.155f*s, 0.022f*s, 16), matSec,
                mPos + V3(0, 0.035f*s, 0));

            // motor glow ring
            AddPart("MGlow" + i, MakeRing(0.115f*s, 0.125f*s, 0.007f*s, 16), matGlow,
                mPos + V3(0, 0.050f*s, 0));

            // exhaust glow (under motor)
            AddPart("Exhaust" + i, MakeDisc(0.07f*s, 10, 0.003f*s), matGlow,
                mPos - V3(0, 0.040f*s, 0));

            // ── Rotor assembly (3 blades + hub) ──
            GameObject rotorGO = new GameObject("Rotor" + i);
            rotorGO.transform.SetParent(transform, false);
            rotorGO.transform.localPosition = mPos + V3(0, 0.055f*s, 0);
            rotors[i] = rotorGO.transform;

            for (int b = 0; b < 3; b++)
            {
                GameObject blade = AddPart("Bl" + i + "_" + b,
                    MakeBlade(0.32f*s, 0.045f*s, 0.005f*s), matBlade, Vector3.zero);
                blade.transform.SetParent(rotorGO.transform, false);
                blade.transform.localRotation = Quaternion.Euler(6f, b * 120f, 0);
            }
            GameObject hub = AddPart("Hub" + i,
                MakeCylinder(0.018f*s, 0.012f*s, 8), matSec, Vector3.zero);
            hub.transform.SetParent(rotorGO.transform, false);

            // ── Landing skid ──
            Vector3 skid = mPos - V3(0, 0.055f*s, 0);
            AddPart("Skid" + i, MakeBox(0.012f*s, 0.09f*s, 0.012f*s), matPri, skid);
            AddPart("Foot" + i, MakeBox(0.055f*s, 0.007f*s, 0.055f*s), matDark,
                skid - V3(0, 0.045f*s, 0));

            // ── Stabilizer fin ──
            GameObject fin = AddPart("Fin" + i,
                MakeBox(0.05f*s, 0.065f*s, 0.005f*s), matSec,
                mPos + V3(0, 0.020f*s, 0) - dir * 0.13f * s);
            fin.transform.localRotation = Quaternion.Euler(0, -angles[i], 0);
        }

        // ── Hull panel accents ──
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.40f * s;
            pos.y = -0.008f * s;
            GameObject p = AddPart("Panel" + i,
                MakeBox(0.07f*s, 0.012f*s, 0.035f*s), matSec, pos);
            p.transform.localRotation = Quaternion.Euler(0, -i * 45f, 0);
        }
    }

    // ═══════════════════════════════════════
    //  MESH GENERATORS
    // ═══════════════════════════════════════

    /// <summary>Beveled octagonal flying-saucer disc.</summary>
    Mesh OctDisc(float radius, float height, float bevel)
    {
        int N = 8;
        List<Vector3> V = new List<Vector3>();
        List<int> T = new List<int>();
        float hh = height * 0.5f;

        // -- top face --
        V.Add(new Vector3(0, hh, 0)); // index 0
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            V.Add(new Vector3(Mathf.Cos(a) * (radius - bevel), hh, Mathf.Sin(a) * (radius - bevel)));
        }
        for (int i = 0; i < N; i++)
        {
            T.Add(0); T.Add(1 + i); T.Add(1 + (i + 1) % N);
        }

        // -- bevel ring (widest point, mid-height) --
        int bvS = V.Count;
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            V.Add(new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
        }
        for (int i = 0; i < N; i++)
        {
            int t0 = 1 + i, t1 = 1 + (i + 1) % N;
            int b0 = bvS + i, b1 = bvS + (i + 1) % N;
            T.AddRange(new int[] { t0, b0, t1, t1, b0, b1 });
        }

        // -- bottom face --
        int bc = V.Count;
        V.Add(new Vector3(0, -hh, 0));
        int br = V.Count;
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            V.Add(new Vector3(Mathf.Cos(a) * (radius - bevel), -hh, Mathf.Sin(a) * (radius - bevel)));
        }
        for (int i = 0; i < N; i++)
        {
            T.Add(bc); T.Add(br + (i + 1) % N); T.Add(br + i);
        }

        // -- bevel-to-bottom sides --
        for (int i = 0; i < N; i++)
        {
            int b0 = bvS + i, b1 = bvS + (i + 1) % N;
            int bt0 = br + i, bt1 = br + (i + 1) % N;
            T.AddRange(new int[] { b0, bt0, b1, b1, bt0, bt1 });
        }

        return FinalizeMesh("OctDisc", V, T);
    }

    /// <summary>Flat filled disc with slight thickness.</summary>
    Mesh MakeDisc(float r, int seg, float thick)
    {
        List<Vector3> V = new List<Vector3>();
        List<int> T = new List<int>();
        float h = thick * 0.5f;

        // top
        V.Add(new Vector3(0, h, 0));
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            V.Add(new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r));
        }
        for (int i = 0; i < seg; i++)
        {
            T.Add(0); T.Add(1 + i); T.Add(1 + (i + 1) % seg);
        }

        // bottom
        int bc = V.Count;
        V.Add(new Vector3(0, -h, 0));
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            V.Add(new Vector3(Mathf.Cos(a) * r, -h, Mathf.Sin(a) * r));
        }
        for (int i = 0; i < seg; i++)
        {
            T.Add(bc); T.Add(bc + 1 + (i + 1) % seg); T.Add(bc + 1 + i);
        }

        return FinalizeMesh("Disc", V, T);
    }

    /// <summary>Hollow ring (torus cross-section is rectangular).</summary>
    Mesh MakeRing(float ri, float ro, float h, int seg)
    {
        List<Vector3> V = new List<Vector3>();
        List<int> T = new List<int>();
        float hh = h * 0.5f;

        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            float c = Mathf.Cos(a), sn = Mathf.Sin(a);
            V.Add(new Vector3(c * ro,  hh, sn * ro));  // 4i+0 top-outer
            V.Add(new Vector3(c * ri,  hh, sn * ri));  // 4i+1 top-inner
            V.Add(new Vector3(c * ro, -hh, sn * ro));  // 4i+2 bot-outer
            V.Add(new Vector3(c * ri, -hh, sn * ri));  // 4i+3 bot-inner
        }
        for (int i = 0; i < seg; i++)
        {
            int ci = i * 4, ni = (i + 1) * 4;
            // top face
            T.AddRange(new int[] { ci, ni, ci+1, ci+1, ni, ni+1 });
            // bottom face
            T.AddRange(new int[] { ci+3, ni+2, ci+2, ci+3, ni+3, ni+2 });
            // outer wall
            T.AddRange(new int[] { ci, ci+2, ni, ni, ci+2, ni+2 });
            // inner wall
            T.AddRange(new int[] { ci+1, ni+1, ci+3, ci+3, ni+1, ni+3 });
        }

        return FinalizeMesh("Ring", V, T);
    }

    /// <summary>Closed cylinder with caps.</summary>
    Mesh MakeCylinder(float r, float h, int seg)
    {
        List<Vector3> V = new List<Vector3>();
        List<int> T = new List<int>();
        float hh = h * 0.5f;

        // top cap
        V.Add(new Vector3(0, hh, 0));
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            V.Add(new Vector3(Mathf.Cos(a) * r, hh, Mathf.Sin(a) * r));
        }
        for (int i = 0; i < seg; i++)
        {
            T.Add(0); T.Add(1 + i); T.Add(1 + (i + 1) % seg);
        }

        // bottom cap
        int bc = V.Count;
        V.Add(new Vector3(0, -hh, 0));
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            V.Add(new Vector3(Mathf.Cos(a) * r, -hh, Mathf.Sin(a) * r));
        }
        for (int i = 0; i < seg; i++)
        {
            T.Add(bc); T.Add(bc + 1 + (i + 1) % seg); T.Add(bc + 1 + i);
        }

        // side wall
        int ss = V.Count;
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            float c = Mathf.Cos(a), sn = Mathf.Sin(a);
            V.Add(new Vector3(c * r,  hh, sn * r));
            V.Add(new Vector3(c * r, -hh, sn * r));
        }
        for (int i = 0; i < seg; i++)
        {
            int ci = ss + i * 2;
            T.AddRange(new int[] { ci, ci+2, ci+1, ci+1, ci+2, ci+3 });
        }

        return FinalizeMesh("Cyl", V, T);
    }

    /// <summary>Axis-aligned box with proper per-face normals.</summary>
    Mesh MakeBox(float w, float h, float d)
    {
        float hw = w * 0.5f, hh = h * 0.5f, hd = d * 0.5f;
        Vector3[] verts = new Vector3[]
        {
            // Front  (+Z)
            new Vector3(-hw, -hh,  hd), new Vector3( hw, -hh,  hd),
            new Vector3( hw,  hh,  hd), new Vector3(-hw,  hh,  hd),
            // Back   (-Z)
            new Vector3( hw, -hh, -hd), new Vector3(-hw, -hh, -hd),
            new Vector3(-hw,  hh, -hd), new Vector3( hw,  hh, -hd),
            // Top    (+Y)
            new Vector3(-hw,  hh,  hd), new Vector3( hw,  hh,  hd),
            new Vector3( hw,  hh, -hd), new Vector3(-hw,  hh, -hd),
            // Bottom (-Y)
            new Vector3(-hw, -hh, -hd), new Vector3( hw, -hh, -hd),
            new Vector3( hw, -hh,  hd), new Vector3(-hw, -hh,  hd),
            // Right  (+X)
            new Vector3( hw, -hh,  hd), new Vector3( hw, -hh, -hd),
            new Vector3( hw,  hh, -hd), new Vector3( hw,  hh,  hd),
            // Left   (-X)
            new Vector3(-hw, -hh, -hd), new Vector3(-hw, -hh,  hd),
            new Vector3(-hw,  hh,  hd), new Vector3(-hw,  hh, -hd),
        };
        int[] tris = new int[36];
        for (int f = 0; f < 6; f++)
        {
            int vi = f * 4, ti = f * 6;
            tris[ti]   = vi;   tris[ti+1] = vi+2; tris[ti+2] = vi+1;
            tris[ti+3] = vi;   tris[ti+4] = vi+3; tris[ti+5] = vi+2;
        }
        Mesh m = new Mesh { name = "Box", vertices = verts, triangles = tris };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Box that tapers from wBase (at -X) to wTip (at +X).</summary>
    Mesh MakeTaperedBox(float length, float h, float wBase, float wTip)
    {
        float hl = length * 0.5f, hh = h * 0.5f;
        float wb = wBase * 0.5f, wt = wTip * 0.5f;

        Vector3[] verts = new Vector3[]
        {
            // Front  (+Z, wider at base end)
            new Vector3(-hl, -hh,  wb), new Vector3( hl, -hh,  wt),
            new Vector3( hl,  hh,  wt), new Vector3(-hl,  hh,  wb),
            // Back   (-Z)
            new Vector3( hl, -hh, -wt), new Vector3(-hl, -hh, -wb),
            new Vector3(-hl,  hh, -wb), new Vector3( hl,  hh, -wt),
            // Top    (+Y)
            new Vector3(-hl,  hh,  wb), new Vector3( hl,  hh,  wt),
            new Vector3( hl,  hh, -wt), new Vector3(-hl,  hh, -wb),
            // Bottom (-Y)
            new Vector3(-hl, -hh, -wb), new Vector3( hl, -hh, -wt),
            new Vector3( hl, -hh,  wt), new Vector3(-hl, -hh,  wb),
            // Right  (+X, tip end)
            new Vector3( hl, -hh,  wt), new Vector3( hl, -hh, -wt),
            new Vector3( hl,  hh, -wt), new Vector3( hl,  hh,  wt),
            // Left   (-X, base end)
            new Vector3(-hl, -hh, -wb), new Vector3(-hl, -hh,  wb),
            new Vector3(-hl,  hh,  wb), new Vector3(-hl,  hh, -wb),
        };
        int[] tris = new int[36];
        for (int f = 0; f < 6; f++)
        {
            int vi = f * 4, ti = f * 6;
            tris[ti]   = vi;   tris[ti+1] = vi+2; tris[ti+2] = vi+1;
            tris[ti+3] = vi;   tris[ti+4] = vi+3; tris[ti+5] = vi+2;
        }
        Mesh m = new Mesh { name = "TaperedBox", vertices = verts, triangles = tris };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Upper hemisphere with flat base.</summary>
    Mesh MakeHemisphere(float r, int seg, int rings)
    {
        List<Vector3> V = new List<Vector3>();
        List<int> T = new List<int>();

        V.Add(new Vector3(0, r, 0)); // apex
        for (int ri = 1; ri <= rings; ri++)
        {
            float phi = Mathf.PI * 0.5f * ri / rings;
            float y  = Mathf.Cos(phi) * r;
            float rr = Mathf.Sin(phi) * r;
            for (int s = 0; s < seg; s++)
            {
                float th = s * Mathf.PI * 2f / seg;
                V.Add(new Vector3(Mathf.Cos(th) * rr, y, Mathf.Sin(th) * rr));
            }
        }

        // apex triangles
        for (int s = 0; s < seg; s++)
        {
            T.Add(0); T.Add(1 + s); T.Add(1 + (s + 1) % seg);
        }

        // ring quads
        for (int ri = 0; ri < rings - 1; ri++)
        {
            for (int s = 0; s < seg; s++)
            {
                int c  = 1 + ri * seg + s;
                int n  = 1 + ri * seg + (s + 1) % seg;
                int cd = c + seg;
                int nd = n + seg;
                T.AddRange(new int[] { c, cd, n, n, cd, nd });
            }
        }

        // flat base cap
        int bci = V.Count;
        V.Add(Vector3.zero);
        int lr = 1 + (rings - 1) * seg;
        for (int s = 0; s < seg; s++)
        {
            T.Add(bci); T.Add(lr + (s + 1) % seg); T.Add(lr + s);
        }

        return FinalizeMesh("Hemi", V, T);
    }

    /// <summary>Tapered propeller blade extending along +X.</summary>
    Mesh MakeBlade(float len, float w, float thick)
    {
        float hw = w * 0.5f, ht = thick * 0.5f;
        float tw = w * 0.15f;

        Vector3[] verts = new Vector3[]
        {
            // base end (near hub)
            new Vector3(0.015f,  ht,       -hw),  // 0
            new Vector3(0.015f,  ht,        hw),  // 1
            new Vector3(0.015f, -ht,        hw),  // 2
            new Vector3(0.015f, -ht,       -hw),  // 3
            // tip end
            new Vector3(len,     ht * 0.3f, -tw), // 4
            new Vector3(len,     ht * 0.3f,  tw), // 5
            new Vector3(len,    -ht * 0.3f,  tw), // 6
            new Vector3(len,    -ht * 0.3f, -tw), // 7
        };

        int[] tris = new int[]
        {
            0,1,5, 0,5,4,   // top
            2,3,7, 2,7,6,   // bottom
            1,2,6, 1,6,5,   // front edge
            3,0,4, 3,4,7,   // back edge
            4,5,6, 4,6,7,   // tip cap
            1,0,3, 1,3,2,   // base cap
        };

        Mesh m = new Mesh { name = "Blade", vertices = verts, triangles = tris };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ═══════════════════════════════════════
    //  UTILITY
    // ═══════════════════════════════════════

    Mesh FinalizeMesh(string name, List<Vector3> v, List<int> t)
    {
        Mesh m = new Mesh { name = name };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    GameObject AddPart(string name, Mesh mesh, Material mat, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().mesh = mesh;
        go.AddComponent<MeshRenderer>().material = mat;
        return go;
    }

    static Vector3 V3(float x, float y, float z)
    {
        return new Vector3(x, y, z);
    }
}
