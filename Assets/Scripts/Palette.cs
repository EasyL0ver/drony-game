using UnityEngine;

/// <summary>
/// Central color definitions for the entire game.
/// Every visual element reads from here — never hardcode colors elsewhere.
/// </summary>
public static class Palette
{
    // ── drone states ──
    public static readonly Color DroneIdle      = Hex("B0B8C0");
    public static readonly Color DroneSelected   = Color.white;
    public static readonly Color DroneMoving     = Hex("3399FF");
    public static readonly Color DroneDepleted   = Hex("CC3333");

    // ── passage types ──
    public static readonly Color CorridorGlow    = Hex("00D4AA");
    public static readonly Color DuctGlow        = Hex("FF8C00");
    public static readonly Color VentGlow        = Hex("33FF4D");
    public static readonly Color RubbleGlow      = Hex("8B5E3C");
    public static readonly Color ImpassableGlow  = Hex("CC2222");

    // ── stations ──
    public static readonly Color ChargingGlow    = Hex("FFB800");
    public static readonly Color RefittingGlow   = Hex("00D0FF");

    // ── route lines ──
    public static readonly Color JourneyLine     = Hex("3399FF");
    public static readonly Color PreviewLine     = Hex("6688AA");
    public static readonly Color OverBudgetLine  = Hex("FF2222");

    // ── overlay bars ──
    public static readonly Color ActiveBarFill   = Hex("3399FF");
    public static readonly Color ActiveBarBg     = new Color(0.02f, 0.04f, 0.08f, 0.88f);
    public static readonly Color ActiveBarInfo   = Hex("3399FF");
    public static readonly Color PreviewBarFill  = Hex("6688AA");
    public static readonly Color PreviewBarBg    = new Color(0.08f, 0.06f, 0.04f, 0.75f);
    public static readonly Color PreviewBarInfo  = Hex("8899AA");
    public static readonly Color OverBudgetFill  = Hex("FF2222");
    public static readonly Color OverBudgetBg    = new Color(0.12f, 0.02f, 0.02f, 0.80f);
    public static readonly Color OverBudgetInfo  = Hex("FF4433");
    public static readonly Color InactiveBarFill = new Color(0.25f, 0.35f, 0.45f, 0.55f);
    public static readonly Color InactiveBarBg   = new Color(0.02f, 0.04f, 0.08f, 0.55f);
    public static readonly Color InactiveBarInfo = new Color(0.5f, 0.6f, 0.7f, 0.6f);

    // ── overlay labels ──
    public static readonly Color ActiveLabel     = Hex("3399FF");
    public static readonly Color PreviewLabel    = Hex("8899AA");
    public static readonly Color OverBudgetLabel = Hex("FF4433");

    // ── selection ──
    public static readonly Color SelectionRing   = Color.white;
    public static readonly Color SelectionBoxFill   = new Color(1f, 1f, 1f, 0.10f);
    public static readonly Color SelectionBoxBorder = new Color(1f, 1f, 1f, 0.60f);

    // ── fog of war ──
    public static readonly Color FogUnknown      = new Color(0.01f, 0.01f, 0.02f, 1f);
    public static readonly Color FogDiscovered   = new Color(0.02f, 0.02f, 0.04f, 0.50f);
    public static readonly Color FogOutline      = Hex("445566");

    // ── environment ──
    public static readonly Color FloorColor      = new Color(0.05f, 0.05f, 0.07f, 1f);
    public static readonly Color WallColor       = new Color(0.10f, 0.10f, 0.14f, 1f);
    public static readonly Color CameraBg        = new Color(0.01f, 0.01f, 0.02f, 1f);

    // ── helpers ──
    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }
}
