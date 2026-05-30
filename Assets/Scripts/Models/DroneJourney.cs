using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks an active drone journey: the sequence of rooms to traverse
/// and an optional goal interaction at the end.
/// </summary>
public class DroneJourney
{
    public IReadOnlyList<Vector2Int> Rooms { get; }
    public WallInteractionConfig GoalInteraction { get; }
    public bool IsBlockingWallInteraction { get; }
    public Vector2Int WallConnA { get; }
    public Vector2Int WallConnB { get; }

    public int CurrentHopIndex { get; private set; }
    public bool GoalDone { get; private set; }

    /// <summary>Travel path with optional non-blocking interaction at end.</summary>
    public DroneJourney(List<Vector2Int> rooms, WallInteractionConfig goalInteraction = null)
    {
        Rooms = rooms;
        GoalInteraction = goalInteraction;
        IsBlockingWallInteraction = false;
    }

    /// <summary>Travel path with blocking wall interaction at a specific connection.</summary>
    public DroneJourney(List<Vector2Int> rooms, WallInteractionConfig interaction, Vector2Int connA, Vector2Int connB)
    {
        Rooms = rooms;
        GoalInteraction = interaction;
        IsBlockingWallInteraction = true;
        WallConnA = connA;
        WallConnB = connB;
    }

    public void AdvanceHop() => CurrentHopIndex++;
    public void MarkGoalDone() => GoalDone = true;

    public int RemainingEnergyCost
    {
        get
        {
            int remaining = Rooms.Count - CurrentHopIndex;
            int cost = remaining; // 1 energy per hop (simplified)
            if (GoalInteraction != null && !GoalDone)
                cost += GoalInteraction.EnergyCost;
            return cost;
        }
    }
}
