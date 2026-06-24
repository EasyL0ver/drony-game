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

        // ── Triangular tracks (apex drive sprocket + two bottom idlers) ──
        // Massive tracks that rise ABOVE the hull, with the body slung low between them.
        float trackW = W * 0.72f;               // X thickness of each track (chunky)
        float apexH = 0.34f;                    // height of the apex above ground (tall)
        float trackLen = L * 1.95f;             // bottom-run length (Z)
        float beltT = 0.11f;                    // belt beam thickness (massive)
        float idlerR = 0.10f;                   // bottom idler wheel radius
        float sprocketR = 0.12f;                // apex drive sprocket radius
        float baseHalf = trackLen * 0.5f;

        // Hull half-width + a clean gap so the tracks sit OUTSIDE the hull mesh
        // (no overlap / blending artifacts where they meet).
        float bodyHalfW = W * 0.82f;
        float trackGap = 0.03f;
        float trackX = bodyHalfW + trackGap + trackW * 0.5f;  // track center

        var wheels = new List<Transform>();
        for (int side = 0; side < 2; side++)
        {
            float sx = side == 0 ? -trackX : trackX;

            Vector3 apex  = new Vector3(sx, groundY + apexH, 0f);
            Vector3 botF  = new Vector3(sx, groundY,          baseHalf);
            Vector3 botB  = new Vector3(sx, groundY,         -baseHalf);

            // Bottom belt run
            MeshPrimitives.Spawn(parent, $"TrackBase{side}",
                MeshPrimitives.Box(new Vector3(sx, groundY + beltT * 0.5f, 0f),
                    trackW, beltT, trackLen), matArm);

            // Two diagonal belt beams rising to the apex (box + transform pitch)
            SpawnBeam(parent, $"TrackFront{side}", botF, apex, trackW, beltT, matArm);
            SpawnBeam(parent, $"TrackBack{side}",  botB, apex, trackW, beltT, matArm);

            // Glowing energy stripes running up each diagonal beam
            float gx = sx + (side == 0 ? -trackW * 0.5f : trackW * 0.5f);
            Vector3 gApex = new Vector3(gx, groundY + apexH, 0f);
            SpawnBeam(parent, $"TrackGlowF{side}",
                new Vector3(gx, groundY + beltT, baseHalf * 0.9f), gApex, 0.02f, 0.02f, matGlow);
            SpawnBeam(parent, $"TrackGlowB{side}",
                new Vector3(gx, groundY + beltT, -baseHalf * 0.9f), gApex, 0.02f, 0.02f, matGlow);

            // Glowing apex cap on the drive sprocket
            MeshPrimitives.Spawn(parent, $"ApexGlow{side}",
                MeshPrimitives.Box(new Vector3(sx, groundY + apexH + 0.01f, 0f),
                    trackW * 1.15f, 0.03f, 0.10f), matGlow);

            // Inner frame plate filling the triangle (mass, slightly inset)
            MeshPrimitives.Spawn(parent, $"TrackFrame{side}",
                MeshPrimitives.Box(new Vector3(sx, groundY + apexH * 0.42f, 0f),
                    trackW * 0.6f, apexH * 0.5f, trackLen * 0.5f), matHull);

            // Tread cleats along the ground run — span the full belt width
            int cleats = 11;
            for (int c = 0; c < cleats; c++)
            {
                float tz = Mathf.Lerp(-baseHalf * 0.92f, baseHalf * 0.92f, c / (float)(cleats - 1));
                MeshPrimitives.Spawn(parent, $"Cleat{side}_{c}",
                    MeshPrimitives.Box(new Vector3(sx, groundY + 0.018f, tz),
                        trackW * 1.14f, 0.036f, 0.05f), matHull);
            }

            // Wheels: apex drive sprocket + two bottom idlers (these spin)
            var sproket = MeshPrimitives.Spawn(parent, $"Wheel{side * 3 + 0}",
                WheelMesh(sprocketR, trackW * 1.1f, 16), matHull);
            sproket.transform.localPosition = apex - Vector3.up * sprocketR * 0.25f;
            wheels.Add(sproket.transform);

            var idF = MeshPrimitives.Spawn(parent, $"Wheel{side * 3 + 1}",
                WheelMesh(idlerR, trackW * 1.1f, 16), matHull);
            idF.transform.localPosition = new Vector3(sx, groundY + idlerR, baseHalf - idlerR * 0.6f);
            wheels.Add(idF.transform);

            var idB = MeshPrimitives.Spawn(parent, $"Wheel{side * 3 + 2}",
                WheelMesh(idlerR, trackW * 1.1f, 16), matHull);
            idB.transform.localPosition = new Vector3(sx, groundY + idlerR, -baseHalf + idlerR * 0.6f);
            wheels.Add(idB.transform);
        }

        // ── Main hull (slung low BETWEEN the tracks; tracks rise above it) ──
        float hullW = bodyHalfW * 2f;
        float hullLen = L * 1.7f;
        float hullH = 0.18f;
        float hullBottom = groundY + 0.05f;     // low to the ground
        float hullCY = hullBottom + hullH * 0.5f;
        MeshPrimitives.Spawn(parent, "Hull",
            MeshPrimitives.Box(new Vector3(0f, hullCY, -L * 0.05f), hullW, hullH, hullLen), matHull);

        // Underglow strip spanning between the two tracks
        MeshPrimitives.Spawn(parent, "Underglow",
            MeshPrimitives.Box(new Vector3(0f, hullBottom - 0.005f, -L * 0.05f),
                hullW * 0.7f, 0.02f, hullLen * 0.78f), matGlow);

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
        float binH = 0.15f;
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

        // ── Hazard chevrons on the front bumper (glowing accent stripes) ──
        float bumpZ = hullLen * 0.5f - L * 0.05f + 0.051f;
        float bumpY = hullBottom + 0.08f;
        for (int i = 0; i < 5; i++)
        {
            float cx = Mathf.Lerp(-hullW * 0.36f, hullW * 0.36f, i / 4f);
            MeshPrimitives.Spawn(parent, $"Chevron{i}",
                MeshPrimitives.RotatedBox(new Vector3(cx, bumpY, bumpZ),
                    0.14f, 0.03f, 0.012f, 45f), matGlow);
        }

        // ── Hydraulic loader arm (front-right, built in local space) ──
        float armBaseW = W * 0.30f;
        float armBaseH = 0.22f;
        Vector3 armRoot = new Vector3(hullW * 0.30f, binFloorY, hullLen * 0.30f);

        var armGroup = new GameObject("ArmPillar");
        armGroup.transform.SetParent(parent, false);
        armGroup.transform.localPosition = armRoot;
        var armT = armGroup.transform;

        // Pillar (base at y=0, rising up)
        MeshPrimitives.Spawn(armT, "Pillar",
            MeshPrimitives.Box(new Vector3(0f, armBaseH * 0.5f, 0f),
                armBaseW, armBaseH, armBaseW), matArm);
        // Shoulder block
        MeshPrimitives.Spawn(armT, "Shoulder",
            MeshPrimitives.Box(new Vector3(0f, armBaseH, 0f),
                armBaseW * 1.15f, armBaseW * 0.7f, armBaseW * 1.15f), matHull);

        // Boom: forward beam from the shoulder out over the bin
        float boomLen = L * 0.42f;
        float boomY = armBaseH + armBaseW * 0.15f;
        MeshPrimitives.Spawn(armT, "Boom",
            MeshPrimitives.Box(new Vector3(0f, boomY, boomLen * 0.5f),
                armBaseW * 0.5f, armBaseW * 0.55f, boomLen), matHull);

        // Claw hangs from the boom tip
        float clawLen = W * 0.34f;
        float prongW = 0.022f;
        var clawParent = new GameObject("Claw");
        clawParent.transform.SetParent(armT, false);
        clawParent.transform.localPosition = new Vector3(0f, boomY, boomLen);
        MeshPrimitives.Spawn(clawParent.transform, "ClawWrist",
            MeshPrimitives.Box(Vector3.zero, armBaseW * 0.8f, prongW * 1.6f, armBaseW * 0.5f), matArm);
        MeshPrimitives.Spawn(clawParent.transform, "ProngL",
            MeshPrimitives.Box(new Vector3(-armBaseW * 0.34f, -clawLen * 0.45f, 0f),
                prongW, clawLen, prongW), matGlow);
        MeshPrimitives.Spawn(clawParent.transform, "ProngR",
            MeshPrimitives.Box(new Vector3(armBaseW * 0.34f, -clawLen * 0.45f, 0f),
                prongW, clawLen, prongW), matGlow);

        var armPillarGO = armGroup;

        // Cargo nests in the bin: width-fit, but capped by bin depth so the crate
        // sits inside and only its glowing top peeks above the rim.
        float widthFit = (binOuterW - wallT * 2f) / 0.8f;   // crate body is 0.8 wide
        float depthFit = (binH * 1.3f) / 0.8f;              // crate body is 0.8 tall
        float cargoScale = Mathf.Min(widthFit, depthFit);
        float cargoCenterY = binFloorY + 0.04f + 0.8f * cargoScale * 0.5f;

        return new Result
        {
            wheels = wheels.ToArray(),
            arm = armPillarGO.transform,
            claw = clawParent.transform,
            cargoAnchor = new Vector3(0f, cargoCenterY, binZ),
            cargoScale = cargoScale,
        };
    }

    /// <summary>Spawn a box "beam" spanning from <paramref name="from"/> to
    /// <paramref name="to"/>, with the given cross-section (X width, Y thickness).
    /// Uses transform rotation so the beam can pitch (which RotatedBox can't do).</summary>
    static void SpawnBeam(Transform parent, string name, Vector3 from, Vector3 to,
        float width, float thickness, Material mat)
    {
        Vector3 dir = to - from;
        float len = dir.magnitude;
        var go = MeshPrimitives.Spawn(parent, name,
            MeshPrimitives.Box(Vector3.zero, width, thickness, len), mat);
        go.transform.localPosition = (from + to) * 0.5f;
        go.transform.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
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
