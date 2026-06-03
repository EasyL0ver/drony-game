using UnityEngine;
using System.Collections;

/// <summary>
/// Abstract base for wall presentation. Each instance maps to a WallModel
/// and handles visuals: meshes, materials, beam effects, hover glow.
/// Placed at a wall midpoint with local +Z facing into the room.
/// </summary>
public abstract class WallView : MonoBehaviour
{
    /// <summary>The underlying game-logic model for this wall.</summary>
    public WallModel Model { get; private set; }

    /// <summary>Assign the model this view represents.</summary>
    public void SetModel(WallModel model) { Model = model; }

    /// <summary>How far in front of the wall the drone parks (along local +Z).</summary>
    public abstract float ParkOffset { get; }

    /// <summary>Short description shown in the hover info panel.</summary>
    public virtual string HoverDescription => "";

    /// <summary>World-space point where a visiting drone should sit.</summary>
    public Vector3 DroneParkPoint => transform.position + transform.forward * ParkOffset;

    // ── animation API ────────────────────────────────────────────

    protected Coroutine activeAnimation;
    protected int animationToken;
    protected bool isReversing;

    /// <summary>
    /// Play a traversal animation: drone passes through this wall over the given duration.
    /// departing=true: drone moves from park point into the wall (leaving room).
    /// departing=false: drone moves from wall out to park point (entering room).
    /// Returns the effective traversal distance from start to wall midpoint.
    /// Override for non-straight paths (e.g. crooked vents).
    /// </summary>
    public virtual float GetTraversalDistance(Vector3 from)
    {
        Vector3 mid = transform.position;
        mid.y = from.y;
        return Vector3.Distance(from, mid);
    }

    /// <summary>
    /// Traversals cannot be cancelled — only reversed via ReverseTraversal().
    /// Override in subclasses for custom traversal visuals.
    /// </summary>
    public virtual void PlayTraversal(Transform drone, float duration, bool departing, System.Action onComplete)
    {
        isReversing = false;
        int token = ++animationToken;
        activeAnimation = StartCoroutine(RunTraversal(drone, duration, departing, token, onComplete));
    }

    /// <summary>
    /// Reverse an in-progress traversal. Drone goes back the way it came.
    /// Takes the same amount of elapsed time to return.
    /// Calls onReversed when the drone is back at the start.
    /// </summary>
    public void ReverseTraversal(System.Action onReversed)
    {
        isReversing = true;
        // The running coroutine detects isReversing and handles the reverse path.
        // onReversed is stashed for the coroutine to call.
        reverseCallback = onReversed;
    }

    protected System.Action reverseCallback;

    /// <summary>
    /// Play an interaction animation (charge, refit, clear rubble) over the given duration.
    /// Interactions can be cancelled via CancelInteraction().
    /// Override in subclasses for custom interaction visuals.
    /// </summary>
    public virtual void PlayInteraction(Transform drone, float duration, WallInteractionConfig config, System.Action onComplete)
    {
        CancelInteraction();
        int token = ++animationToken;
        activeAnimation = StartCoroutine(RunInteraction(drone, duration, config, token, onComplete));
    }

    /// <summary>Cancel a running interaction animation immediately.</summary>
    public void CancelInteraction()
    {
        animationToken++;
        if (activeAnimation != null)
        {
            StopCoroutine(activeAnimation);
            activeAnimation = null;
        }
        HideBeam();
    }

    protected virtual IEnumerator RunTraversal(Transform drone, float duration, bool departing, int token, System.Action onComplete)
    {
        Vector3 parkPoint = DroneParkPoint;
        Vector3 wallMid = transform.position;
        parkPoint.y = drone.position.y;
        wallMid.y = drone.position.y;

        // When arriving, start from drone's actual position to avoid any snap
        Vector3 start = departing ? parkPoint : drone.position;
        Vector3 end = departing ? wallMid : parkPoint;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (isReversing) break;
            float t = elapsed / duration;
            drone.position = Vector3.Lerp(start, end, t);
            UpdateLineConsumed(t);
            // Face direction of travel
            Vector3 dir = (end - start).normalized;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (isReversing)
        {
            // Reverse: go back from current position to start
            Vector3 reverseFrom = drone.position;
            float reverseTime = elapsed;
            float reverseElapsed = 0f;
            while (reverseElapsed < reverseTime)
            {
                float t = reverseElapsed / reverseTime;
                drone.position = Vector3.Lerp(reverseFrom, start, t);
                Vector3 dir = (start - reverseFrom).normalized;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
                reverseElapsed += Time.deltaTime;
                yield return null;
            }
            drone.position = start;
            activeAnimation = null;
            isReversing = false;
            reverseCallback?.Invoke();
            reverseCallback = null;
            yield break;
        }

        drone.position = end;
        HideLine();
        activeAnimation = null;
        if (token == animationToken) onComplete?.Invoke();
    }

