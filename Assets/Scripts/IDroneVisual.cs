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
}
