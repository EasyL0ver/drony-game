using UnityEngine;

/// <summary>
/// Generates the procedural mesh parts for a refitting station.
/// All geometry in local space: Z+ = into room, Z- = into wall, Y+ = up.
/// </summary>
public static class RefittingStationMesh
{
    public static void Build(Transform parent, Material matBase, Material matBody, Material matAccent, Material matGlow)
    {
        Spawn(parent, "Backplate", MeshPrimitives.Box(new Vector3(0, 0.7f, -0.04f), 1.6f, 1.4f, 0.08f), matBase);
        Spawn(parent, "Housing", MeshPrimitives.Box(new Vector3(0, 0.55f, 0.25f), 1.2f, 0.9f, 0.5f), matBody);
        Spawn(parent, "WorkSurface", MeshPrimitives.Box(new Vector3(0, 0.08f, 0.45f), 1.3f, 0.06f, 0.8f), matBase);
        Spawn(parent, "Canopy", MeshPrimitives.Box(new Vector3(0, 1.2f, 0.2f), 1.4f, 0.06f, 0.4f), matAccent);
        Spawn(parent, "FrameL", MeshPrimitives.Box(new Vector3(-0.65f, 0.7f, 0.1f), 0.08f, 1.1f, 0.2f), matAccent);
        Spawn(parent, "FrameR", MeshPrimitives.Box(new Vector3( 0.65f, 0.7f, 0.1f), 0.08f, 1.1f, 0.2f), matAccent);
        Spawn(parent, "TopGlow", MeshPrimitives.Box(new Vector3(0, 1.24f, 0.2f), 1.1f, 0.03f, 0.3f), matGlow);

        for (int i = -1; i <= 1; i++)
        {
            float x = i * 0.3f;
            Spawn(parent, $"Slot{i + 1}", MeshPrimitives.Box(new Vector3(x, 0.7f, 0.51f), 0.18f, 0.5f, 0.02f), matGlow);
        }

        Spawn(parent, "Arm", MeshPrimitives.Box(new Vector3(0.3f, 0.35f, 0.6f), 0.08f, 0.06f, 0.3f), matAccent);
        Spawn(parent, "ArmTip", MeshPrimitives.Box(new Vector3(0.3f, 0.35f, 0.76f), 0.1f, 0.08f, 0.04f), matGlow);

        float iconZ = 0.52f;
        Spawn(parent, "WrenchBar1", MeshPrimitives.RotatedBox(new Vector3(0, 1.0f, iconZ), 0.24f, 0.04f, 0.01f, 45f), matGlow);
        Spawn(parent, "WrenchBar2", MeshPrimitives.RotatedBox(new Vector3(0, 1.0f, iconZ), 0.24f, 0.04f, 0.01f, -45f), matGlow);
    }

    static void Spawn(Transform parent, string name, Mesh mesh, Material mat)
        => MeshPrimitives.Spawn(parent, name, mesh, mat, addCollider: true);
}
