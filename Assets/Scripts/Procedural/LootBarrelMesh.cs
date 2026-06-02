using UnityEngine;

/// <summary>
/// Procedural mesh builder for a loot barrel.
/// Simple box crate with a glowing top panel.
/// </summary>
public static class LootBarrelMesh
{
    public static void Build(Transform parent, Material matHull, Material matAccent, Material matGlow)
    {
        // Simple crate: box body + glowing top
        MeshPrimitives.Spawn(parent, "Body",
            MeshPrimitives.Box(Vector3.zero, 0.8f, 0.7f, 0.8f), matHull);

        // Accent trim bands
        MeshPrimitives.Spawn(parent, "Trim",
            MeshPrimitives.Box(new Vector3(0, 0, 0), 0.84f, 0.08f, 0.84f), matAccent);

        // Glowing top panel
        MeshPrimitives.Spawn(parent, "TopGlow",
            MeshPrimitives.Box(new Vector3(0, 0.36f, 0), 0.6f, 0.03f, 0.6f), matGlow);
    }
}
