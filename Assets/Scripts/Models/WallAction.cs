/// <summary>
/// Handle returned by WallModel when a drone begins a traversal or interaction.
/// Tracks progress for that specific action. The controller ticks Elapsed each frame.
/// UI reads Progress from this handle. View animates based on it.
/// </summary>
public class WallAction
{
    /// <summary>Total duration of this action in seconds.</summary>
    public float Duration { get; private set; }

    /// <summary>Time elapsed so far. Ticked by the controller.</summary>
    public float Elapsed { get; set; }

    /// <summary>Progress 0-1.</summary>
    public float Progress => Duration > 0f ? UnityEngine.Mathf.Clamp01(Elapsed / Duration) : 1f;

    /// <summary>True when the action has reached its duration.</summary>
    public bool IsComplete => Elapsed >= Duration;

    /// <summary>True if this traversal is being reversed (drone going back).</summary>
    public bool IsReversing { get; private set; }

    /// <summary>Progress of the reverse (0 = just started reversing, 1 = back at start).</summary>
    public float ReverseProgress => reverseTotal > 0f ? UnityEngine.Mathf.Clamp01(reverseElapsed / reverseTotal) : 1f;

    /// <summary>True when reverse is complete.</summary>
    public bool IsReverseComplete => IsReversing && reverseElapsed >= reverseTotal;

    /// <summary>The wall this action is happening on.</summary>
    public WallModel Wall { get; private set; }

    /// <summary>The drone performing this action.</summary>
    public DroneModel Drone { get; private set; }

    /// <summary>Energy cost applied on completion.</summary>
    public int EnergyCost { get; private set; }

    /// <summary>Label for UI display.</summary>
    public string Label { get; private set; }

    /// <summary>
    /// Called when a cycle completes. Applies effects and returns true if
    /// the action should repeat. Null means no repeat.
    /// </summary>
    public System.Func<bool> ShouldRepeat { get; set; }

    float reverseElapsed;
    float reverseTotal;

    public WallAction(WallModel wall, DroneModel drone, float duration, int energyCost, string label)
    {
        Wall = wall;
        Drone = drone;
        Duration = duration;
        EnergyCost = energyCost;
        Label = label;
    }

    // ── Factories ───────────────────────────

    /// <summary>Create an action for traversing a wall.</summary>
    public static WallAction Traversal(WallModel wall, DroneModel drone)
    {
        var p = wall.GetPassability(drone);
        if (!p.CanPass) return null;
        return new WallAction(wall, drone, p.Duration, p.EnergyCost, p.Label);
    }

    /// <summary>Create an action for performing a wall interaction.</summary>
    public static WallAction Interaction(WallModel wall, DroneModel drone, WallInteractionConfig cfg)
    {
        var action = new WallAction(wall, drone, cfg.BaseDuration, cfg.EnergyCost, cfg.Label);

        if (cfg.RepeatCondition != null)
        {
            action.ShouldRepeat = () =>
            {
                if (cfg.EnergyGainPerCycle > 0)
                    drone.CurrentEnergy = UnityEngine.Mathf.Min(drone.MaxEnergy, drone.CurrentEnergy + cfg.EnergyGainPerCycle);
                return cfg.RepeatCondition(drone);
            };
        }

        return action;
    }

    /// <summary>
    /// Begin reversing this traversal. Takes the same time as already elapsed to go back.
    /// </summary>
    public void Reverse()
    {
        if (IsReversing) return;
        IsReversing = true;
        reverseTotal = Elapsed;
        reverseElapsed = 0f;
    }

    /// <summary>Tick the reverse timer. Called by controller each frame when reversing.</summary>
    public void TickReverse(float deltaTime)
    {
        if (!IsReversing) return;
        reverseElapsed += deltaTime;
    }

    /// <summary>Tick the forward timer. Called by controller each frame.</summary>
    public void Tick(float deltaTime)
    {
        if (IsReversing) return;
        Elapsed += deltaTime;
    }
}
