using UnityEngine;

/// <summary>
/// Procedural wall-mounted refitting station. Teal sci-fi workbench / tool rack
/// that protrudes from the hex wall into the room.
/// Local +Z faces into the room, origin is at the wall surface.
/// </summary>
public class RefittingStation : WallEntity
{
    public override float ParkOffset => 1.2f;
    public override StationType StationType => StationType.Refitting;

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

        // All geometry in local space: Z+ = into room, Z- = into wall, Y+ = up

        Spawn("Backplate", Box(new Vector3(0, 0.7f, -0.04f), 1.6f, 1.4f, 0.08f), matBase);
        Spawn("Housing", Box(new Vector3(0, 0.55f, 0.25f), 1.2f, 0.9f, 0.5f), matBody);
        Spawn("WorkSurface", Box(new Vector3(0, 0.08f, 0.45f), 1.3f, 0.06f, 0.8f), matBase);
        Spawn("Canopy", Box(new Vector3(0, 1.2f, 0.2f), 1.4f, 0.06f, 0.4f), matAccent);
        Spawn("FrameL", Box(new Vector3(-0.65f, 0.7f, 0.1f), 0.08f, 1.1f, 0.2f), matAccent);
        Spawn("FrameR", Box(new Vector3( 0.65f, 0.7f, 0.1f), 0.08f, 1.1f, 0.2f), matAccent);
        Spawn("TopGlow", Box(new Vector3(0, 1.24f, 0.2f), 1.1f, 0.03f, 0.3f), matGlow);

        for (int i = -1; i <= 1; i++)
        {
            float x = i * 0.3f;
            Spawn($"Slot{i + 1}", Box(new Vector3(x, 0.7f, 0.51f), 0.18f, 0.5f, 0.02f), matGlow);
        }

        Spawn("Arm", Box(new Vector3(0.3f, 0.35f, 0.6f), 0.08f, 0.06f, 0.3f), matAccent);
        Spawn("ArmTip", Box(new Vector3(0.3f, 0.35f, 0.76f), 0.1f, 0.08f, 0.04f), matGlow);

        float iconZ = 0.52f;
        Spawn("WrenchBar1", RotatedBox(new Vector3(0, 1.0f, iconZ), 0.24f, 0.04f, 0.01f, 45f), matGlow);
        Spawn("WrenchBar2", RotatedBox(new Vector3(0, 1.0f, iconZ), 0.24f, 0.04f, 0.01f, -45f), matGlow);
    }
}