using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static builder for the hauler drone mesh.
/// Bulky tracked chassis with an open-top cargo bin and a stubby grabber arm.
/// Designed to read as heavy and slow, with carried cargo visible in the bin.
/// </summary>
public static class HaulerDroneMesh
{
    public struct Result
    {
        public Transform[] wheels;   // road wheels (spin with movement)
        public Transform arm;        // grabber pillar (idle bob)
        public Transform claw;       // claw group

        // Where carried cargo should sit (local to drone root) and its scale.
        public Vector3 cargoAnchor;
        public float cargoScale;
    }

    public static Result Build(Transform parent, float chassisLength, float chassisWidth,
        Material matHull, Material matArm, Material matGlow)
    {
        float L = chassisLength;    // half-length along Z (~0.55)
        float W = chassisWidth;     // half-width along X  (~0.28)
        float groundY = -0.14f;     // bottom of tracks (matches old wheel bottom)

        // ── Tracks (two heavy tread units) ──
        float trackW = W * 0.55f;               // X thickness of each track
        float trackH = 0.22f;                   // height
        float trackLen = L * 2.05f;             // Z length
        float trackX = W * 1.0f;                // distance of each track from center
        float trackCY = groundY + trackH * 0.5f;
        float wheelR = trackH * 0.52f;

        var wheels = new List<Transform>();
        for (int side = 0; side < 2; side++)
        {
            float sx = side == 0 ? -trackX : trackX;

            // Track housing
            MeshPrimitives.Spawn(parent, $"Track{side}",
                MeshPrimitives.Box(new Vector3(sx, trackCY, 0f), trackW, trackH, trackLen), matArm);

            // Raised drive cover on top of each track end (mass + detail)
            MeshPrimitives.Spawn(parent, $"TrackCover{side}",
                MeshPrimitives.Box(new Vector3(sx, trackCY + trackH * 0.45f, trackLen * 0.36f),
                    trackW * 1.05f, trackH * 0.4f, trackLen * 0.3f), matHull);

            // Tread ridges along the outer face
            float outerX = sx + (side == 0 ? -1f : 1f) * (trackW * 0.5f);
            int ridges = 9;
            for (int r = 0; r < ridges; r++)
            {
                float tz = Mathf.Lerp(-trackLen * 0.46f, trackLen * 0.46f, r / (float)(ridges - 1));
                MeshPrimitives.Spawn(parent, $"Tread{side}_{r}",
                    MeshPrimitives.Box(new Vector3(outerX, groundY + trackH * 0.5f, tz),
                        0.03f, trackH * 0.92f, 0.045f), matHull);
            }

            // Two visible road wheels per side (these spin)
            for (int w = 0; w < 2; w++)
            {
                float wz = w == 0 ? -trackLen * 0.26f : trackLen * 0.26f;
                var wGO = MeshPrimitives.Spawn(parent, $"Wheel{side * 2 + w}",
                    WheelMesh(wheelR, trackW * 1.05f, 14), matHull);
                wGO.transform.localPosition = new Vector3(sx, groundY + wheelR, wz);
                wheels.Add(wGO.transform);
            }
        }

        // ── Main hull (sits on top of the tracks, bridging them) ──
        float hullW = trackX * 2f + trackW * 0.2f;
        float hullLen = L * 1.7f;
        float hullH = 0.24f;
        float hullBottom = groundY + trackH;
        float hullCY = hullBottom + hullH * 0.5f;
        MeshPrimitives.Spawn(parent, "Hull",
            MeshPrimitives.Box(new Vector3(0f, hullCY, -L * 0.05f), hullW, hullH, hullLen), matHull);

        // Beveled lower skirt for mass
        MeshPrimitives.Spawn(parent, "Skirt",
            MeshPrimitives.Box(new Vector3(0f, hullBottom + 0.03f, -L * 0.05f),
                hullW + 0.04f, 0.06f, hullLen + 0.04f), matArm);

        // Sloped front glacis / bumper
        MeshPrimitives.Spawn(parent, "Bumper",
            MeshPrimitives.Box(new Vector3(0f, hullBottom + 0.08f, hullLen * 0.5f - L * 0.05f),
                hullW * 0.92f, 0.16f, 0.10f), matArm);

        // ── Open-top cargo bin ──
        float binFloorY = hullCY + hullH * 0.5f;        // hull top
        float binOuterW = hullW * 0.86f;
        float binOuterL = hullLen * 0.74f;
        float binH = 0.24f;
        float wallT = 0.035f;
        float binZ = -L * 0.08f;

        MeshPrimitives.Spawn(parent, "BinFloor",
            MeshPrimitives.Box(new Vector3(0f, binFloorY + 0.02f, binZ),
                binOuterW, 0.04f, binOuterL), matArm);

        float wallCY = binFloorY + binH * 0.5f;
        // left / right walls (span Z)
        MeshPrimitives.Spawn(parent, "BinWallL",
            MeshPrimitives.Box(new Vector3(-binOuterW * 0.5f + wallT * 0.5f, wallCY, binZ),
                wallT, binH, binOuterL), matHull);
        MeshPrimitives.Spawn(parent, "BinWallR",
            MeshPrimitives.Box(new Vector3(binOuterW * 0.5f - wallT * 0.5f, wallCY, binZ),
                wallT, binH, binOuterL), matHull);
        // front / back walls (span X)
        MeshPrimitives.Spawn(parent, "BinWallF",
            MeshPrimitives.Box(new Vector3(0f, wallCY, binZ + binOuterL * 0.5f - wallT * 0.5f),
                binOuterW, binH, wallT), matHull);
        MeshPrimitives.Spawn(parent, "BinWallB",
            MeshPrimitives.Box(new Vector3(0f, wallCY, binZ - binOuterL * 0.5f + wallT * 0.5f),
                binOuterW, binH, wallT), matHull);

        // Glow rim around the bin top
        float rimY = binFloorY + binH;
        MeshPrimitives.Spawn(parent, "BinRimL",
            MeshPrimitives.Box(new Vector3(-binOuterW * 0.5f + wallT * 0.5f, rimY, binZ),
                wallT * 1.3f, 0.02f, binOuterL), matGlow);
        MeshPrimitives.Spawn(parent, "BinRimR",
            MeshPrimitives.Box(new Vector3(binOuterW * 0.5f - wallT * 0.5f, rimY, binZ),
                wallT * 1.3f, 0.02f, binOuterL), matGlow);

        // ── Headlights ──
        MeshPrimitives.Spawn(parent, "HeadlightL",
            MeshPrimitives.Box(new Vector3(-hullW * 0.28f, hullBottom + 0.12f, hullLen * 0.5f - L * 0.05f + 0.005f),
                0.06f, 0.05f, 0.01f), matGlow);
        MeshPrimitives.Spawn(parent, "HeadlightR",
            MeshPrimitives.Box(new Vector3(hullW * 0.28f, hullBottom + 0.12f, hullLen * 0.5f - L * 0.05f + 0.005f),
                0.06f, 0.05f, 0.01f), matGlow);

        // ── Stubby grabber arm (front, beside the bin) ──
        float armBaseW = W * 0.22f;
        float armBaseH = 0.16f;
        Vector3 armRoot = new Vector3(0f, binFloorY, hullLen * 0.5f - L * 0.05f - 0.04f);

        var armPillarGO = MeshPrimitives.Spawn(parent, "ArmPillar",
            MeshPrimitives.Box(Vector3.zero, armBaseW, armBaseH, armBaseW), matArm);
        armPillarGO.transform.localPosition = armRoot + Vector3.up * armBaseH * 0.5f;

        float forearmLen = L * 0.34f;
        var forearmGO = MeshPrimitives.Spawn(parent, "ArmForearm",
            MeshPrimitives.Box(new Vector3(0f, 0f, forearmLen * 0.5f),
                armBaseW * 0.6f, armBaseW * 0.5f, forearmLen), matHull);
        forearmGO.transform.localPosition = armRoot + Vector3.up * armBaseH;
        forearmGO.transform.SetParent(armPillarGO.transform, true);

        float clawLen = W * 0.28f;
        float prongW = 0.015f;
        Vector3 clawBase = armRoot + Vector3.up * armBaseH + Vector3.forward * forearmLen;
        var clawParent = new GameObject("Claw");
        clawParent.transform.SetParent(parent, false);
        clawParent.transform.localPosition = clawBase;
        MeshPrimitives.Spawn(clawParent.transform, "ProngL",
            MeshPrimitives.Box(new Vector3(-armBaseW * 0.28f, -clawLen * 0.4f, 0f),
                prongW, clawLen, prongW), matGlow);
        MeshPrimitives.Spawn(clawParent.transform, "ProngR",
            MeshPrimitives.Box(new Vector3(armBaseW * 0.28f, -clawLen * 0.4f, 0f),
                prongW, clawLen, prongW), matGlow);
        MeshPrimitives.Spawn(clawParent.transform, "ClawBar",
            MeshPrimitives.Box(Vector3.zero, armBaseW * 0.7f, prongW, prongW), matArm);

        // Cargo sits on the bin floor, scaled to fit between the walls.
        float cargoScale = (binOuterW - wallT * 2f) / 0.8f;   // crate body is 0.8 wide
        float cargoCenterY = binFloorY + 0.04f + 0.7f * cargoScale * 0.5f;

        return new Result
        {
            wheels = wheels.ToArray(),
            arm = armPillarGO.transform,
            claw = clawParent.transform,
            cargoAnchor = new Vector3(0f, cargoCenterY, binZ),
            cargoScale = cargoScale,
        };
    }

    /// <summary>Wheel mesh: cylinder with axle along X (rolls around X).</summary>
    static Mesh WheelMesh(float radius, float width, int segments)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float hw = width * 0.5f;
        int n = segments;

        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            float y = Mathf.Sin(a) * radius;
            float z = Mathf.Cos(a) * radius;
            verts.Add(new Vector3(-hw, y, z));
            verts.Add(new Vector3(hw, y, z));
        }
        int lc = verts.Count;
        verts.Add(new Vector3(-hw, 0, 0));
        verts.Add(new Vector3(hw, 0, 0));

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int l0 = i * 2, r0 = i * 2 + 1;
            int l1 = next * 2, r1 = next * 2 + 1;
            tris.Add(l0); tris.Add(l1); tris.Add(r1);
            tris.Add(l0); tris.Add(r1); tris.Add(r0);
            tris.Add(lc); tris.Add(l1); tris.Add(l0);
            tris.Add(lc + 1); tris.Add(r0); tris.Add(r1);
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }
}
