using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Procedural refitting station building. Crude sci-fi look: hex base platform,
/// central tower with antenna, glowing accents. Sits on the floor of a room.
/// </summary>
public class RefittingStation : MonoBehaviour
{
    Material matBase, matTower, matGlow, matAccent;

    void OnEnable()
    {
        if (transform.childCount == 0)
            Build();
    }

    [ContextMenu("Rebuild Station")]
    public void Build()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        InitMaterials();

        // ── hex platform (base) ──
        float platR = 1.2f, platH = 0.08f;
        Spawn("Platform", HexPrism(platR, platH, 6, 0f), matBase);

        // ── glow ring around platform edge ──
        Spawn("PlatGlow", Ring(platR * 1.02f, platR * 0.92f, 0.02f, platH, 6), matGlow);

        // ── main tower: octagonal prism ──
        float towerR = 0.5f, towerH = 0.7f;
        Spawn("Tower", HexPrism(towerR, towerH, 8, platH + towerH), matTower);

        // ── tower glow band (mid-height) ──
        Spawn("TowerBand", Ring(towerR * 1.04f, towerR * 0.90f, 0.04f,
                                platH + towerH, 8), matGlow);

        // ── upper section: smaller octagon ──
        float upperR = 0.35f, upperH = 0.4f;
        float upperY = platH + towerH * 2 + upperH;
        Spawn("Upper", HexPrism(upperR, upperH, 8, upperY), matTower);

        // ── roof: flat hex cap ──
        float roofY = upperY + upperH;
        Spawn("Roof", HexPrism(upperR * 1.3f, 0.04f, 6, roofY), matAccent);

        // ── antenna mast ──
        float antennaH = 0.5f;
        float antennaY = roofY + antennaH + 0.04f;
        Spawn("Antenna", HexPrism(0.03f, antennaH, 4, antennaY), matAccent);

        // ── antenna tip glow ──
        float tipY = antennaY + antennaH + 0.04f;
        Spawn("AntennaTip", HexPrism(0.06f, 0.04f, 6, tipY), matGlow);

        // ── 4 small support pillars at platform corners ──
        for (int i = 0; i < 4; i++)
        {
            float angle = (i * 90f + 45f) * Mathf.Deg2Rad;
            float px = Mathf.Cos(angle) * platR * 0.7f;
            float pz = Mathf.Sin(angle) * platR * 0.7f;
            float pillarH = 0.25f;
            Spawn($"Pillar{i}", HexPrism(0.06f, pillarH, 6, platH + pillarH,
                                          new Vector3(px, 0, pz)), matAccent);
            // pillar top glow
            Spawn($"PillarGlow{i}", HexPrism(0.09f, 0.02f, 6, platH + pillarH * 2 + 0.02f,
                                              new Vector3(px, 0, pz)), matGlow);
        }

        // ── wrench/gear icon: 2 crossed bars on the front face ──
        float iconY = platH + towerH;
        float iconFwd = towerR * 1.05f;
        float barLen = 0.3f, barW = 0.04f;
        Spawn("WrenchBar1", RotatedBox(new Vector3(0, iconY, iconFwd),
                                        barLen, barW, 0.01f, 45f), matGlow);
        Spawn("WrenchBar2", RotatedBox(new Vector3(0, iconY, iconFwd),
                                        barLen, barW, 0.01f, -45f), matGlow);
    }

    void InitMaterials()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        matBase = new Material(sh) { color = new Color(0.08f, 0.08f, 0.10f) };
        matBase.SetFloat("_Smoothness", 0.3f);

        matTower = new Material(sh) { color = new Color(0.10f, 0.12f, 0.14f) };
        matTower.SetFloat("_Smoothness", 0.35f);

        matAccent = new Material(sh) { color = new Color(0.15f, 0.18f, 0.20f) };
        matAccent.SetFloat("_Smoothness", 0.2f);

        Color glowCol = new Color(0.2f, 1f, 0.8f);
        matGlow = new Material(sh) { color = glowCol };
        matGlow.EnableKeyword("_EMISSION");
        matGlow.SetColor("_EmissionColor", glowCol * 3f);
        matGlow.SetFloat("_Smoothness", 0.9f);
    }

    // ── mesh primitives (same style as LowPolyDrone) ──

    Mesh HexPrism(float r, float halfH, int sides, float yOff, Vector3 off = default)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
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

        int tc = 2 * n, bc = 2 * n + 1;
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

    Mesh Ring(float outerR, float innerR, float halfH, float yOff, int sides)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        int n = sides;

        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            float co = Mathf.Cos(a), si = Mathf.Sin(a);
            verts.Add(new Vector3(co * outerR, halfH + yOff, si * outerR));
            verts.Add(new Vector3(co * outerR, -halfH + yOff, si * outerR));
            verts.Add(new Vector3(co * innerR, halfH + yOff, si * innerR));
            verts.Add(new Vector3(co * innerR, -halfH + yOff, si * innerR));
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

    Mesh RotatedBox(Vector3 center, float len, float width, float depth, float angleDeg)
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

    GameObject Spawn(string name, Mesh mesh, Material mat)
    {
        if (mesh.vertexCount == 0) return new GameObject(name);
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }
}
