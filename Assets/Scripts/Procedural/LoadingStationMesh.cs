using UnityEngine;

/// <summary>
/// Procedural mesh for loading station — raised platform with ramp and glowing indicators.
/// </summary>
public static class LoadingStationMesh
{
    public static void Build(Transform parent, Material matBase, Material matBody, Material matAccent, Material matGlow)
    {
        // Main platform (wide, low)
        MeshPrimitives.Spawn(parent, "Platform",
            MeshPrimitives.Box(new Vector3(0, 0.1f, 0.6f), 1.6f, 0.2f, 1.2f), matBase);

        // Ramp (angled slab leading up to platform)
        var rampGO = MeshPrimitives.Spawn(parent, "Ramp",
            MeshPrimitives.Box(Vector3.zero, 1.0f, 0.06f, 0.8f), matBody);
        rampGO.transform.localPosition = new Vector3(0, 0.02f, 1.3f);
        rampGO.transform.localRotation = Quaternion.Euler(8f, 0, 0);

        // Side rails
        MeshPrimitives.Spawn(parent, "RailL",
            MeshPrimitives.Box(new Vector3(-0.75f, 0.35f, 0.6f), 0.06f, 0.5f, 1.2f), matAccent);
        MeshPrimitives.Spawn(parent, "RailR",
            MeshPrimitives.Box(new Vector3(0.75f, 0.35f, 0.6f), 0.06f, 0.5f, 1.2f), matAccent);

        // Back wall
        MeshPrimitives.Spawn(parent, "BackWall",
            MeshPrimitives.Box(new Vector3(0, 0.4f, 0.0f), 1.6f, 0.8f, 0.08f), matBody);

        // Glow strips on platform edges
        MeshPrimitives.Spawn(parent, "GlowL",
            MeshPrimitives.Box(new Vector3(-0.7f, 0.21f, 0.6f), 0.08f, 0.04f, 1.0f), matGlow);
        MeshPrimitives.Spawn(parent, "GlowR",
            MeshPrimitives.Box(new Vector3(0.7f, 0.21f, 0.6f), 0.08f, 0.04f, 1.0f), matGlow);

        // Top indicator panel
        MeshPrimitives.Spawn(parent, "Indicator",
            MeshPrimitives.Box(new Vector3(0, 0.75f, 0.02f), 0.6f, 0.2f, 0.04f), matGlow);
    }
}
