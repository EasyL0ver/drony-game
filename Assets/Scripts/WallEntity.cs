using UnityEngine;

/// <summary>
/// Base class for anything mounted on a hex room wall: stations, corridors, etc.
/// Placed at a wall midpoint with local +Z facing into the room.
/// </summary>
public abstract class WallEntity : MonoBehaviour
{
    /// <summary>How far in front of the wall the drone parks (along local +Z).</summary>
    public abstract float ParkOffset { get; }

    /// <summary>World-space point where a visiting drone should sit.</summary>
    public Vector3 DroneParkPoint => transform.position + transform.forward * ParkOffset;

    /// <summary>Which station type this entity represents (None for passages).</summary>
    public virtual StationType StationType => StationType.None;

    // ── material system (used by visual wall entities) ───────────

    protected Material matBase, matBody, matGlow, matAccent;
    protected Color baseGlowEmission;
    protected Color baseBaseCol, baseBodyCol, baseAccentCol, baseGlowCol;

    protected void InitMaterials(Color baseCol, Color bodyCol, Color accentCol, Color glowCol)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        baseBaseCol = baseCol;
        matBase = new Material(sh) { color = baseCol };
        matBase.SetFloat("_Smoothness", 0.3f);

        baseBodyCol = bodyCol;
        matBody = new Material(sh) { color = bodyCol };
        matBody.SetFloat("_Smoothness", 0.35f);

        baseAccentCol = accentCol;
        matAccent = new Material(sh) { color = accentCol };
        matAccent.SetFloat("_Smoothness", 0.2f);

        baseGlowCol = glowCol;
        matGlow = new Material(sh) { color = glowCol };
        matGlow.EnableKeyword("_EMISSION");
        baseGlowEmission = glowCol * 3f;
        matGlow.SetColor("_EmissionColor", baseGlowEmission);
        matGlow.SetFloat("_Smoothness", 0.9f);
    }

    public virtual void SetHoverGlow(bool hovered)
    {
        if (matGlow == null) return;
        float t = hovered ? 0.25f : 0f;
        matBase.color  = Color.Lerp(baseBaseCol, baseGlowCol, t);
        matBody.color  = Color.Lerp(baseBodyCol, baseGlowCol, t);
        matAccent.color = Color.Lerp(baseAccentCol, baseGlowCol, t);
        matGlow.color  = Color.Lerp(baseGlowCol, Color.white, t);
        matGlow.SetColor("_EmissionColor", hovered ? baseGlowEmission * 3f : baseGlowEmission);
    }

    // ── mesh primitives (shared by all visual wall entities) ─────

    protected Mesh Box(Vector3 center, float sizeX, float sizeY, float sizeZ)
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

    protected Mesh RotatedBox(Vector3 center, float len, float width, float depth, float angleDeg)
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

    protected GameObject Spawn(string name, Mesh mesh, Material mat)
    {
        if (mesh.vertexCount == 0) return new GameObject(name);
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }
}
