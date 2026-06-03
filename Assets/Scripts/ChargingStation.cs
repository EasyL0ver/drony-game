using UnityEngine;

/// <summary>
/// Procedural wall-mounted charging station. Amber/yellow sci-fi power conduit
/// that protrudes from the hex wall into the room.
/// Local +Z faces into the room, origin is at the wall surface.
/// </summary>
public class ChargingStation : WallView
{
    public override float ParkOffset => 1.8f;
    public override string HoverDescription => "Charging Station — Recharges drone energy\n⚡ +5 energy/cycle   🔌 3 power/cycle";

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
            new Color(0.10f, 0.08f, 0.05f),
            new Color(0.12f, 0.10f, 0.06f),
            new Color(0.20f, 0.16f, 0.08f),
            Palette.ChargingGlow
        );

        ChargingStationMesh.Build(transform, matBase, matBody, matAccent, matGlow);
    }
}