    protected virtual IEnumerator RunInteraction(Transform drone, float duration, WallInteractionConfig config, int token, System.Action onComplete)
    {
        ShowBeam(drone);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (token != animationToken) yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }
        HideBeam();
        activeAnimation = null;
        if (token == animationToken) onComplete?.Invoke();
    }

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

    bool isPowered = true;

    /// <summary>
    /// Toggle powered state. When unpowered, glow emission is removed but color stays.
    /// </summary>
    public virtual void SetPowered(bool powered)
    {
        isPowered = powered;
        if (matGlow == null) return;
        if (powered)
        {
            matGlow.EnableKeyword("_EMISSION");
            matGlow.SetColor("_EmissionColor", baseGlowEmission);
        }
        else
        {
            matGlow.SetColor("_EmissionColor", Color.black);
        }
    }

    public virtual void SetHoverGlow(bool hovered)
    {
        if (matGlow == null) return;
        float t = hovered ? 0.25f : 0f;
        matBase.color  = Color.Lerp(baseBaseCol, baseGlowCol, t);
        matBody.color  = Color.Lerp(baseBodyCol, baseGlowCol, t);
        matAccent.color = Color.Lerp(baseAccentCol, baseGlowCol, t);
        matGlow.color  = Color.Lerp(baseGlowCol, Color.white, t);
        if (isPowered)
            matGlow.SetColor("_EmissionColor", hovered ? baseGlowEmission * 3f : baseGlowEmission);
    }

    // ── mesh primitives (delegate to MeshPrimitives) ───────────────

    protected Mesh Box(Vector3 center, float sizeX, float sizeY, float sizeZ)
        => MeshPrimitives.Box(center, sizeX, sizeY, sizeZ);

    protected Mesh RotatedBox(Vector3 center, float len, float width, float depth, float angleDeg)
        => MeshPrimitives.RotatedBox(center, len, width, depth, angleDeg);

    protected GameObject Spawn(string name, Mesh mesh, Material mat)
        => MeshPrimitives.Spawn(transform, name, mesh, mat, addCollider: true);

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

    // ── dashed route line ────────────────────────────────────────

    protected const float lineY = 0.06f;
    protected const float lineWidth = 0.12f;
    protected const float lineDash = 0.30f;
    protected const float lineGap = 0.15f;

    protected GameObject lineGO;
    MeshFilter lineMF;
    MeshRenderer lineMR;
    protected Material lineMat;
    protected Mesh lineMesh;
    protected readonly System.Collections.Generic.List<Vector3> lineWaypoints = new System.Collections.Generic.List<Vector3>();
    protected readonly System.Collections.Generic.List<float> lineCumulDist = new System.Collections.Generic.List<float>();
    protected float lineConsumed;

    /// <summary>Show a dashed line segment between two points.</summary>
    public virtual void ShowLine(Vector3 from, Vector3 to, Color color)
    {
        EnsureLine();
        lineGO.SetActive(true);
        lineGO.transform.position = Vector3.zero;
        lineGO.transform.rotation = Quaternion.identity;

        lineWaypoints.Clear();
        lineCumulDist.Clear();
        lineWaypoints.Add(new Vector3(from.x, lineY, from.z));
        lineWaypoints.Add(new Vector3(to.x, lineY, to.z));
        lineCumulDist.Add(0f);
        lineCumulDist.Add(Vector3.Distance(lineWaypoints[0], lineWaypoints[1]));
        lineConsumed = 0f;

        lineMat.color = color;
        lineMat.SetColor("_BaseColor", color);
        DashedRibbon.Build(lineMesh, lineWaypoints, lineCumulDist, 0f, lineWidth, lineDash, lineGap);
    }

    /// <summary>Update consumed distance on the line (for journey animation).</summary>
    public void UpdateLineConsumed(float t)
    {
        if (lineGO == null || !lineGO.activeSelf) return;
        float totalDist = lineCumulDist.Count > 1 ? lineCumulDist[lineCumulDist.Count - 1] : 0f;
        lineConsumed = t * totalDist;
        DashedRibbon.Build(lineMesh, lineWaypoints, lineCumulDist, lineConsumed, lineWidth, lineDash, lineGap);
    }

    /// <summary>Hide the dashed line.</summary>
    public virtual void HideLine()
    {
        if (lineGO != null) lineGO.SetActive(false);
    }

    protected void EnsureLine()
    {
        if (lineGO != null) return;

        lineGO = new GameObject("RouteLine");
        lineGO.transform.SetParent(transform, true);
        lineGO.transform.localPosition = Vector3.zero;
        lineMF = lineGO.AddComponent<MeshFilter>();
        lineMR = lineGO.AddComponent<MeshRenderer>();

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        lineMat = new Material(sh);
        lineMat.SetFloat("_Surface", 1f);
        lineMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        lineMat.SetOverrideTag("RenderType", "Transparent");
        lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMat.SetInt("_ZWrite", 0);
        lineMat.SetFloat("_Cull", 0f);
        lineMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
        lineMR.sharedMaterial = lineMat;
        lineMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        lineMesh = new Mesh { name = "WallRouteLine" };
        lineMF.sharedMesh = lineMesh;
        lineGO.SetActive(false);
    }
}
