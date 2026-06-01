using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// A crooked vent passage: drone navigates through multiple turns.
/// Overrides traversal and line drawing to follow a zigzag path between rooms.
/// </summary>
public class CrookedVentPassage : WallView
{
    public override float ParkOffset => 0.5f;

    public PassageType Type { get; private set; }
    public Vector2Int Room { get; private set; }
    public Vector2Int Neighbor { get; private set; }
    public int Edge { get; private set; }

    /// <summary>Local-space waypoints defining the crooked path (from park point to wall center).</summary>
    readonly List<Vector3> localWaypoints = new List<Vector3>();

    public void Init(Vector2Int room, Vector2Int neighbor, int edge, Vector3 pipeStart, Vector3 pipeEnd, int waypointSeed)
    {
        Room = room;
        Neighbor = neighbor;
        Edge = edge;
        Type = PassageType.CrookedVent;
        storedPipeStart = pipeStart;
        storedPipeEnd = pipeEnd;
        storedSeed = waypointSeed;
    }

    Vector3 storedPipeStart;
    Vector3 storedPipeEnd;
    int storedSeed;
    bool waypointsBuilt;

    /// <summary>
    /// Generate 90-degree turn waypoints between two points.
    /// Shared between mesh generation and runtime passage.
    /// Creates a Z-shape: forward, perpendicular, forward.
    /// </summary>
    public static System.Collections.Generic.List<Vector3> GenerateWaypoints(Vector3 start, Vector3 end, int seed)
    {
        var rng = new System.Random(seed);
        var waypoints = new System.Collections.Generic.List<Vector3>();

        Vector3 toEnd = end - start;
        float length = toEnd.magnitude;
        Vector3 fwd = toEnd.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        // Lateral offset amount
        float lateralDist = (float)(rng.NextDouble() * 0.2 + 0.15) * length;
        float sign = (seed & 2) == 0 ? 1f : -1f;

        // Split: how far along before the first turn, and before the last turn
        float t1 = 0.25f + (float)rng.NextDouble() * 0.15f;
        float t2 = 0.60f + (float)rng.NextDouble() * 0.15f;

        waypoints.Add(start);

        // First turn: go forward then turn perpendicular
        Vector3 p1 = start + fwd * (length * t1);
        p1.y = start.y;
        waypoints.Add(p1);

        // Perpendicular segment
        Vector3 p2 = p1 + right * (lateralDist * sign);
        p2.y = start.y;
        waypoints.Add(p2);

        // Second turn: go forward to align with end, then turn back
        Vector3 p3 = p2 + fwd * (length * (t2 - t1));
        p3.y = start.y;
        waypoints.Add(p3);

        // Turn back perpendicular to rejoin the end axis
        Vector3 p4 = p3 - right * (lateralDist * sign);
        p4.y = start.y;
        waypoints.Add(p4);

        waypoints.Add(end);
        return waypoints;
    }

    void BuildWaypoints()
    {
        if (waypointsBuilt) return;
        waypointsBuilt = true;

        Vector3 start = new Vector3(storedPipeStart.x, 0, storedPipeStart.z);
        Vector3 end = new Vector3(storedPipeEnd.x, 0, storedPipeEnd.z);

        var generated = GenerateWaypoints(start, end, storedSeed);

        localWaypoints.Clear();
        localWaypoints.AddRange(generated);
    }

    // ── Ring animation fields ───────────────────────────────

    GameObject ringsGO;
    MeshFilter ringsMF;
    MeshRenderer ringsMR;
    Material ringsMat;
    Mesh ringsMesh;
    Coroutine ringsAnim;
    List<Vector3> ringsWaypoints;
    List<float> ringsCumulDist;
    const int ringCount = 5;
    const float ringSpeed = 0.5f;
    const float ringPipeRadius = 0.22f;

    // ── Line drawing ────────────────────────────────────────

