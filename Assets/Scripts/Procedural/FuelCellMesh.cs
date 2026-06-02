using UnityEngine;

/// <summary>
/// Procedural mesh for a sci-fi fuel cell (glowing capsule shape).
/// Shown on top of hauler drone when carrying cargo.
/// </summary>
public static class FuelCellMesh
{
    public static void Build(Transform parent)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        var matBody = new Material(sh) { color = new Color(0.15f, 0.2f, 0.25f) };
        matBody.SetFloat("_Smoothness", 0.5f);

        Color glowCol = new Color(0.2f, 0.8f, 1f);
        var matGlow = new Material(sh) { color = glowCol };
        matGlow.EnableKeyword("_EMISSION");
        matGlow.SetColor("_EmissionColor", glowCol * 4f);
        matGlow.SetFloat("_Smoothness", 0.9f);

        float r = 0.04f;
        float halfH = 0.06f;

        // Capsule body (hexagonal prism)
        MeshPrimitives.Spawn(parent, "CellBody",
            MeshPrimitives.Prism(r, halfH, 8), matBody);

        // Glowing core (inner cylinder, slightly smaller)
        MeshPrimitives.Spawn(parent, "CellCore",
            MeshPrimitives.Prism(r * 0.6f, halfH * 0.8f, 8), matGlow);

        // End caps glow
        MeshPrimitives.Spawn(parent, "CapTop",
            MeshPrimitives.Prism(r * 0.5f, 0.008f, 6, halfH), matGlow);
        MeshPrimitives.Spawn(parent, "CapBot",
            MeshPrimitives.Prism(r * 0.5f, 0.008f, 6, -halfH), matGlow);
    }
}
