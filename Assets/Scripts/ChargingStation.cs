using UnityEngine;

/// <summary>
/// Procedural wall-mounted charging station. Amber/yellow sci-fi power conduit
/// that protrudes from the hex wall into the room.
/// Local +Z faces into the room, origin is at the wall surface.
/// </summary>
public class ChargingStation : WallEntity
{
    public override float ParkOffset => 1.2f;
    public override StationType StationType => StationType.Charging;

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
            new Color(0.10f, 0.08f, 0.05f),  // base
            new Color(0.12f, 0.10f, 0.06f),  // body (pylon)
            new Color(0.20f, 0.16f, 0.08f),  // accent
            new Color(1f, 0.75f, 0.1f)       // glow (amber)
        );

        // All geometry in local space: Z+ = into room, Z- = into wall, Y+ = up

        Spawn("Backplate", Box(new Vector3(0, 0.65f, -0.04f), 1.4f, 1.3f, 0.08f), matBase);
        Spawn("Housing", Box(new Vector3(0, 0.6f, 0.3f), 1.0f, 1.0f, 0.6f), matBody);
        Spawn("Canopy", Box(new Vector3(0, 1.15f, 0.25f), 1.2f, 0.08f, 0.5f), matAccent);
        Spawn("BaseLedge", Box(new Vector3(0, 0.06f, 0.2f), 1.2f, 0.06f, 0.4f), matBase);
        Spawn("FrontGlow", Box(new Vector3(0, 0.6f, 0.61f), 0.8f, 0.06f, 0.02f), matGlow);
        Spawn("GlowStripL", Box(new Vector3(-0.35f, 0.6f, 0.61f), 0.04f, 0.7f, 0.02f), matGlow);
        Spawn("GlowStripR", Box(new Vector3( 0.35f, 0.6f, 0.61f), 0.04f, 0.7f, 0.02f), matGlow);
        Spawn("ConduitL", Box(new Vector3(-0.6f, 0.5f, 0.15f), 0.12f, 0.8f, 0.2f), matAccent);
        Spawn("ConduitR", Box(new Vector3( 0.6f, 0.5f, 0.15f), 0.12f, 0.8f, 0.2f), matAccent);
        Spawn("CondCapL", Box(new Vector3(-0.6f, 0.92f, 0.15f), 0.14f, 0.06f, 0.22f), matGlow);
        Spawn("CondCapR", Box(new Vector3( 0.6f, 0.92f, 0.15f), 0.14f, 0.06f, 0.22f), matGlow);

        float iconZ = 0.62f;
        Spawn("BoltBar", RotatedBox(new Vector3(0, 0.65f, iconZ), 0.28f, 0.05f, 0.01f, 0f), matGlow);
        Spawn("BoltAngle", RotatedBox(new Vector3(0, 0.56f, iconZ), 0.18f, 0.05f, 0.01f, -50f), matGlow);
    }
}