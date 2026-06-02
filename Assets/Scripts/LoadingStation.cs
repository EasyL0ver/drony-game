using UnityEngine;

/// <summary>
/// Loading station wall entity. Hauler drones unload cargo here for purchase points.
/// Simple platform with ramp and glowing cargo indicator.
/// </summary>
public class LoadingStation : WallView
{
    public override float ParkOffset => 1.2f;

    void OnEnable()
    {
        if (transform.childCount == 0)
            Build();
    }

    [ContextMenu("Rebuild Station")]
    public void Build()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        InitMaterials(
            new Color(0.12f, 0.10f, 0.08f),   // base: industrial dark
            new Color(0.15f, 0.12f, 0.08f),   // body
            new Color(0.20f, 0.18f, 0.10f),   // accent
            new Color(1f, 0.5f, 0.0f)         // glow: orange (matches barrel theme)
        );

        LoadingStationMesh.Build(transform, matBase, matBody, matAccent, matGlow);
    }
}
