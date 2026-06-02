using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static builder for the hauler drone mesh.
/// Wheeled chassis, open cargo bay, mechanical grabber arm.
/// </summary>
public static class HaulerDroneMesh
{
    public struct Result
    {
        public Transform[] wheels;
        public Transform arm;
        public Transform claw;
    }

    public static Result Build(Transform parent, float chassisLength, float chassisWidth,
        Material matHull, Material matArm, Material matGlow)
    {
        float cL = chassisLength;   // half-length along Z
        float cW = chassisWidth;    // half-width along X
        float cH = cW * 0.35f;     // chassis half-height
        float wheelR = cW * 0.4f;
        float wheelW = cW * 0.18f;
        float axleY = -cH * 0.3f;

        // ── Main chassis (box) ──
        MeshPrimitives.Spawn(parent, "Chassis",
            MeshPrimitives.Box(new Vector3(0, 0, 0), cW * 2f, cH * 2f, cL * 2f), matHull);

        // ── Cargo bay (recessed area on top, slightly inset) ──
        float bayInset = 0.02f;
        float bayDepth = cH * 0.6f;
        float bayW = cW - bayInset * 2f;
        float bayL = cL * 0.6f;
        // Floor of cargo bay
        MeshPrimitives.Spawn(parent, "CargoBayFloor",
            MeshPrimitives.Box(new Vector3(0, cH - bayDepth * 0.5f, -cL * 0.1f),
                bayW * 2f, 0.01f, bayL * 2f), matArm);
        // Bay walls (thin boxes on sides)
        float wallT = 0.01f;
        MeshPrimitives.Spawn(parent, "BayWallL",
            MeshPrimitives.Box(new Vector3(-bayW, cH - bayDepth * 0.5f + bayDepth * 0.5f, -cL * 0.1f),
                wallT, bayDepth, bayL * 2f), matArm);
        MeshPrimitives.Spawn(parent, "BayWallR",
            MeshPrimitives.Box(new Vector3(bayW, cH - bayDepth * 0.5f + bayDepth * 0.5f, -cL * 0.1f),
                wallT, bayDepth, bayL * 2f), matArm);
        MeshPrimitives.Spawn(parent, "BayWallBack",
            MeshPrimitives.Box(new Vector3(0, cH - bayDepth * 0.5f + bayDepth * 0.5f, -cL * 0.1f - bayL),
                bayW * 2f, bayDepth, wallT), matArm);

        // ── Glow strip (status indicator on front) ──
        MeshPrimitives.Spawn(parent, "GlowStrip",
            MeshPrimitives.Box(new Vector3(0, cH * 0.3f, cL + 0.005f),
                cW * 1.2f, cH * 0.25f, 0.008f), matGlow);
        // Rear glow
        MeshPrimitives.Spawn(parent, "GlowRear",
            MeshPrimitives.Box(new Vector3(0, cH * 0.3f, -cL - 0.005f),
                cW * 0.8f, cH * 0.15f, 0.008f), matGlow);

        // ── Wheels (4, Prism-based cylinders on sides) ──
        var wheels = new Transform[4];
        float[][] wheelPos = new float[][]
        {
            new[] { -cW - wheelW * 0.5f, axleY, cL * 0.6f },
            new[] {  cW + wheelW * 0.5f, axleY, cL * 0.6f },
            new[] { -cW - wheelW * 0.5f, axleY, -cL * 0.6f },
            new[] {  cW + wheelW * 0.5f, axleY, -cL * 0.6f },
        };
        for (int i = 0; i < 4; i++)
        {
            var wGO = MeshPrimitives.Spawn(parent, $"Wheel{i}",
                WheelMesh(wheelR, wheelW, 12), matArm);
            wGO.transform.localPosition = new Vector3(wheelPos[i][0], wheelPos[i][1], wheelPos[i][2]);
            wGO.transform.localRotation = Quaternion.Euler(0, 0, 90);
            wheels[i] = wGO.transform;
        }

        // ── Grabber arm (articulated: base pillar + forearm + claw) ──
        float armBaseH = cH * 1.2f;
        float armBaseW = cW * 0.25f;
        Vector3 armRoot = new Vector3(0, cH, cL * 0.55f);

        // Base pillar
        var armPillarGO = MeshPrimitives.Spawn(parent, "ArmPillar",
            MeshPrimitives.Box(Vector3.zero, armBaseW, armBaseH, armBaseW), matHull);
        armPillarGO.transform.localPosition = armRoot + Vector3.up * armBaseH * 0.5f;
        var armT = armPillarGO.transform;

        // Forearm (extends forward)
        float forearmLen = cL * 0.4f;
        var forearmGO = MeshPrimitives.Spawn(parent, "ArmForearm",
            MeshPrimitives.Box(new Vector3(0, 0, forearmLen * 0.5f),
                armBaseW * 0.7f, armBaseW * 0.5f, forearmLen), matArm);
        forearmGO.transform.localPosition = armRoot + Vector3.up * armBaseH;
        forearmGO.transform.SetParent(armPillarGO.transform, true);

        // Claw (two prongs)
        float clawLen = cW * 0.3f;
        float prongW = 0.008f;
        Vector3 clawBase = armRoot + Vector3.up * armBaseH + Vector3.forward * forearmLen;
        var clawParent = new GameObject("Claw");
        clawParent.transform.SetParent(parent, false);
        clawParent.transform.localPosition = clawBase;
        MeshPrimitives.Spawn(clawParent.transform, "ProngL",
            MeshPrimitives.Box(new Vector3(-armBaseW * 0.3f, -clawLen * 0.5f, 0),
                prongW, clawLen, prongW), matGlow);
        MeshPrimitives.Spawn(clawParent.transform, "ProngR",
            MeshPrimitives.Box(new Vector3(armBaseW * 0.3f, -clawLen * 0.5f, 0),
                prongW, clawLen, prongW), matGlow);
        // Cross bar
        MeshPrimitives.Spawn(clawParent.transform, "ClawBar",
            MeshPrimitives.Box(new Vector3(0, 0, 0),
                armBaseW * 0.7f, prongW, prongW), matArm);

        return new Result { wheels = wheels, arm = armT, claw = clawParent.transform };
    }

    /// <summary>Wheel mesh: cylinder lying on its side (built as Prism rotated later).</summary>
    static Mesh WheelMesh(float radius, float width, int segments)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float hw = width * 0.5f;
        int n = segments;

        // Two circles
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            float x = Mathf.Cos(a) * radius;
            float y = Mathf.Sin(a) * radius;
            verts.Add(new Vector3(-hw, y, x)); // left circle
            verts.Add(new Vector3(hw, y, x));  // right circle
        }
        int lc = verts.Count;
        verts.Add(new Vector3(-hw, 0, 0)); // left center
        verts.Add(new Vector3(hw, 0, 0));  // right center

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int l0 = i * 2, r0 = i * 2 + 1;
            int l1 = next * 2, r1 = next * 2 + 1;

            // Side quad
            tris.Add(l0); tris.Add(l1); tris.Add(r1);
            tris.Add(l0); tris.Add(r1); tris.Add(r0);
            // Left cap
            tris.Add(lc); tris.Add(l1); tris.Add(l0);
            // Right cap
            tris.Add(lc + 1); tris.Add(r0); tris.Add(r1);
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }
}
