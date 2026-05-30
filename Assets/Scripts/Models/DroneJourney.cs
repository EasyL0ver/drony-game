using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks an active drone journey: wraps a DronePath with execution state.
/// </summary>
public class DroneJourney
{
    public DronePath Path { get; }
    public int CurrentHopIndex { get; private set; }

    public DroneJourney(DronePath path)
    {
        Path = path;
    }

    public IReadOnlyList<WallModel> Walls => Path.Walls;
    public DroneModel Drone => Path.Drone;

    public void AdvanceHop() => CurrentHopIndex++;

    public int RemainingEnergyCost
    {
        get
        {
            int cost = 0;
            for (int i = CurrentHopIndex; i < Walls.Count; i++)
                cost += Walls[i].GetPassability(Drone).EnergyCost;
            return cost;
        }
    }
}
