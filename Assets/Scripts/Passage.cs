using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Wall entity representing a corridor/duct/vent passage on one side of a room.
/// Placed at the wall midpoint, facing into the room (+Z = inward).
/// Each connection spawns two Passage instances (one per room).
/// </summary>
public class Passage : WallView
{
    public override float ParkOffset => 0.5f;

    public PassageType Type { get; private set; }
    public Vector2Int Room { get; private set; }
    public Vector2Int Neighbor { get; private set; }
    public int Edge { get; private set; }

    public void Init(Vector2Int room, Vector2Int neighbor, int edge, PassageType type)
    {
        Room = room;
        Neighbor = neighbor;
        Edge = edge;
        Type = type;
    }

    // Passage glow material for power toggling (set after geometry is built)
    Material passageGlowMat;
    Color passageGlowEmission;

    /// <summary>Store the passage glow material for power state toggling.</summary>
    public void SetPassageGlow(Material mat)
    {
        passageGlowMat = mat;
        if (mat != null)
            passageGlowEmission = mat.GetColor("_EmissionColor");
    }

    public override void SetPowered(bool powered)
    {
        base.SetPowered(powered);
        if (passageGlowMat != null)
            passageGlowMat.SetColor("_EmissionColor", powered ? passageGlowEmission : Color.black);
    }

    public void UpdateType(PassageType newType)
    {
        Type = newType;
    }

    /// <summary>Set pipe geometry for vent ring animation.</summary>
    public void SetPipeInfo(Vector3 pipeStart, Vector3 pipeEnd, float pipeRadius)
    {
        ventPipeStart = pipeStart;
        ventPipeEnd = pipeEnd;
        ringsPipeRadius = pipeRadius;
        hasPipeInfo = true;
    }

    Vector3 ventPipeStart, ventPipeEnd;
    bool hasPipeInfo;

    // ── Vent light rings ────────────────────────────────────

    GameObject ringsGO;
    MeshFilter ringsMF;
    MeshRenderer ringsMR;
    Material ringsMat;
    Mesh ringsMesh;
    Coroutine ringsAnim;
    Vector3 ringsFrom, ringsTo;
    float ringsPipeRadius;
    const int ringCount = 4;
    const float ringSpeed = 0.5f;

