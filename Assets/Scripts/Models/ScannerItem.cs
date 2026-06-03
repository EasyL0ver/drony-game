using UnityEngine;
using System.Linq;

/// <summary>
/// Gear that automatically activates when the drone is idle at room center
/// and conditions are met (e.g. Scanner activates in Unknown rooms).
/// </summary>
public interface IAutoActivateGear
{
    /// <summary>Should this item activate in the given room?</summary>
    bool IsEligible(DroneModel owner, RoomModel room);

    /// <summary>Energy cost when activating.</summary>
    int ActivationEnergyCost(DroneModel owner, RoomModel room);

    /// <summary>Duration of the activation (seconds).</summary>
    float GetDuration(DroneModel owner, RoomModel room);

    /// <summary>Label shown in journey UI.</summary>
    string StepLabel { get; }

    /// <summary>Called when activation starts.</summary>
    void OnActivationStart(DroneModel owner, RoomModel room);

    /// <summary>Called when activation completes.</summary>
    void OnActivationComplete(DroneModel owner, RoomModel room);
}

public class ScannerItem : GearItem, IAutoActivateGear
{
    readonly int energyCost;

    public ScannerItem(int energyCost = 2)
        : base(GearType.Scanner, "Scanner",
               "Allows the drone to scan and reveal unknown rooms.",
               cost: 2, icon: "\u25CE", size: SlotSize.Small, sellPrice: 1)
    {
        this.energyCost = energyCost;
    }

    public string StepLabel => "SCAN";

    public bool IsEligible(DroneModel owner, RoomModel room) => room.State == FogState.Unknown;

    public int ActivationEnergyCost(DroneModel owner, RoomModel room) => energyCost;

    public float GetDuration(DroneModel owner, RoomModel room) => 3f;

    public void OnActivationStart(DroneModel owner, RoomModel room)
    {
        room.BeginScan();
    }

    public void OnActivationComplete(DroneModel owner, RoomModel room)
    {
        room.CompleteScan();
    }
}

public class EnergyLinkItem : GearItem, IAutoActivateGear
{
    const float SecondsPerEnergy = 0.4f;

    public EnergyLinkItem()
        : base(GearType.EnergyLink, "Energy Link",
               "Transfers energy to nearby drones when idle.",
               cost: 4, icon: "\u26A1", size: SlotSize.Medium, sellPrice: 2)
    {
    }

    public string StepLabel => "LINK";

    public bool IsEligible(DroneModel owner, RoomModel room)
    {
        if (owner.CurrentEnergy <= 0) return false;
        return room.Drones.Any(d => d != owner && d.CurrentEnergy < d.MaxEnergy);
    }

    public int ActivationEnergyCost(DroneModel owner, RoomModel room)
    {
        return EnergyToTransfer(owner, room);
    }

    public float GetDuration(DroneModel owner, RoomModel room)
    {
        return EnergyToTransfer(owner, room) * SecondsPerEnergy;
    }

    public void OnActivationStart(DroneModel owner, RoomModel room) { }

    public void OnActivationComplete(DroneModel owner, RoomModel room)
    {
        // Distribute energy to other drones. Controller deducts from owner afterward.
        int budget = owner.CurrentEnergy;
        foreach (var drone in room.Drones)
        {
            if (drone == owner || budget <= 0) continue;
            int need = drone.MaxEnergy - drone.CurrentEnergy;
            if (need <= 0) continue;
            int give = Mathf.Min(need, budget);
            drone.CurrentEnergy += give;
            budget -= give;
        }
    }

    int EnergyToTransfer(DroneModel owner, RoomModel room)
    {
        int budget = owner.CurrentEnergy;
        int total = 0;
        foreach (var drone in room.Drones)
        {
            if (drone == owner || budget <= 0) continue;
            int need = drone.MaxEnergy - drone.CurrentEnergy;
            if (need <= 0) continue;
            int give = Mathf.Min(need, budget);
            total += give;
            budget -= give;
        }
        return total;
    }
}
