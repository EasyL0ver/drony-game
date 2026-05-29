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

    // ── energy beam ──────────────────────────────────────────────

    ParticleSystem beamParticles;
    Transform beamTarget;
    GameObject beamGO;

    /// <summary>
    /// Show an energy vacuum effect from drone to station.
    /// </summary>
    public void ShowBeam(Transform target)
    {
        beamTarget = target;
        if (beamGO == null)
            CreateBeamEffect();
        beamGO.SetActive(true);
        beamParticles.Play();
    }

    /// <summary>Hide the energy beam.</summary>
    public void HideBeam()
    {
        beamTarget = null;
        if (beamGO != null)
        {
            beamParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            beamGO.SetActive(false);
        }
    }

    void CreateBeamEffect()
    {
        beamGO = new GameObject("EnergyVacuum");
        beamGO.transform.SetParent(transform, false);
        beamGO.transform.localPosition = new Vector3(0f, 0.6f, 0.5f);

        beamParticles = beamGO.AddComponent<ParticleSystem>();
        var main = beamParticles.main;
        main.loop = true;
        main.startLifetime = 0.6f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = baseGlowCol;
        main.gravityModifier = 0f;

        var emission = beamParticles.emission;
        emission.rateOverTime = 40f;

        // Particles spawn in a small sphere around the drone (set in Update)
        var shape = beamParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        // Size shrinks as particles reach the station (vacuum funnel)
        var sizeOverLife = beamParticles.sizeOverLifetime;
        sizeOverLife.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1.0f);
        sizeCurve.AddKey(0.3f, 0.7f);
        sizeCurve.AddKey(1f, 0.1f);
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Color fades in then brightens at end
        var colorOverLife = beamParticles.colorOverLifetime;
        colorOverLife.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(baseGlowCol, 0f),
                new GradientColorKey(Color.white, 0.8f),
                new GradientColorKey(baseGlowCol, 1f)
            },
            new[] {
                new GradientAlphaKey(0.3f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLife.color = gradient;

        // Emissive particle material
        var renderer = beamGO.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Particles/Standard Unlit");
        var mat = new Material(sh);
        mat.color = baseGlowCol;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", baseGlowCol * 4f);
        // Make additive
        mat.SetFloat("_Surface", 1f); // transparent
        mat.SetFloat("_Blend", 1f);   // additive
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        renderer.material = mat;

        beamGO.SetActive(false);
    }

    protected virtual void Update()
    {
        if (beamParticles == null || beamTarget == null || !beamGO.activeSelf) return;

        Vector3 stationPoint = transform.position + transform.forward * 0.5f + Vector3.up * 0.6f;
        Vector3 dronePoint = beamTarget.position;

        // Position emitter at drone
        var shape = beamParticles.shape;
        shape.position = beamParticles.transform.InverseTransformPoint(dronePoint);

        // Move particles toward station using velocity over lifetime
        // We manually update particle positions for the vacuum suction effect
        var particles = new ParticleSystem.Particle[beamParticles.particleCount];
        int count = beamParticles.GetParticles(particles);

        for (int i = 0; i < count; i++)
        {
            float t = 1f - (particles[i].remainingLifetime / particles[i].startLifetime);
            // Accelerating pull toward station
            float pull = t * t * 18f;
            Vector3 dir = (stationPoint - particles[i].position).normalized;
            particles[i].velocity = dir * pull;

            // Add spiral
            float angle = t * 12f + i * 0.5f;
            Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;
            Vector3 spiral = (perp * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * (1f - t) * 0.4f;
            particles[i].velocity += spiral;
        }

        beamParticles.SetParticles(particles, count);
    }
}