    public override void ShowLine(Vector3 from, Vector3 to, Color color)
    {
        if (Type != PassageType.Vent || !hasPipeInfo)
        {
            base.ShowLine(from, to, color);
            return;
        }

        // Determine direction based on which end 'from' is closer to
        float dToStart = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(ventPipeStart.x, 0, ventPipeStart.z));
        float dToEnd = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(ventPipeEnd.x, 0, ventPipeEnd.z));
        ringsFrom = ventPipeStart;
        ringsTo = ventPipeEnd;
        if (dToEnd < dToStart)
        {
            ringsFrom = ventPipeEnd;
            ringsTo = ventPipeStart;
        }

        // Check if neighbor is fogged — clip rings at midpoint
        bool neighborFogged = Model?.Neighbor?.Owner?.State == FogState.Unknown;
        Vector3 ringsEnd = ringsTo;
        if (neighborFogged)
        {
            ringsEnd = (ringsFrom + ringsTo) * 0.5f;
            // Show dashed line for the fogged half
            Vector3 dashFrom = new Vector3(ringsEnd.x, lineY, ringsEnd.z);
            Vector3 dashTo = new Vector3(ringsTo.x, lineY, ringsTo.z);
            base.ShowLine(dashFrom, dashTo, color);
        }
        else
        {
            if (lineGO != null) lineGO.SetActive(false);
        }

        ringsTo = ringsEnd;

        EnsureRings();
        ringsGO.SetActive(true);

        ringsMat.color = color;
        ringsMat.SetColor("_BaseColor", color);
        ringsMat.SetColor("_EmissionColor", color * 3f);

        BuildRingsMesh(0f);
        if (ringsAnim != null) StopCoroutine(ringsAnim);
        ringsAnim = StartCoroutine(AnimateRings());
    }

    public override void HideLine()
    {
        base.HideLine();
        if (ringsGO != null) ringsGO.SetActive(false);
        if (ringsAnim != null) { StopCoroutine(ringsAnim); ringsAnim = null; }
    }

    void EnsureRings()
    {
        if (ringsGO != null) return;

        ringsGO = new GameObject("VentRings");
        ringsGO.transform.SetParent(transform, true);
        ringsGO.transform.position = Vector3.zero;
        ringsGO.transform.rotation = Quaternion.identity;

        ringsMF = ringsGO.AddComponent<MeshFilter>();
        ringsMR = ringsGO.AddComponent<MeshRenderer>();

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        ringsMat = new Material(sh);
        ringsMat.SetFloat("_Surface", 1f);
        ringsMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ringsMat.SetOverrideTag("RenderType", "Transparent");
        ringsMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        ringsMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ringsMat.SetInt("_ZWrite", 0);
        ringsMat.SetFloat("_Cull", 0f);
        ringsMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 2;
        ringsMR.sharedMaterial = ringsMat;
        ringsMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ringsMesh = new Mesh { name = "VentRingsMesh" };
        ringsMF.sharedMesh = ringsMesh;
        ringsGO.SetActive(false);
    }

    IEnumerator AnimateRings()
    {
        float phase = 0f;
        while (true)
        {
            phase += Time.deltaTime * ringSpeed;
            if (phase >= 1f) phase -= 1f;
            BuildRingsMesh(phase);
            yield return null;
        }
    }

    void BuildRingsMesh(float phase)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        Vector3 dir = (ringsTo - ringsFrom);
        float pipeLen = dir.magnitude;
        if (pipeLen < 0.01f) return;
        Vector3 axis = dir / pipeLen;

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(axis, up)) > 0.99f) up = Vector3.right;
        Vector3 right = Vector3.Cross(axis, up).normalized;
        Vector3 fwd = Vector3.Cross(right, axis).normalized;

        float bandW = ringsPipeRadius * 0.5f;
        float r = ringsPipeRadius * 1.01f;
        int seg = 10;

        for (int ring = 0; ring < ringCount; ring++)
        {
            float t = (phase + (float)ring / ringCount) % 1f;
            Vector3 center = ringsFrom + dir * t;
            // Fade at edges
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(t * 4f, (1f - t) * 4f));
            if (fade < 0.01f) continue;

            Vector3 halfD = axis * bandW * 0.5f;

            for (int i = 0; i < seg; i++)
            {
                float a1 = Mathf.PI * 2f * i / seg;
                float a2 = Mathf.PI * 2f * ((i + 1) % seg) / seg;
                Vector3 d1 = (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * r;
                Vector3 d2 = (right * Mathf.Cos(a2) + fwd * Mathf.Sin(a2)) * r;

                int vi = verts.Count;
                verts.Add(center + d1 - halfD);
                verts.Add(center + d1 + halfD);
                verts.Add(center + d2 + halfD);
                verts.Add(center + d2 - halfD);

                tris.Add(vi); tris.Add(vi + 1); tris.Add(vi + 2);
                tris.Add(vi); tris.Add(vi + 2); tris.Add(vi + 3);
            }
        }

        ringsMesh.Clear();
        ringsMesh.SetVertices(verts);
        ringsMesh.SetTriangles(tris, 0);
    }

    // ── Interactions ────────────────────────────────────────

    protected override IEnumerator RunInteraction(Transform drone, float duration, WallInteractionConfig config, int token, System.Action onComplete)
    {
        if (config != null && config.DestroysDrone)
        {
            yield return RunBombInteraction(drone, duration, token, onComplete);
            yield break;
        }

        // Default: beam interaction (e.g. non-bomb rubble clear if we ever add one)
        yield return base.RunInteraction(drone, duration, config, token, onComplete);
    }

    IEnumerator RunBombInteraction(Transform drone, float duration, int token, System.Action onComplete)
    {
        Vector3 wallPos = transform.position;
        Vector3 intoRoom = transform.forward;
        Vector3 startPos = drone.position;
        float hoverY = startPos.y;

        float pullBackTime = duration * 0.3f;
        float flashTime = duration * 0.3f;
        float chargeTime = duration * 0.4f;

        float pullBackDist = 1.2f;
        Vector3 pullBackTarget = startPos + intoRoom * pullBackDist;
        pullBackTarget.y = hoverY;

        Vector3 impactPos = wallPos;
        impactPos.y = hoverY;

        IDroneVisual droneVisual = drone.GetComponentInChildren<LowPolyDrone>() as IDroneVisual
                                ?? drone.GetComponentInChildren<HaulerDrone>() as IDroneVisual;
        Color bombRed = new Color(1f, 0.1f, 0f);

        // ── Phase 1: Arc backward ──
        float elapsed = 0f;
        while (elapsed < pullBackTime)
        {
            if (token != animationToken) yield break;
            float t = elapsed / pullBackTime;
            float ease = 1f - (1f - t) * (1f - t);
            Vector3 pos = Vector3.Lerp(startPos, pullBackTarget, ease);
            pos.y = hoverY + Mathf.Sin(t * Mathf.PI) * 0.4f;
            drone.position = pos;
            drone.rotation = Quaternion.LookRotation(intoRoom);
            elapsed += Time.deltaTime;
            yield return null;
        }
        drone.position = pullBackTarget;

        // ── Phase 2: Arming flash + vibrate ──
        droneVisual?.Flash(bombRed, flashTime + chargeTime);

        elapsed = 0f;
        while (elapsed < flashTime)
        {
            if (token != animationToken) yield break;
            float t = elapsed / flashTime;
            Vector3 shake = Random.insideUnitSphere * 0.03f * (0.5f + t);
            shake.y = 0f;
            drone.position = pullBackTarget + shake;
            float turnT = Mathf.SmoothStep(0f, 1f, t);
            drone.rotation = Quaternion.Slerp(
                Quaternion.LookRotation(intoRoom),
                Quaternion.LookRotation(-intoRoom),
                turnT);
            elapsed += Time.deltaTime;
            yield return null;
        }
        drone.position = pullBackTarget;
        drone.rotation = Quaternion.LookRotation(-intoRoom);

        // ── Phase 3: Charge into the wall ──
        elapsed = 0f;
        while (elapsed < chargeTime)
        {
            if (token != animationToken) yield break;
            float t = elapsed / chargeTime;
            float ease = t * t;
            Vector3 pos = Vector3.Lerp(pullBackTarget, impactPos, ease);
            pos.y = hoverY - t * 0.1f;
            drone.position = pos;
            elapsed += Time.deltaTime;
            yield return null;
        }
        drone.position = impactPos;

        activeAnimation = null;
        if (token == animationToken) onComplete?.Invoke();
    }
}
