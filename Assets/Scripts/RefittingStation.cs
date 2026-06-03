using UnityEngine;

/// <summary>
/// Procedural wall-mounted refitting station. Teal sci-fi workbench / tool rack
/// that protrudes from the hex wall into the room.
/// Local +Z faces into the room, origin is at the wall surface.
/// </summary>
public class RefittingStation : WallView
{
    public override float ParkOffset => 1.8f;

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
            new Color(0.08f, 0.08f, 0.10f),
            new Color(0.10f, 0.12f, 0.14f),
            new Color(0.15f, 0.18f, 0.20f),
            Palette.RefittingGlow
        );

        RefittingStationMesh.Build(transform, matBase, matBody, matAccent, matGlow);
    }
}