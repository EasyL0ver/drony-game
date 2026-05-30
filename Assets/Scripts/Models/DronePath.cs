using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A hypothetical path for a drone: walls to traverse.
/// Energy cost and time are computed from the walls for the specific drone.
/// </summary>
public class DronePath
{
    public IReadOnlyList<WallModel> Walls { get; }
    public DroneModel Drone { get; }

    public DronePath(List<WallModel> walls, DroneModel drone)
    {
        Walls = walls;
        Drone = drone;
    }

    public int TotalEnergyCost
    {
        get
        {
            int cost = 0;
            foreach (var wall in Walls)
                cost += wall.GetPassability(Drone).EnergyCost;
            return cost;
        }
    }

    public float TotalTime
    {
        get
        {
            float time = 0f;
            foreach (var wall in Walls)
                time += wall.GetPassability(Drone).Duration;
            return time;
        }
    }

    public bool CanAfford() => TotalEnergyCost <= Drone.CurrentEnergy;
}
