using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pure game-logic model for a drone.
/// Owns energy, journey plan, path queue, movement state.
/// No MonoBehaviour, no visuals, no GameObjects.
/// </summary>
public class DroneModel
{
    // ── Identity ─────────────────────────────

    public string Name { get; set; } = "Drone";

    // ── Energy ───────────────────────────────

    public int MaxEnergy { get; set; } = 10;
    public int CurrentEnergy { get; set; } = 10;
    public float EnergyFraction => MaxEnergy > 0 ? (float)CurrentEnergy / MaxEnergy : 0f;

    // ── Position / movement ──────────────────

    public Vector2Int CurrentRoom { get; set; }
    public Vector2Int FromRoom { get; set; }
    public Vector2Int ToRoom { get; set; }
    public float TravelProgress { get; set; } = 1f;  // 1 = arrived
    public float TravelDuration { get; set; }
    public float SpeedJitter { get; set; } = 1f;

    /// <summary>True when actively moving between rooms or centering.</summary>
    public bool IsMoving => Path.Count > 0 || TravelProgress < 1f || IsCentering;

    // Path queue — rooms remaining to visit
    public Queue<Vector2Int> Path { get; private set; } = new Queue<Vector2Int>();

    // Centering phase (glide to room center before scanning)
    public bool IsCentering { get; set; }

    // ── Selection ────────────────────────────

    public bool IsSelected { get; set; }

    // ── Journey plan ─────────────────────────

    public struct JourneyStep
    {
        public string label;
        public float duration;
        public bool isScan;
        public int energyCost;
    }

    public List<JourneyStep> JourneyPlan { get; private set; } = new List<JourneyStep>();
    public int JourneyIdx { get; set; } = -1;

    public List<JourneyStep> PreviewPlan { get; private set; } = new List<JourneyStep>();
    public bool IsShowingPreview { get; set; }

    // ── Computed journey properties ──────────

    /// <summary>Total energy cost of remaining journey steps.</summary>
    public int JourneyEnergyCost
    {
        get
        {
            int total = 0;
            for (int i = Mathf.Max(0, JourneyIdx); i < JourneyPlan.Count; i++)
                total += JourneyPlan[i].energyCost;
            return total;
        }
    }

    /// <summary>Total energy cost of the preview journey.</summary>
    public int PreviewEnergyCost
    {
        get
        {
            int total = 0;
            foreach (var s in PreviewPlan) total += s.energyCost;
            return total;
        }
    }

    /// <summary>True when the preview path costs more energy than available.</summary>
    public bool PreviewExceedsEnergy =>
        IsShowingPreview && PreviewEnergyCost > (CurrentEnergy - JourneyEnergyCost);

    /// <summary>Total duration of all journey steps.</summary>
    public float JourneyTotalTime
    {
        get { float t = 0; foreach (var s in JourneyPlan) t += s.duration; return t; }
    }

    /// <summary>Elapsed time across all journey steps.</summary>
    public float JourneyElapsedTime
    {
        get
        {
            float t = 0;
            for (int i = 0; i < JourneyPlan.Count; i++)
                t += GetStepElapsed(i);
            return t;
        }
    }

    /// <summary>Overall journey progress 0..1.</summary>
    public float JourneyOverallProgress
    {
        get
        {
            float total = JourneyTotalTime;
            return total > 0 ? Mathf.Clamp01(JourneyElapsedTime / total) : 0f;
        }
    }

    /// <summary>Total duration of the preview journey.</summary>
    public float PreviewTotalTime
    {
        get { float t = 0; foreach (var s in PreviewPlan) t += s.duration; return t; }
    }

    // ── Step progress helpers ────────────────

    /// <summary>Per-step scan progress callback. Set by the controller to query room scan state.</summary>
    public System.Func<float> GetScanProgress { get; set; }

    public float GetStepProgress(int i)
    {
        if (JourneyIdx < 0 || i < 0 || i >= JourneyPlan.Count) return 0f;
        if (i < JourneyIdx) return 1f;
        if (i > JourneyIdx) return 0f;
        // Active step
        if (JourneyPlan[i].isScan)
            return GetScanProgress?.Invoke() ?? 0f;
        return TravelProgress;
    }

    public float GetStepElapsed(int i)
    {
        if (i < 0 || i >= JourneyPlan.Count) return 0f;
        return GetStepProgress(i) * JourneyPlan[i].duration;
    }

    // ── Journey building ─────────────────────

