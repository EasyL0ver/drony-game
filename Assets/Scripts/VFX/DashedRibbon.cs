using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Builds a dashed ribbon mesh from a list of waypoints.
/// Used for route preview and journey path visualization.
/// </summary>
public static class DashedRibbon
{
    public static void Build(Mesh targetMesh, List<Vector3> waypoints, List<float> cumulDist, float consumedDist, float width, float dash, float gap)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            Vector3 a = waypoints[i];
            Vector3 b = waypoints[i + 1];
            float segStart = cumulDist[i];
            float segEnd   = cumulDist[i + 1];
            float segLen   = segEnd - segStart;
            if (segLen < 0.001f) continue;

            if (segEnd <= consumedDist) continue;

            Vector3 dir = (b - a) / segLen;
            dir.y = 0f;
            Vector3 right = new Vector3(-dir.z, 0f, dir.x);
            float hw = width * 0.5f;
            float cycle = dash + gap;

            float local = 0f;
            while (local < segLen - 0.001f)
            {
                float worldDist = segStart + local;
                float phase = worldDist % cycle;

                if (phase < dash)
                {
                    float dashRemain = dash - phase;
                    float segRemain  = segLen - local;
                    float seg = Mathf.Min(dashRemain, segRemain);
                    if (seg < 0.001f) { local += 0.001f; continue; }
                    float dStart = worldDist;
                    float dEnd   = worldDist + seg;

                    if (dEnd > consumedDist)
                    {
                        float clampStart = Mathf.Max(dStart, consumedDist);
                        Vector3 p0 = a + dir * (clampStart - segStart);
                        Vector3 p1 = a + dir * (dEnd - segStart);

                        int vi = verts.Count;
                        verts.Add(p0 - right * hw);
                        verts.Add(p0 + right * hw);
                        verts.Add(p1 - right * hw);
                        verts.Add(p1 + right * hw);

                        tris.Add(vi);     tris.Add(vi + 2); tris.Add(vi + 1);
                        tris.Add(vi + 1); tris.Add(vi + 2); tris.Add(vi + 3);
                    }

                    local += seg;
                }
                else
                {
                    float gapRemain = cycle - phase;
                    local += Mathf.Max(Mathf.Min(gapRemain, segLen - local), 0.001f);
                }
            }
        }

        targetMesh.Clear();
        if (verts.Count > 0)
        {
            targetMesh.SetVertices(verts);
            targetMesh.SetTriangles(tris, 0);
            targetMesh.RecalculateBounds();
        }
    }
}