    public override void ShowLine(Vector3 from, Vector3 to, Color color)
    {
        BuildWaypoints();

        // Determine direction
        float distToFirst = Vector3.Distance(new Vector3(from.x, 0, from.z), 
            new Vector3(localWaypoints[0].x, 0, localWaypoints[0].z));
        float distToLast = Vector3.Distance(new Vector3(from.x, 0, from.z), 
            new Vector3(localWaypoints[localWaypoints.Count - 1].x, 0, localWaypoints[localWaypoints.Count - 1].z));

        // Build waypoints at pipe height for rings
        var pipeWaypoints = GenerateWaypoints(storedPipeStart, storedPipeEnd, storedSeed);
        if (distToLast < distToFirst)
            pipeWaypoints.Reverse();

        // Check if neighbor is fogged — clip rings at midpoint, show dashed line for far half
        bool neighborFogged = Model?.Neighbor?.Owner?.State == FogState.Unknown;
        if (neighborFogged)
        {
            float totalLen = 0f;
            for (int i = 1; i < pipeWaypoints.Count; i++)
                totalLen += Vector3.Distance(pipeWaypoints[i - 1], pipeWaypoints[i]);
            float halfLen = totalLen * 0.5f;

            // Find midpoint along path and truncate
            var clipped = new List<Vector3>();
            clipped.Add(pipeWaypoints[0]);
            float accum = 0f;
            for (int i = 1; i < pipeWaypoints.Count; i++)
            {
                float seg = Vector3.Distance(pipeWaypoints[i - 1], pipeWaypoints[i]);
                if (accum + seg >= halfLen)
                {
                    float t = (halfLen - accum) / seg;
                    clipped.Add(Vector3.Lerp(pipeWaypoints[i - 1], pipeWaypoints[i], t));
                    break;
                }
                clipped.Add(pipeWaypoints[i]);
                accum += seg;
            }
            // Dashed line for far half
            Vector3 midPt = clipped[clipped.Count - 1];
            Vector3 endPt = pipeWaypoints[pipeWaypoints.Count - 1];
            base.ShowLine(new Vector3(midPt.x, lineY, midPt.z), new Vector3(endPt.x, lineY, endPt.z), color);

            pipeWaypoints = clipped;
        }
        else
        {
            if (lineGO != null) lineGO.SetActive(false);
        }

        ringsWaypoints = pipeWaypoints;
        ringsCumulDist = new List<float>();
        float cumul = 0f;
        ringsCumulDist.Add(0f);
        for (int i = 1; i < ringsWaypoints.Count; i++)
        {
            cumul += Vector3.Distance(ringsWaypoints[i - 1], ringsWaypoints[i]);
            ringsCumulDist.Add(cumul);
        }

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

        ringsMesh = new Mesh { name = "CrookedVentRings" };
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

        float totalLen = ringsCumulDist[ringsCumulDist.Count - 1];
        if (totalLen < 0.01f) return;

        float bandW = ringPipeRadius * 0.5f;
        float r = ringPipeRadius * 1.01f;
        int seg = 10;

        for (int ring = 0; ring < ringCount; ring++)
        {
            float t = (phase + (float)ring / ringCount) % 1f;
            float targetDist = t * totalLen;

            // Find position along multi-segment path
            Vector3 center = ringsWaypoints[0];
            Vector3 axis = Vector3.forward;
            for (int i = 0; i < ringsCumulDist.Count - 1; i++)
            {
                if (ringsCumulDist[i + 1] >= targetDist)
                {
                    float segLen = ringsCumulDist[i + 1] - ringsCumulDist[i];
                    float segT = segLen > 0.001f ? (targetDist - ringsCumulDist[i]) / segLen : 0f;
                    center = Vector3.Lerp(ringsWaypoints[i], ringsWaypoints[i + 1], segT);
                    axis = (ringsWaypoints[i + 1] - ringsWaypoints[i]).normalized;
                    break;
                }
            }

            // Fade at edges
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(t * 4f, (1f - t) * 4f));
            if (fade < 0.01f) continue;

            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(axis, up)) > 0.99f) up = Vector3.right;
            Vector3 right = Vector3.Cross(axis, up).normalized;
            Vector3 fwd = Vector3.Cross(right, axis).normalized;
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

    // ── Traversal distance ─────────────────────────────────

    public override float GetTraversalDistance(Vector3 from)
    {
        BuildWaypoints();
        float dist = 0f;
        Vector3 prev = from;
        prev.y = 0;
        for (int i = 0; i < localWaypoints.Count; i++)
        {
            Vector3 wp = localWaypoints[i];
            wp.y = 0;
            dist += Vector3.Distance(prev, wp);
            prev = wp;
        }
        return dist;
    }

    // ── Traversal animation ─────────────────────────────────

    protected override IEnumerator RunTraversal(Transform drone, float duration, bool departing, int token, System.Action onComplete)
    {
        // Arrival side: straight hop from wall mid to park point (base behavior)
        if (!departing)
        {
            yield return base.RunTraversal(drone, duration, departing, token, onComplete);
            yield break;
        }

        BuildWaypoints();
        var waypoints = new List<Vector3>(localWaypoints);
        float hoverY = drone.position.y;

        // Set all waypoints to drone hover height
        for (int i = 0; i < waypoints.Count; i++)
            waypoints[i] = new Vector3(waypoints[i].x, hoverY, waypoints[i].z);

        // If drone is closer to the end, reverse the path (departing from the other side)
        float distToFirst = Vector3.Distance(drone.position, waypoints[0]);
        float distToLast = Vector3.Distance(drone.position, waypoints[waypoints.Count - 1]);
        if (distToLast < distToFirst)
            waypoints.Reverse();

        // Compute total path length for uniform speed
        float totalLength = 0f;
        var segLengths = new List<float>();
        for (int i = 1; i < waypoints.Count; i++)
        {
            float seg = Vector3.Distance(waypoints[i - 1], waypoints[i]);
            segLengths.Add(seg);
            totalLength += seg;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (isReversing) break;
            float t = elapsed / duration;
            UpdateLineConsumed(t);

            // Find position along the multi-segment path
            float targetDist = t * totalLength;
            float accumulated = 0f;
            Vector3 pos = waypoints[0];
            Vector3 dir = Vector3.forward;

            for (int i = 0; i < segLengths.Count; i++)
            {
                if (accumulated + segLengths[i] >= targetDist)
                {
                    float segT = (targetDist - accumulated) / segLengths[i];
                    pos = Vector3.Lerp(waypoints[i], waypoints[i + 1], segT);
                    dir = (waypoints[i + 1] - waypoints[i]).normalized;
                    break;
                }
                accumulated += segLengths[i];
                pos = waypoints[i + 1];
                if (i + 1 < waypoints.Count - 1)
                    dir = (waypoints[i + 2] - waypoints[i + 1]).normalized;
            }

            drone.position = pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (isReversing)
        {
            // Reverse back along path
            Vector3 reverseFrom = drone.position;
            Vector3 reverseTarget = waypoints[0];
            float reverseTime = elapsed;
            float reverseElapsed = 0f;
            while (reverseElapsed < reverseTime)
            {
                float t = reverseElapsed / reverseTime;
                drone.position = Vector3.Lerp(reverseFrom, reverseTarget, t);
                Vector3 dir = (reverseTarget - reverseFrom).normalized;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
                reverseElapsed += Time.deltaTime;
                yield return null;
            }
            drone.position = reverseTarget;
            activeAnimation = null;
            isReversing = false;
            reverseCallback?.Invoke();
            reverseCallback = null;
            yield break;
        }

        drone.position = waypoints[waypoints.Count - 1];
        HideLine();
        activeAnimation = null;
        if (token == animationToken) onComplete?.Invoke();
    }
}
