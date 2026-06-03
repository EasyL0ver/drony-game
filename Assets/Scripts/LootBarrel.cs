using UnityEngine;

/// <summary>
/// Loot barrel wall entity. Contains a fuel cell that only a hauler drone can pick up.
/// Procedurally built sci-fi barrel placed at a hex wall edge.
/// </summary>
public class LootBarrel : WallView
{
    public override float ParkOffset => 1.5f;

    void OnEnable()
    {
        if (transform.childCount == 0)
            Build();
    }

    [ContextMenu("Rebuild Barrel")]
    public void Build()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        InitMaterials(
            new Color(0.25f, 0.22f, 0.15f),   // base: brownish metal
            new Color(0.20f, 0.18f, 0.12f),   // body
            new Color(0.30f, 0.25f, 0.12f),   // accent: gold-ish trim
            new Color(1f, 0.6f, 0.05f)        // glow: warm orange/amber
        );

        // Offset container so barrel sits in front of wall (local +Z = into room)
        var container = new GameObject("BarrelMesh");
        container.transform.SetParent(transform, false);
        container.transform.localPosition = new Vector3(0, 0, 0.45f);
        LootBarrelMesh.Build(container.transform, matBase, matAccent, matGlow);
    }
}
