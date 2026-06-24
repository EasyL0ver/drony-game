using UnityEngine;

/// <summary>
/// Shared interface for drone visual components (glow material access).
/// Implemented by LowPolyDrone and HaulerDrone.
/// </summary>
public interface IDroneVisual
{
    Material GlowMaterial { get; }
    Color BaseGlowColor { get; }
    float BaseGlowIntensity { get; }

    /// <summary>
    /// Briefly flash the drone glow to the given color, then fade back to base.
    /// </summary>
    void Flash(Color color, float duration = 0.3f);

    /// <summary>Show a carried-cargo visual on the drone (no-op for drones that
    /// can't carry cargo).</summary>
    void ShowCargo();

    /// <summary>Remove the carried-cargo visual, if any.</summary>
    void HideCargo();
}
