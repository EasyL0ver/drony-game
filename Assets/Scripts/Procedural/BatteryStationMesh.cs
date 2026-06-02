using UnityEngine;

/// <summary>
/// Generates the procedural mesh parts for a battery power source station.
/// Looks like a chunky industrial power cell / capacitor bank on the wall.
/// All geometry in local space: Z+ = into room, Z- = into wall, Y+ = up.
/// </summary>
public static class BatteryStationMesh
{
    public static void Build(Transform parent, Material matBase, Material matBody, Material matAccent, Material matGlow)
    {
        // Wall backplate
        Spawn(parent, "Backplate", MeshPrimitives.Box(new Vector3(0, 0.65f, -0.04f), 1.6f, 1.4f, 0.08f), matBase);

        // Main battery housing — wide and boxy
        Spawn(parent, "Housing", MeshPrimitives.Box(new Vector3(0, 0.6f, 0.25f), 1.2f, 1.0f, 0.5f), matBody);

        // Top vent/heat sink
        Spawn(parent, "TopVent", MeshPrimitives.Box(new Vector3(0, 1.15f, 0.2f), 1.0f, 0.06f, 0.4f), matAccent);

        // Base plate
        Spawn(parent, "BasePlate", MeshPrimitives.Box(new Vector3(0, 0.06f, 0.2f), 1.3f, 0.08f, 0.5f), matBase);

        // Power cells (three vertical columns)
        Spawn(parent, "CellL", MeshPrimitives.Box(new Vector3(-0.35f, 0.6f, 0.35f), 0.22f, 0.7f, 0.22f), matAccent);
        Spawn(parent, "CellC", MeshPrimitives.Box(new Vector3(0f, 0.6f, 0.35f), 0.22f, 0.7f, 0.22f), matAccent);
        Spawn(parent, "CellR", MeshPrimitives.Box(new Vector3(0.35f, 0.6f, 0.35f), 0.22f, 0.7f, 0.22f), matAccent);

        // Glow strips on cells (energy indicators)
        float glowZ = 0.47f;
        Spawn(parent, "GlowL", MeshPrimitives.Box(new Vector3(-0.35f, 0.6f, glowZ), 0.04f, 0.6f, 0.02f), matGlow);
        Spawn(parent, "GlowC", MeshPrimitives.Box(new Vector3(0f, 0.6f, glowZ), 0.04f, 0.6f, 0.02f), matGlow);
        Spawn(parent, "GlowR", MeshPrimitives.Box(new Vector3(0.35f, 0.6f, glowZ), 0.04f, 0.6f, 0.02f), matGlow);

        // Front status bar (horizontal)
        Spawn(parent, "StatusBar", MeshPrimitives.Box(new Vector3(0, 0.2f, 0.51f), 0.9f, 0.06f, 0.02f), matGlow);

        // Cable connectors on sides
        Spawn(parent, "ConnL", MeshPrimitives.Box(new Vector3(-0.7f, 0.4f, 0.15f), 0.14f, 0.3f, 0.14f), matBase);
        Spawn(parent, "ConnR", MeshPrimitives.Box(new Vector3(0.7f, 0.4f, 0.15f), 0.14f, 0.3f, 0.14f), matBase);
        Spawn(parent, "ConnGlowL", MeshPrimitives.Box(new Vector3(-0.7f, 0.4f, 0.23f), 0.08f, 0.08f, 0.02f), matGlow);
        Spawn(parent, "ConnGlowR", MeshPrimitives.Box(new Vector3(0.7f, 0.4f, 0.23f), 0.08f, 0.08f, 0.02f), matGlow);
    }

    static void Spawn(Transform parent, string name, Mesh mesh, Material mat)
        => MeshPrimitives.Spawn(parent, name, mesh, mat, addCollider: true);
}
