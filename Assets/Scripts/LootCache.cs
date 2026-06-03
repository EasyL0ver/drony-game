using UnityEngine;

/// <summary>
/// Loot cache wall entity. Wall-mounted storage locker that starts locked.
/// Lockpick gear opens it, then hauler can pick up contents.
/// Scanner can identify contents without opening.
/// </summary>
public class LootCache : WallView
{
    public override float ParkOffset => 1.5f;

    public override string HoverDescription
    {
        get
        {
            var cacheModel = Model as LootCacheWallModel;
            bool open = cacheModel != null && cacheModel.IsOpen;

            if (open && content != null)
                return $"Loot Cache (Open) — Contains: {content.Name} ({content.Size})\nRequires: Hauler drone to pick up";
            return "Loot Cache (Locked) — Unknown contents\nRequires: Lockpick to open";
        }
    }

    GearItem content;
    Material glowMat;
    Material doorMat;

    public void SetContent(GearItem item)
    {
        content = item;
    }

    public void OnOpened()
    {
        var cacheModel = Model as LootCacheWallModel;
        if (cacheModel != null)
        {
            cacheModel.IsOpen = true;
            cacheModel.IsScanned = true;
        }
        Rebuild();
    }

    void OnEnable()
    {
        if (transform.childCount == 0)
            Build(false);
    }

    void Rebuild()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);
        var cacheModel = Model as LootCacheWallModel;
        bool open = cacheModel != null && cacheModel.IsOpen;
        Build(open);
    }

    void Build(bool isOpen)
    {
        Color baseGlow = isOpen && content != null ? ContentGlowColor(content) : new Color(0.5f, 0.5f, 0.5f);

        InitMaterials(
            new Color(0.15f, 0.15f, 0.18f),
            new Color(0.20f, 0.20f, 0.22f),
            new Color(0.20f, 0.20f, 0.22f),
            baseGlow
        );

        glowMat = matGlow;
        doorMat = matBody;

        LootCacheMesh.Build(transform, matBase, matBody, matGlow, isOpen);
    }

    static Color ContentGlowColor(GearItem item)
    {
        if (item == null) return Color.grey;
        if (item.SellPrice >= 5) return new Color(1f, 0.2f, 0.8f);    // purple — rare
        if (item.SellPrice >= 3) return new Color(1f, 0.6f, 0.05f);   // orange — uncommon
        return new Color(0.3f, 0.8f, 1f);                              // cyan — common
    }
}
