using UnityEngine;

/// <summary>
/// Gear that automatically activates when the drone is idle at room center
/// and conditions are met (e.g. Scanner activates in Unknown rooms).
/// </summary>
public interface IAutoActivateGear
{
    /// <summary>Should this item activate in the given room?</summary>
    bool IsEligible(RoomModel room);

    /// <summary>Energy cost when activating.</summary>
    int ActivationEnergyCost { get; }

    /// <summary>Duration of the activation (seconds).</summary>
    float GetDuration(RoomModel room);

    /// <summary>Label shown in journey UI.</summary>
    string StepLabel { get; }

    /// <summary>Called when activation completes.</summary>
    void OnActivationComplete(RoomModel room);
}

public class ScannerItem : GearItem, IAutoActivateGear
{
    public ScannerItem(int energyCost = 2)
        : base(GearType.Scanner, "Scanner",
               "Allows the drone to scan and reveal unknown rooms.",
               cost: 2, icon: "\u25CE", size: SlotSize.Small, sellPrice: 1)
    {
        ActivationEnergyCost = energyCost;
    }

    public int ActivationEnergyCost { get; }
    public string StepLabel => "SCAN";

    public bool IsEligible(RoomModel room) => room.State == FogState.Unknown;

    public float GetDuration(RoomModel room) => room.ScanDuration;

    public void OnActivationComplete(RoomModel room)
    {
        // Room reveal is handled by RoomModel.OnScanComplete event
    }
}
