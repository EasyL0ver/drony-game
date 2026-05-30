using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static builder for rubble barrier meshes (faceted parabolic wall + chunks + broken glow strips).
/// </summary>
public static class RubbleBarrierMesh
{
    public struct Params
    {
        public Vector3 center;
        public Vector3 along;
        public Vector3 across;
        public float halfLen;
        public float halfW;
        public float passH;
        public int seed;
    }

    /// <summary>
    /// Builds all rubble geometry as children of the given parent transform.
    /// Returns the broken glow strip parent so the caller can add RubbleFlicker components.
    /// </summary>
    public static void Build(Transform parent, Params p, Material matRubble, Material matWallChunk, Material matBrokenGlow)
    {
        var rng = new System.Random(p.seed);
        float F() => (float)rng.NextDouble();

        float maxDepth = p.halfLen * 1.0f;

        var verts = new List<Vector3>();
        var tris = new List<int>();

        int segX = 5;
        int segY = 5;

        // Front face vertices
        for (int yi = 0; yi <= segY; yi++)
        {
            float t = (float)yi / segY;
            float y = t * p.passH;
            float localHalfW = p.halfW;
            float baseDepth = maxDepth * (0.15f + 0.85f * (1f - t) * (1f - t));

            for (int xi = 0; xi <= segX; xi++)
            {
                float s = (float)xi / segX;
                float xPos = Mathf.Lerp(-localHalfW, localHalfW, s);
                float edgeDist = 1f - Mathf.Abs(s - 0.5f) * 2f;
                float edgeTaper = 0.4f + 0.6f * Mathf.Max(0, edgeDist);
                float depth = baseDepth * edgeTaper;
                depth += (F() - 0.3f) * 0.25f * maxDepth * edgeTaper * (1f - t);

                Vector3 pt = p.across * xPos + Vector3.up * y;
                verts.Add(pt + p.along * depth * 0.5f);
            }
        }

        // Back face vertices
        int backStart = verts.Count;
        float backScale = 0.6f + F() * 0.5f;
        for (int yi = 0; yi <= segY; yi++)
        {
            float t = (float)yi / segY;
            float y = t * p.passH;
            float localHalfW = p.halfW;
            float baseDepth = maxDepth * backScale * (0.2f + 0.8f * (1f - t) * (1f - 0.7f * t));

            for (int xi = 0; xi <= segX; xi++)
            {
                float s = (float)xi / segX;
                float xPos = Mathf.Lerp(-localHalfW, localHalfW, s);
                float edgeDist = 1f - Mathf.Abs(s - 0.5f) * 2f;
                float edgeTaper = 0.35f + 0.65f * Mathf.Max(0, edgeDist);
                float depth = baseDepth * edgeTaper;
                depth += (F() - 0.35f) * 0.3f * maxDepth * edgeTaper * (1f - t);

                Vector3 pt = p.across * xPos + Vector3.up * y;
                verts.Add(pt - p.along * depth * 0.5f);
            }
        }

        int w = segX + 1;

        // Triangulate front face
        for (int yi = 0; yi < segY; yi++)
            for (int xi = 0; xi < segX; xi++)
            {
                int a = yi * w + xi, b = a + 1, c2 = a + w, d = c2 + 1;
                tris.AddRange(new[] { a, c2, b, b, c2, d });
            }

        // Triangulate back face (reversed winding)
        for (int yi = 0; yi < segY; yi++)
            for (int xi = 0; xi < segX; xi++)
            {
                int a = backStart + yi * w + xi, b = a + 1, c2 = a + w, d = c2 + 1;
                tris.AddRange(new[] { a, b, c2, b, d, c2 });
            }

        // Stitch side edges
        for (int yi = 0; yi < segY; yi++)
        {
            int fl = yi * w, bl = backStart + yi * w;
            int flUp = fl + w, blUp = bl + w;
            tris.AddRange(new[] { fl, bl, flUp, bl, blUp, flUp });

            int fr = yi * w + segX, br = backStart + yi * w + segX;
            int frUp = fr + w, brUp = br + w;
            tris.AddRange(new[] { fr, frUp, br, br, frUp, brUp });
        }

        // Top edge stitching
        for (int xi = 0; xi < segX; xi++)
        {
            int ft = segY * w + xi, bt = backStart + segY * w + xi;
            int ft1 = ft + 1, bt1 = bt + 1;
            tris.AddRange(new[] { ft, ft1, bt, bt, ft1, bt1 });
        }

        // Wall chunks
        int numChunks = 3 + rng.Next(2);
        var wallChunkVerts = new List<Vector3>();
        var wallChunkTris = new List<int>();
        for (int cp = 0; cp < numChunks; cp++)
        {
            float py = F() * p.passH * 0.6f + p.passH * 0.1f;
            float t = py / p.passH;
            float px = (F() * 2f - 1f) * p.halfW * 0.4f;
            float depth = maxDepth * (1f - t) * (1f - t) * 0.5f;
            float side = F() > 0.5f ? 1f : -1f;

            Vector3 chunkBase = p.across * px + Vector3.up * py + p.along * side * depth;

            float cw = 0.2f + F() * 0.3f;
            float ch = 0.3f + F() * 0.4f;
            float cd = 0.04f + F() * 0.04f;

            float tiltFwd = (F() - 0.5f) * 0.8f;
            float tiltSide = (F() - 0.5f) * 0.4f;
            Vector3 panelUp = (Vector3.up + p.along * tiltFwd + p.across * tiltSide).normalized;
            Vector3 panelRight = Vector3.Cross(panelUp, p.along).normalized;
            Vector3 panelFwd = Vector3.Cross(panelRight, panelUp).normalized;

            Vector3 r = panelRight * cw * 0.5f;
            Vector3 u = panelUp * ch * 0.5f;
            Vector3 f = panelFwd * cd * 0.5f;

            int bv = wallChunkVerts.Count;
            wallChunkVerts.Add(chunkBase - r - u - f);
            wallChunkVerts.Add(chunkBase + r - u - f);
            wallChunkVerts.Add(chunkBase + r + u - f);
            wallChunkVerts.Add(chunkBase - r + u - f);
            wallChunkVerts.Add(chunkBase - r - u + f);
            wallChunkVerts.Add(chunkBase + r - u + f);
            wallChunkVerts.Add(chunkBase + r + u + f);
            wallChunkVerts.Add(chunkBase - r + u + f);

            wallChunkTris.AddRange(new[] {
                bv+0,bv+2,bv+1, bv+0,bv+3,bv+2,
                bv+4,bv+5,bv+6, bv+4,bv+6,bv+7,
                bv+0,bv+1,bv+5, bv+0,bv+5,bv+4,
                bv+2,bv+3,bv+7, bv+2,bv+7,bv+6,
                bv+0,bv+4,bv+7, bv+0,bv+7,bv+3,
                bv+1,bv+2,bv+6, bv+1,bv+6,bv+5,
            });
        }

        // Wall chunks mesh
        if (wallChunkVerts.Count > 0)
        {
            var chunkMesh = new Mesh { name = "RubbleWallChunks" };
            chunkMesh.SetVertices(wallChunkVerts);
            chunkMesh.SetTriangles(wallChunkTris, 0);
            chunkMesh.RecalculateNormals();
            var chunkGO = new GameObject("WallChunks");
            chunkGO.transform.SetParent(parent, false);
            chunkGO.AddComponent<MeshFilter>().sharedMesh = chunkMesh;
            chunkGO.AddComponent<MeshRenderer>().sharedMaterial = matWallChunk;
        }

        // Main rubble mesh
        var mesh = new Mesh { name = "RubbleMesh" };
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var meshGO = new GameObject("RubbleMesh");
        meshGO.transform.SetParent(parent, false);
        meshGO.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshGO.AddComponent<MeshRenderer>().sharedMaterial = matRubble;

        // Broken glow strips
        int stripCount = 3 + rng.Next(2);
        for (int i = 0; i < stripCount; i++)
        {
            float st = 0.15f + F() * 0.55f;
            float sx = (F() * 2f - 1f) * p.halfW * 0.5f;
            float depthAtT = maxDepth * (0.15f + 0.85f * (1f - st) * (1f - st));
            float side = F() > 0.5f ? 1f : -1f;

            Vector3 stripPos = p.across * sx + Vector3.up * (st * p.passH) + p.along * side * (depthAtT * 0.55f);

            var stripGO = new GameObject("BrokenGlowStrip");
            stripGO.transform.SetParent(parent, false);
            stripGO.transform.localPosition = stripPos;

            float tilt = (F() - 0.5f) * 40f;
            stripGO.transform.localRotation = Quaternion.Euler(tilt, 0f, (F() - 0.5f) * 25f);

            var stripMesh = new Mesh { name = "BrokenStrip" };
            float sw = 0.04f;
            float sh = 0.3f + F() * 0.2f;
            float sd = 0.015f;
            stripMesh.vertices = new[] {
                new Vector3(-sw, -sh, -sd), new Vector3(sw, -sh, -sd),
                new Vector3(sw, sh, -sd), new Vector3(-sw, sh, -sd),
                new Vector3(-sw, -sh, sd), new Vector3(sw, -sh, sd),
                new Vector3(sw, sh, sd), new Vector3(-sw, sh, sd),
            };
            stripMesh.triangles = new[] {
                0,2,1, 0,3,2,
                4,5,6, 4,6,7,
                0,1,5, 0,5,4,
                2,3,7, 2,7,6,
                0,4,7, 0,7,3,
                1,2,6, 1,6,5,
            };
            stripMesh.RecalculateNormals();

            stripGO.AddComponent<MeshFilter>().sharedMesh = stripMesh;
            stripGO.AddComponent<MeshRenderer>().sharedMaterial = matBrokenGlow;
            stripGO.AddComponent<RubbleFlicker>();
        }
    }
}
