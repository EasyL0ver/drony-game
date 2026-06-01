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

    // ── Line drawing ────────────────────────────────────────

    public override void ShowLine(Vector3 from, Vector3 to, Color color)
    {
        BuildWaypoints();

        // Determine if we need to reverse waypoints (departing from the far end)
        float distToFirst = Vector3.Distance(new Vector3(from.x, 0, from.z), 
            new Vector3(localWaypoints[0].x, 0, localWaypoints[0].z));
        float distToLast = Vector3.Distance(new Vector3(from.x, 0, from.z), 
            new Vector3(localWaypoints[localWaypoints.Count - 1].x, 0, localWaypoints[localWaypoints.Count - 1].z));

        var orderedWaypoints = new List<Vector3>(localWaypoints);
        if (distToLast < distToFirst)
            orderedWaypoints.Reverse();

        EnsureLine();
        lineGO.SetActive(true);

        // Reset line transform to world origin so mesh vertices are world-space
        lineGO.transform.position = Vector3.zero;
        lineGO.transform.rotation = Quaternion.identity;

        lineWaypoints.Clear();
        lineCumulDist.Clear();

        // Start from the 'from' point (e.g. drone park position)
        var startPt = new Vector3(from.x, lineY, from.z);
        lineWaypoints.Add(startPt);
        lineCumulDist.Add(0f);

        // Add crooked waypoints
        float cumul = 0f;
        for (int i = 0; i < orderedWaypoints.Count; i++)
        {
            var pt = new Vector3(orderedWaypoints[i].x, lineY, orderedWaypoints[i].z);
            cumul += Vector3.Distance(lineWaypoints[lineWaypoints.Count - 1], pt);
            lineWaypoints.Add(pt);
            lineCumulDist.Add(cumul);
        }

        // End at the 'to' point (e.g. arrival park point)
        var endPt = new Vector3(to.x, lineY, to.z);
        float endDist = Vector3.Distance(lineWaypoints[lineWaypoints.Count - 1], endPt);
        if (endDist > 0.01f)
        {
            cumul += endDist;
            lineWaypoints.Add(endPt);
            lineCumulDist.Add(cumul);
        }

        lineConsumed = 0f;

        lineMat.color = color;
        lineMat.SetColor("_BaseColor", color);
        DashedRibbon.Build(lineMesh, lineWaypoints, lineCumulDist, 0f, lineWidth, lineDash, lineGap);
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