    /// <summary>
    /// Build journey plan from a path. Returns false if energy insufficient.
    /// </summary>
    /// <param name="newPath">List of rooms to visit (excluding current).</param>
    /// <param name="map">Map model for passage types and travel times.</param>
    /// <param name="getRoomState">Returns FogState for a room coord.</param>
    /// <param name="scanDurationForRoom">Returns scan duration for a room coord, or 0 if not needed.</param>
    public bool TryBuildJourney(List<Vector2Int> newPath, MapModel map,
                                System.Func<Vector2Int, FogState> getRoomState,
                                System.Func<Vector2Int, float> scanDurationForRoom)
    {
        // Calculate total energy cost before committing
        int cost = 0;
        Vector2Int prev = CurrentRoom;
        foreach (var room in newPath)
        {
            cost += MapModel.StepEnergyCost(map.GetPassageType(prev, room));
            prev = room;
        }

        var destState = getRoomState(newPath[newPath.Count - 1]);
        float scanDur = 0f;
        if (destState == FogState.Unknown)
        {
            cost += MapModel.ScanEnergyCost;
            scanDur = scanDurationForRoom(newPath[newPath.Count - 1]);
        }

        int available = CurrentEnergy - JourneyEnergyCost;
        if (cost > available) return false;

        // Commit: clear old state
        Path.Clear();
        IsCentering = false;

        if (TravelProgress < 1f)
        {
            CurrentRoom = TravelProgress < 0.5f ? FromRoom : ToRoom;
            TravelProgress = 1f;
        }
        FromRoom = CurrentRoom;
        ToRoom = CurrentRoom;

        foreach (var room in newPath)
            Path.Enqueue(room);

        // Build journey plan
        JourneyPlan.Clear();
        JourneyIdx = -1;

        if (newPath.Count > 0)
        {
            JourneyIdx = 0;
            prev = CurrentRoom;
            foreach (var room in newPath)
            {
                var ptype = map.GetPassageType(prev, room);
                float dur = MapModel.TravelTime(ptype) * SpeedJitter;
                JourneyPlan.Add(new JourneyStep
                {
                    label = PassageLabel(ptype),
                    duration = dur,
                    isScan = false,
                    energyCost = MapModel.StepEnergyCost(ptype),
                });
                prev = room;
            }

            if (destState == FogState.Unknown)
            {
                JourneyPlan.Add(new JourneyStep
                {
                    label = "SCAN",
                    duration = scanDur,
                    isScan = true,
                    energyCost = MapModel.ScanEnergyCost,
                });
            }
        }

        return true;
    }

    /// <summary>
    /// Build preview plan from a path (doesn't commit movement).
    /// </summary>
    public void BuildPreviewPlan(List<Vector2Int> previewPath, MapModel map,
                                 System.Func<Vector2Int, FogState> getRoomState,
                                 System.Func<Vector2Int, float> scanDurationForRoom)
    {
        PreviewPlan.Clear();
        if (previewPath == null || previewPath.Count == 0) return;

        Vector2Int prev = CurrentRoom;
        foreach (var room in previewPath)
        {
            var ptype = map.GetPassageType(prev, room);
            float dur = MapModel.TravelTime(ptype) * SpeedJitter;
            PreviewPlan.Add(new JourneyStep
            {
                label = PassageLabel(ptype),
                duration = dur,
                isScan = false,
                energyCost = MapModel.StepEnergyCost(ptype),
            });
            prev = room;
        }

        var destState = getRoomState(previewPath[previewPath.Count - 1]);
        if (destState == FogState.Unknown)
        {
            PreviewPlan.Add(new JourneyStep
            {
                label = "SCAN",
                duration = scanDurationForRoom(previewPath[previewPath.Count - 1]),
                isScan = true,
                energyCost = MapModel.ScanEnergyCost,
            });
        }
    }

    /// <summary>Consume energy for the current journey step and advance index.</summary>
    public void ConsumeStepEnergy()
    {
        if (JourneyIdx >= 0 && JourneyIdx < JourneyPlan.Count)
            CurrentEnergy = Mathf.Max(0, CurrentEnergy - JourneyPlan[JourneyIdx].energyCost);
    }

    // ── Helpers ──────────────────────────────

    public static string PassageLabel(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor: return "CORRIDOR";
            case PassageType.Duct:     return "DUCT";
            case PassageType.Vent:     return "VENT";
            default:                   return "MOVE";
        }
    }
}
