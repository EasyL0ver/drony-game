using UnityEngine;

/// <summary>
/// Generates the procedural mesh parts for a charging station.
/// All geometry in local space: Z+ = into room, Z- = into wall, Y+ = up.
/// </summary>
public static class ChargingStationMesh
{
    public static void Build(Transform parent, Material matBase, Material matBody, Material matAccent, Material matGlow)
    {
        Spawn(parent, "Backplate", MeshPrimitives.Box(new Vector3(0, 0.65f, -0.04f), 1.4f, 1.3f, 0.08f), matBase);
        Spawn(parent, "Housing", MeshPrimitives.Box(new Vector3(0, 0.6f, 0.3f), 1.0f, 1.0f, 0.6f), matBody);
        Spawn(parent, "Canopy", MeshPrimitives.Box(new Vector3(0, 1.15f, 0.25f), 1.2f, 0.08f, 0.5f), matAccent);
        Spawn(parent, "BaseLedge", MeshPrimitives.Box(new Vector3(0, 0.06f, 0.2f), 1.2f, 0.06f, 0.4f), matBase);
        Spawn(parent, "FrontGlow", MeshPrimitives.Box(new Vector3(0, 0.6f, 0.61f), 0.8f, 0.06f, 0.02f), matGlow);
        Spawn(parent, "GlowStripL", MeshPrimitives.Box(new Vector3(-0.35f, 0.6f, 0.61f), 0.04f, 0.7f, 0.02f), matGlow);
        Spawn(parent, "GlowStripR", MeshPrimitives.Box(new Vector3( 0.35f, 0.6f, 0.61f), 0.04f, 0.7f, 0.02f), matGlow);
        Spawn(parent, "ConduitL", MeshPrimitives.Box(new Vector3(-0.6f, 0.5f, 0.15f), 0.12f, 0.8f, 0.2f), matAccent);
        Spawn(parent, "ConduitR", MeshPrimitives.Box(new Vector3( 0.6f, 0.5f, 0.15f), 0.12f, 0.8f, 0.2f), matAccent);
        Spawn(parent, "CondCapL", MeshPrimitives.Box(new Vector3(-0.6f, 0.92f, 0.15f), 0.14f, 0.06f, 0.22f), matGlow);
        Spawn(parent, "CondCapR", MeshPrimitives.Box(new Vector3( 0.6f, 0.92f, 0.15f), 0.14f, 0.06f, 0.22f), matGlow);

        float iconZ = 0.62f;
        Spawn(parent, "BoltBar", MeshPrimitives.RotatedBox(new Vector3(0, 0.65f, iconZ), 0.28f, 0.05f, 0.01f, 0f), matGlow);
        Spawn(parent, "BoltAngle", MeshPrimitives.RotatedBox(new Vector3(0, 0.56f, iconZ), 0.18f, 0.05f, 0.01f, -50f), matGlow);
    }

    static void Spawn(Transform parent, string name, Mesh mesh, Material mat)
        => MeshPrimitives.Spawn(parent, name, mesh, mat, addCollider: true);
}
