using UnityEngine;

/// <summary>
/// Procedural wall-mounted battery power source. Amber sci-fi capacitor bank
/// that protrudes from the hex wall into the room.
/// Shows energy remaining on mouseover.
/// Local +Z faces into the room, origin is at the wall surface.
/// </summary>
public class BatteryStation : WallView
{
    public override float ParkOffset => 1.8f;
    public override string HoverDescription
    {
        get
        {
            if (powerSource == null) return "Battery — Powers nearby stations through the cable network";
            return $"Battery — Powers nearby stations\n⚡ {powerSource.CurrentEnergy}/{powerSource.MaxEnergy} energy stored";
        }
    }

    IPowerSource powerSource;
    GameObject labelGO;
    TextMesh labelText;

    void OnEnable()
    {
        if (transform.childCount == 0)
            Build();
    }

    /// <summary>Link this view to its power source for displaying energy info.</summary>
    public void SetPowerSource(IPowerSource source)
    {
        powerSource = source;
    }

    public override void SetHoverGlow(bool hovered)
    {
        base.SetHoverGlow(hovered);
        if (hovered)
            ShowLabel();
        else
            HideLabel();
    }

    void ShowLabel()
    {
        if (powerSource == null) return;
        EnsureLabel();
        int cur = powerSource.CurrentEnergy;
        int max = powerSource.MaxEnergy;
        labelText.text = $"\u26A1 {cur}/{max}";
        labelGO.SetActive(true);
    }

    void HideLabel()
    {
        if (labelGO != null) labelGO.SetActive(false);
    }

    void EnsureLabel()
    {
        if (labelGO != null) return;
        labelGO = new GameObject("BatteryLabel");
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.localPosition = new Vector3(0f, 1.8f, 0.3f);

        labelText = labelGO.AddComponent<TextMesh>();
        labelText.fontSize = 32;
        labelText.characterSize = 0.08f;
        labelText.anchor = TextAnchor.MiddleCenter;
        labelText.alignment = TextAlignment.Center;
        labelText.color = Palette.CableGlow;
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (labelText.font != null)
            labelGO.GetComponent<MeshRenderer>().sharedMaterial = labelText.font.material;

        labelGO.AddComponent<BillboardLabel>();
        labelGO.SetActive(false);
    }

    [ContextMenu("Rebuild Station")]
    public void Build()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        InitMaterials(
            new Color(0.06f, 0.04f, 0.02f),
            new Color(0.10f, 0.07f, 0.03f),
            new Color(0.14f, 0.10f, 0.04f),
            Palette.CableGlow
        );

        BatteryStationMesh.Build(transform, matBase, matBody, matAccent, matGlow);
    }
}
