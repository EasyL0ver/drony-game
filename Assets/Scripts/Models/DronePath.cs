using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A hypothetical path for a drone: rooms to traverse + computed costs.
/// Used for previews (mouse-over) and for validating before committing to a journey.
/// Pure data, no side effects.
/// </summary>
public class DronePath
{
    public IReadOnlyList<Vector2Int> Rooms { get; }
    public int TotalEnergyCost { get; }
    public float TotalTime { get; }
    public WallInteractionConfig GoalInteraction { get; }

    DronePath(List<Vector2Int> rooms, int energyCost, float time, WallInteractionConfig goal)
    {
        Rooms = rooms;
        TotalEnergyCost = energyCost;
        TotalTime = time;
        GoalInteraction = goal;
    }

    /// <summary>
    /// Compute a path preview given rooms, fog context, and optional goal.
    /// </summary>
    public static DronePath Compute(
        Vector2Int start,
        List<Vector2Int> rooms,
        FogOfWar fog,
        DroneModel drone,
        WallInteractionConfig goalInteraction = null)
    {
        int energy = 0;
        float time = 0f;
        Vector2Int prev = start;

        foreach (var room in rooms)
        {
            var passage = fog?.GetTile(prev)?.GetPassage(room);
            var ptype = passage != null ? passage.Type : PassageType.Corridor;
            energy += MapModel.StepEnergyCost(ptype);
            time += MapModel.TravelTime(ptype);
            prev = room;
        }

        // Scan at final room
        if (rooms.Count > 0 && drone.CanScan)
        {
            var finalTile = fog?.GetTile(rooms[rooms.Count - 1]);
            if (finalTile != null && finalTile.State == FogState.Unknown)
            {
                energy += MapModel.ScanEnergyCost;
                time += finalTile.ScanTotalTime;
            }
        }

        if (goalInteraction != null)
        {
            energy += goalInteraction.EnergyCost;
            time += goalInteraction.BaseDuration;
        }

        return new DronePath(new List<Vector2Int>(rooms), energy, time, goalInteraction);
    }

    /// <summary>Can the drone afford this path given current energy?</summary>
    public bool CanAfford(int currentEnergy) => TotalEnergyCost <= currentEnergy;
}
