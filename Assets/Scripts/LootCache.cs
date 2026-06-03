using UnityEngine;

/// <summary>
/// Loot cache wall entity. Starts as unknown (grey), can be scanned to reveal content.
/// Hauler can pick up content regardless of scan state.
/// Visual changes when scanned to show content value color.
/// </summary>
public class LootCache : WallView
{
    public override float ParkOffset => 1.5f;

    GearItem content;
    Material glowMat;
    GameObject meshContainer;

    public void SetContent(GearItem item)
    {
        content = item;
    }

    public void OnScanned()
    {
        var cacheModel = Model as LootCacheWallModel;
        if (cacheModel != null) cacheModel.IsScanned = true;

        // Update glow color to reflect content value
        if (glowMat != null && content != null)
        {
            Color col = ContentGlowColor(content);
            glowMat.color = col;
            glowMat.SetColor("_EmissionColor", col * 3f);
        }
    }

    void OnEnable()
    {
        if (transform.childCount == 0)
            Build();
    }

    [ContextMenu("Rebuild Cache")]
    public void Build()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        // Unscanned: neutral grey glow
        Color baseGlow = new Color(0.5f, 0.5f, 0.5f);

        InitMaterials(
            new Color(0.15f, 0.15f, 0.18f),   // base: dark metal
            new Color(0.12f, 0.12f, 0.15f),   // body
            new Color(0.20f, 0.20f, 0.22f),   // accent
            baseGlow                            // glow: grey (unknown)
        );

        glowMat = matGlow;

        meshContainer = new GameObject("CacheMesh");
        meshContainer.transform.SetParent(transform, false);
        meshContainer.transform.localPosition = new Vector3(0, 0, 0.45f);
        LootBarrelMesh.Build(meshContainer.transform, matBase, matAccent, matGlow);
    }

    static Color ContentGlowColor(GearItem item)
    {
        if (item == null) return Color.grey;
        if (item.SellPrice >= 5) return new Color(1f, 0.2f, 0.8f);    // purple — rare
        if (item.SellPrice >= 3) return new Color(1f, 0.6f, 0.05f);   // orange — uncommon
        return new Color(0.3f, 0.8f, 1f);                              // cyan — common
    }
}
