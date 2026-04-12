using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Per-drone view component. Delegates game state to DroneModel,
/// handles movement animation, path visualization, and UI overlays.
/// </summary>
public class DroneController : MonoBehaviour
{
    // ── Model (pure game logic) ──────────────
    public DroneModel Model { get; private set; }

    public Vector2Int CurrentRoom { get; private set; }
    public bool IsSelected { get; set; }
    public bool IsMoving => moveSegIdx < moveWaypoints.Count - 1;

    public string DroneName { get; private set; } = "Drone";
    public int DroneIndex { get; private set; }

    // Discrete energy — delegates to model
    public int MaxEnergy => Model != null ? Model.MaxEnergy : 10;
    public int CurrentEnergy
    {
        get => Model != null ? Model.CurrentEnergy : 10;
        set { if (Model != null) Model.CurrentEnergy = value; }
    }
    public float EnergyFraction => Model != null ? Model.EnergyFraction : 1f;

    /// <summary>True when the preview path costs more energy than available.</summary>
    public bool PreviewExceedsEnergy => preview != null && preview.ExceedsEnergy;

    public string CurrentAction
    {
        get
        {
            if (IsMoving) return "MOVING";
            var tile = fog?.GetTile(CurrentRoom);
            if (tile != null && tile.State == FogState.Scanning) return "SCANNING";
            return "IDLE";
        }
    }

    public float ActionProgress
    {
        get
        {
            var tile = fog?.GetTile(CurrentRoom);
            if (tile != null && tile.State == FogState.Scanning) return tile.ScanProgress;
            return -1f;
        }
    }

    public float ActionTimeLeft
    {
        get
        {
            var tile = fog?.GetTile(CurrentRoom);
            if (tile != null && tile.State == FogState.Scanning) return tile.ScanTimeLeft;
            return 0f;
        }
    }

    public float ActionElapsed
    {
        get
        {
            var tile = fog?.GetTile(CurrentRoom);
            if (tile != null && tile.State == FogState.Scanning) return tile.ScanElapsed;
            return 0f;
        }
    }

    public float ActionTotalTime
    {
        get
        {
            var tile = fog?.GetTile(CurrentRoom);
            if (tile != null && tile.State == FogState.Scanning) return tile.ScanTotalTime;
            return 0f;
        }
    }

    HexMapGenerator map;
    FogOfWar fog;
    [SerializeField] float hoverY = 1f;

    // Simple waypoint movement
    enum WaypointKind { Normal, CorridorExit, RoomCenter, StationWall }
    struct MoveWP
    {
        public Vector3 pos;
        public WaypointKind kind;
        public Vector2Int room;
        public int journeyStep;      // which journey plan step this segment belongs to (-1 = none)
        public float durationToNext;  // seconds to reach the next waypoint
    }
    readonly List<MoveWP> moveWaypoints = new List<MoveWP>();
    int moveSegIdx;
    float moveSegT;

    // Selection visuals
    GameObject selectionRing;
    Material ringMat;
    LowPolyDrone droneModel;

    // Journey plan — full ordered list of steps (travel + scan + station actions) for UI display
    public struct JourneyStep
    {
        public string label;
        public float duration;
        public bool isScan;
        public bool isCharge;
        public bool isRefit;
        public int energyCost;
    }

    readonly List<JourneyStep> journeyPlan = new List<JourneyStep>();
    int journeyIdx = -1;

    // Station action timer (charge/refit)
    float stationActionElapsed;
    float stationActionDuration;

    /// <summary>True after completing a REFIT action, until a new move is issued.</summary>
    public bool IsRefitting { get; private set; }

    // All route visualization delegated to RoutePreview
    RoutePreview preview;

    internal int MoveSegIdx => moveSegIdx;
    internal float MoveSegT => moveSegT;

    public IReadOnlyList<JourneyStep> Journey => journeyPlan;
    public int JourneyCurrentIndex => journeyIdx;
    public IReadOnlyList<JourneyStep> PreviewJourney => preview?.Plan;
    public IReadOnlyList<StepAnchor> JourneyAnchors => preview?.JourneyAnchors;
    public IReadOnlyList<StepAnchor> PreviewAnchors => preview?.PreviewAnchors;
    public bool IsShowingPreview => preview != null && preview.IsShowing;

    /// <summary>Total duration of all journey steps.</summary>
    public float JourneyTotalTime
    {
        get { float t = 0; foreach (var s in journeyPlan) t += s.duration; return t; }
    }

    /// <summary>Elapsed time across all journey steps.</summary>
    public float JourneyElapsedTime
    {
        get
        {
            float t = 0;
            for (int i = 0; i < journeyPlan.Count; i++)
                t += GetJourneyStepElapsed(i);
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
    public float PreviewTotalTime => preview != null ? preview.TotalTime : 0f;

    /// <summary>Total energy cost of remaining journey steps (current + future).</summary>
    public int JourneyEnergyCost
    {
        get
        {
            int total = 0;
            for (int i = Mathf.Max(0, journeyIdx); i < journeyPlan.Count; i++)
                total += journeyPlan[i].energyCost;
            return total;
        }
    }

    /// <summary>Total energy cost of the preview journey.</summary>
    public int PreviewEnergyCost => preview != null ? preview.EnergyCost : 0;

    const int scanEnergyCost = 2;

    public float GetJourneyStepProgress(int i)
    {
        if (journeyIdx < 0 || i < 0 || i >= journeyPlan.Count) return 0f;
        if (i < journeyIdx) return 1f;
        if (i > journeyIdx) return 0f;
        if (journeyPlan[i].isScan)
        {
            var tile = fog?.GetTile(CurrentRoom);
            return tile != null ? tile.ScanProgress : 0f;
        }
        if (journeyPlan[i].isCharge || journeyPlan[i].isRefit)
            return stationActionDuration > 0 ? Mathf.Clamp01(stationActionElapsed / stationActionDuration) : 0f;
        // Travel step — compute from waypoint progress
        float totalDist = 0f, doneDist = 0f;
        for (int s = 0; s < moveWaypoints.Count - 1; s++)
        {
            if (moveWaypoints[s].journeyStep != i) continue;
            float segDist = Vector3.Distance(moveWaypoints[s].pos, moveWaypoints[s + 1].pos);
            totalDist += segDist;
            if (s < moveSegIdx) doneDist += segDist;
            else if (s == moveSegIdx) doneDist += segDist * moveSegT;
        }
        return totalDist > 0.001f ? Mathf.Clamp01(doneDist / totalDist) : 1f;
    }

    public float GetJourneyStepElapsed(int i)
    {
        return GetJourneyStepProgress(i) * journeyPlan[i].duration;
    }

    // ── public API ───────────────────────────

    public void Init(HexMapGenerator mapGen, FogOfWar fogOfWar, Vector2Int startRoom, string droneName = "Drone", int droneIndex = 0)
    {
        map = mapGen;
        fog = fogOfWar;
        DroneIndex = droneIndex;

        // Create game-logic model
        Model = new DroneModel
        {
            Name = droneName,
            MaxEnergy = 10,
            CurrentEnergy = 10,
            CurrentRoom = startRoom,
            FromRoom = startRoom,
            ToRoom = startRoom,
            TravelProgress = 1f,
        };

        CurrentRoom = startRoom;
        DroneName = droneName;
        CreateSelectionRing();
        Model.SpeedJitter = 1f;
        Model.InitSlots();
        preview = new RoutePreview(this, map, fog);

        moveWaypoints.Clear();
        moveSegIdx = 0;
        moveSegT = 0f;

        transform.position = RoomWorldPos(startRoom);

        // Notify start tile
        var tile = fog.GetTile(startRoom);
        if (tile != null)
            tile.OnDroneEnter(this);
    }

    /// <summary>True when the drone is executing a station action (charge/refit).</summary>
    public bool IsPerformingStationAction =>
        journeyIdx >= 0 && journeyIdx < journeyPlan.Count
        && (journeyPlan[journeyIdx].isCharge || journeyPlan[journeyIdx].isRefit);

    /// <summary>
    /// Start a station action (charge/refit) when the drone is already on a station tile.
    /// </summary>
    public void StartStationAction(RoomTile tile, StationType stationAction = StationType.None)
    {
        if (tile == null) return;
        if (IsMoving || IsPerformingStationAction) return;
        if (CurrentRoom != tile.Coord) return;
        IsRefitting = false;

        // Reset movement and journey state
        moveWaypoints.Clear();
        moveSegIdx = 0;
        moveSegT = 0f;
        stationActionElapsed = 0f;
        stationActionDuration = 0f;
        journeyPlan.Clear();
        preview?.ClearJourney();

        var station = tile.GetStation();
        if (station == null || station.StationType != stationAction) return;

        journeyPlan.Add(new JourneyStep
        {
            label = MapModel.StationLabel(stationAction),
            duration = MapModel.StationDuration(stationAction),
            isCharge = stationAction == StationType.Charging,
            isRefit = stationAction == StationType.Refitting,
            energyCost = 0,
        });

        journeyIdx = 0;

        // Move drone to station park point (station knows where the drone sits)
        Vector3 parkPos = tile.StationDroneParkPoint ?? RoomWorldPos(CurrentRoom);
        parkPos = new Vector3(parkPos.x, hoverY, parkPos.z);

        moveWaypoints.Add(new MoveWP
        {
            pos = transform.position,
            kind = WaypointKind.Normal,
            room = CurrentRoom,
            journeyStep = -1,
            durationToNext = 0.4f
        });
        moveWaypoints.Add(new MoveWP
        {
            pos = parkPos,
            kind = WaypointKind.StationWall,
            room = CurrentRoom,
            journeyStep = -1,
            durationToNext = 0f
        });

        // Create world step bar at the station
        preview?.SetStationJourney(parkPos);
    }

    public void SetPath(List<Vector2Int> newPath, StationType stationAction = StationType.None)
    {
        IsRefitting = false;
        // Calculate total energy cost of this path before committing
        int cost = 0;
        Vector2Int prev = CurrentRoom;
        foreach (var room in newPath)
        {
            var passage = fog?.GetTile(prev)?.GetPassage(room);
            var ptype = passage != null ? passage.Type : PassageType.Corridor;
            cost += MapModel.StepEnergyCost(ptype);
            prev = room;
        }
        var checkTile = fog?.GetTile(newPath[newPath.Count - 1]);
        if (checkTile != null && checkTile.State == FogState.Unknown && Model.CanScan)
            cost += scanEnergyCost;

        int available = CurrentEnergy - JourneyEnergyCost;
        if (cost > available) return;

        // Reset movement state
        moveWaypoints.Clear();
        moveSegIdx = 0;
        moveSegT = 0f;
        stationActionElapsed = 0f;
        stationActionDuration = 0f;

        // Build journey plan for UI
        journeyPlan.Clear();
        journeyIdx = -1;

        if (newPath.Count > 0)
        {
            journeyIdx = 0;
            prev = CurrentRoom;
            int stepIdx = 0;

            Vector3 roomCenter = RoomWorldPos(CurrentRoom);

            // If drone isn't at room center (e.g. at station wall), return there first
            float distToCenter = Vector3.Distance(transform.position, roomCenter);
            if (distToCenter > 0.2f)
            {
                moveWaypoints.Add(new MoveWP
                {
                    pos = transform.position,
                    kind = WaypointKind.Normal,
                    room = CurrentRoom,
                    journeyStep = -1,
                    durationToNext = 0.4f
                });
            }

            // Room center waypoint
            moveWaypoints.Add(new MoveWP
            {
                pos = roomCenter,
                kind = WaypointKind.Normal,
                room = CurrentRoom,
                journeyStep = 0,
                durationToNext = 0f
            });

            foreach (var room in newPath)
            {
                var passage = fog?.GetTile(prev)?.GetPassage(room);
                var ptype = passage != null ? passage.Type : PassageType.Corridor;
                float hopDur = MapModel.TravelTime(ptype);
                journeyPlan.Add(new JourneyStep
                {
                    label = MapModel.PassageLabel(ptype),
                    duration = hopDur,
                    isScan = false,
                    energyCost = MapModel.StepEnergyCost(ptype),
                });

                // Get entry points from passage wall entities
                Vector3 pA = PassagePoint(prev, room, hoverY);
                Vector3 pB = PassagePoint(room, prev, hoverY);
                Vector3 destCenter = RoomWorldPos(room);

                // Distribute hop duration proportionally across 3 segments
                Vector3 fromPos = moveWaypoints[moveWaypoints.Count - 1].pos;
                float d1 = Vector3.Distance(fromPos, pA);
                float d2 = Vector3.Distance(pA, pB);
                float d3 = Vector3.Distance(pB, destCenter);
                float dTotal = d1 + d2 + d3;
                float dur1 = dTotal > 0.001f ? hopDur * d1 / dTotal : hopDur / 3f;
                float dur2 = dTotal > 0.001f ? hopDur * d2 / dTotal : hopDur / 3f;
                float dur3 = dTotal > 0.001f ? hopDur * d3 / dTotal : hopDur / 3f;

                // Set duration on previous waypoint (keep its journeyStep — it's the arrival for the prior hop)
                var lastWP = moveWaypoints[moveWaypoints.Count - 1];
                lastWP.durationToNext = dur1;
                moveWaypoints[moveWaypoints.Count - 1] = lastWP;

                // Passage entry on departure side
                moveWaypoints.Add(new MoveWP
                {
                    pos = pA,
                    kind = WaypointKind.Normal,
                    room = prev,
                    journeyStep = stepIdx,
                    durationToNext = dur2
                });

                // Passage exit on arrival side — triggers fog reveal
                moveWaypoints.Add(new MoveWP
                {
                    pos = pB,
                    kind = WaypointKind.CorridorExit,
                    room = room,
                    journeyStep = stepIdx,
                    durationToNext = dur3
                });

                // Room center — triggers arrival events
                moveWaypoints.Add(new MoveWP
                {
                    pos = destCenter,
                    kind = WaypointKind.RoomCenter,
                    room = room,
                    journeyStep = stepIdx,
                    durationToNext = 0f
                });

                prev = room;
                stepIdx++;
            }

            // If final room needs scanning and drone has Scanner, add a SCAN step
            var finalTile = fog?.GetTile(newPath[newPath.Count - 1]);
            if (finalTile != null && finalTile.State == FogState.Unknown && Model.CanScan)
            {
                journeyPlan.Add(new JourneyStep
                {
                    label = "SCAN",
                    duration = finalTile.ScanTotalTime,
                    isScan = true,
                    energyCost = scanEnergyCost,
                });
            }

            // Append station action
            var station = finalTile?.GetStation();
            if (station != null && station.StationType == stationAction)
            {
                journeyPlan.Add(new JourneyStep
                {
                    label = MapModel.StationLabel(stationAction),
                    duration = MapModel.StationDuration(stationAction),
                    isCharge = stationAction == StationType.Charging,
                    isRefit = stationAction == StationType.Refitting,
                    energyCost = 0,
                });
            }

            // If station action, add waypoint to approach station wall
            if (stationAction != StationType.None && finalTile != null)
            {
                Vector3? parkPt = finalTile.StationDroneParkPoint;
                if (parkPt.HasValue)
                {
                    Vector3 parkPos = new Vector3(parkPt.Value.x, hoverY, parkPt.Value.z);

                    var lastWP = moveWaypoints[moveWaypoints.Count - 1];
                    lastWP.durationToNext = 0.4f;
                    moveWaypoints[moveWaypoints.Count - 1] = lastWP;

                    moveWaypoints.Add(new MoveWP
                    {
                        pos = parkPos,
                        kind = WaypointKind.StationWall,
                        room = newPath[newPath.Count - 1],
                        journeyStep = -1,
                        durationToNext = 0f
                    });
                }
            }
        }

        // Smooth pass-through room centers: pull toward the chord between
        // the previous and next waypoints so the drone cuts a natural arc
        // instead of detouring to dead center.
        for (int wi = 1; wi < moveWaypoints.Count - 1; wi++)
        {
            var wp = moveWaypoints[wi];
            if (wp.kind != WaypointKind.RoomCenter) continue;

            Vector3 prev3 = moveWaypoints[wi - 1].pos;
            Vector3 next3 = moveWaypoints[wi + 1].pos;
            Vector3 mid = (prev3 + next3) * 0.5f;
            // Blend 70% toward chord midpoint, keep 30% of original center
            wp.pos = Vector3.Lerp(wp.pos, mid, 0.7f);
            moveWaypoints[wi] = wp;
        }

        // Build route visualization (path line + step bars)
        preview?.SetJourney(newPath, stationAction);
        preview?.ClearPreview();
    }

    public void ShowPreviewPath(List<Vector2Int> previewPath, StationType stationAction = StationType.None)
        => preview?.ShowPath(previewPath, stationAction);

    public void ShowStationPreview(RoomTile tile, StationType stationAction = StationType.None)
        => preview?.ShowStation(tile, stationAction);

    public void ClearPreviewPath() => preview?.ClearPreview();

    void Start()
    {
        // Find the LowPolyDrone model (child)
        droneModel = GetComponentInChildren<LowPolyDrone>();
    }

    void Update()
    {
        if (!Application.isPlaying || map == null) return;

        UpdateMovement();
        UpdateJourney();
        preview?.Update();
        UpdateSelectionVisuals();
    }

    void UpdateSelectionVisuals()
    {
        // Ring: show + pulse when selected
        if (selectionRing != null)
        {
            selectionRing.SetActive(IsSelected);
            if (IsSelected && ringMat != null)
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 4f);
                Color col = Palette.WithAlpha(Palette.SelectionRing, pulse);
                ringMat.color = col;
                ringMat.SetColor("_BaseColor", col);
            }
        }

        // Drone glow: color reflects state, boost when selected
        if (droneModel != null && droneModel.GlowMaterial != null)
        {
            float baseInt = droneModel.BaseGlowIntensity;
            Color stateCol;
            if (CurrentEnergy <= 0)
                stateCol = Palette.DroneDepleted;
            else if (moveWaypoints.Count >= 2)
                stateCol = Palette.DroneMoving;
            else if (IsSelected)
                stateCol = Palette.DroneSelected;
            else
                stateCol = Palette.DroneIdle;

            droneModel.GlowMaterial.color = stateCol;
            if (IsSelected)
            {
                float boost = 1.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                droneModel.GlowMaterial.SetColor("_EmissionColor", stateCol * baseInt * boost);
            }
            else
            {
                droneModel.GlowMaterial.SetColor("_EmissionColor", stateCol * baseInt);
            }
        }
    }

    void UpdateMovement()
    {
        if (moveWaypoints.Count < 2 || moveSegIdx >= moveWaypoints.Count - 1)
            return;

        float dur = moveWaypoints[moveSegIdx].durationToNext;
        if (dur <= 0.001f)
        {
            moveSegT = 0f;
            moveSegIdx++;
            transform.position = moveWaypoints[moveSegIdx].pos;
            OnReachedWaypoint(moveSegIdx);
            return;
        }

        moveSegT += Time.deltaTime / dur;
        if (moveSegT >= 1f)
        {
            float overflow = (moveSegT - 1f) * dur;
            moveSegT = 0f;
            moveSegIdx++;
            transform.position = moveWaypoints[moveSegIdx].pos;
            OnReachedWaypoint(moveSegIdx);

            // Carry leftover time into next segment
            if (moveSegIdx < moveWaypoints.Count - 1)
            {
                float nextDur = moveWaypoints[moveSegIdx].durationToNext;
                if (nextDur > 0.001f)
                    moveSegT = overflow / nextDur;
            }
        }
        else
        {
            int i = moveSegIdx;
            int last = moveWaypoints.Count - 1;

            // Ease on endpoints only
            bool isFirst = i == 0;
            bool isLast  = i == last - 1;
            float t;
            if (isFirst && isLast) t = SmoothStep(moveSegT);
            else if (isFirst)      t = EaseIn(moveSegT);
            else if (isLast)       t = EaseOut(moveSegT);
            else                   t = moveSegT;

            // Catmull-Rom through waypoints for smooth curves
            Vector3 p0 = moveWaypoints[Mathf.Max(i - 1, 0)].pos;
            Vector3 p1 = moveWaypoints[i].pos;
            Vector3 p2 = moveWaypoints[i + 1].pos;
            Vector3 p3 = moveWaypoints[Mathf.Min(i + 2, last)].pos;
            Vector3 pos = CatmullRom(p0, p1, p2, p3, t);
            pos.y = Mathf.Lerp(p1.y, p2.y, t); // keep Y flat/linear
            transform.position = pos;

            // Face direction of travel (tangent of curve)
            Vector3 tangent = CatmullRomTangent(p0, p1, p2, p3, t);
            tangent.y = 0f;
            if (tangent.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(tangent), Time.deltaTime * 8f);
        }
    }

    void OnReachedWaypoint(int idx)
    {
        var wp = moveWaypoints[idx];

        switch (wp.kind)
        {
            case WaypointKind.CorridorExit:
                // Entering new room's side — reveal fog
                var destTile = fog?.GetTile(wp.room);
                if (destTile != null)
                    destTile.OnDroneEnter(this);
                break;

            case WaypointKind.RoomCenter:
                // Exit old room
                if (wp.room != CurrentRoom)
                {
                    var oldTile = fog?.GetTile(CurrentRoom);
                    if (oldTile != null)
                        oldTile.OnDroneExit(this);
                }
                CurrentRoom = wp.room;

                // Consume energy for the completed travel step
                int step = wp.journeyStep;
                if (step >= 0 && step < journeyPlan.Count
                    && !journeyPlan[step].isScan
                    && !journeyPlan[step].isCharge
                    && !journeyPlan[step].isRefit)
                {
                    CurrentEnergy = Mathf.Max(0, CurrentEnergy - journeyPlan[step].energyCost);
                    journeyIdx = step + 1;
                }

                // Trigger scan / reveal
                var arrivedTile = fog?.GetTile(CurrentRoom);
                if (arrivedTile != null)
                    arrivedTile.OnDroneArrived(Model.CanScan);
                break;

            case WaypointKind.StationWall:
                break;
        }
    }

    void UpdateJourney()
    {
        // Auto-create a 1-step journey if scanning without a move order (only if drone has Scanner)
        if (journeyPlan.Count == 0 && !IsMoving && Model.CanScan)
        {
            var tile = fog?.GetTile(CurrentRoom);
            if (tile != null && tile.State == FogState.Scanning)
            {
                journeyPlan.Add(new JourneyStep
                {
                    label = "SCAN",
                    duration = tile.ScanTotalTime,
                    isScan = true,
                    energyCost = scanEnergyCost,
                });
                journeyIdx = 0;

                // World bar at room center for scan-only journey
                preview?.ClearJourney();
                Vector3 rc = map.HexCenter(CurrentRoom);
                preview?.SetStationJourney(new Vector3(rc.x, hoverY, rc.z));
            }
        }

        // Check scan step completion
        if (journeyIdx >= 0 && journeyIdx < journeyPlan.Count
            && journeyPlan[journeyIdx].isScan)
        {
            var tile = fog?.GetTile(CurrentRoom);
            if (tile != null && tile.State != FogState.Scanning && tile.State != FogState.Unknown)
            {
                CurrentEnergy = Mathf.Max(0, CurrentEnergy - journeyPlan[journeyIdx].energyCost);
                journeyIdx++;
            }
        }

        // Advance charge/refit step (wait for drone to finish moving to station wall)
        if (journeyIdx >= 0 && journeyIdx < journeyPlan.Count
            && (journeyPlan[journeyIdx].isCharge || journeyPlan[journeyIdx].isRefit))
        {
            if (IsMoving) return;
            // Start the timer on first frame of this step
            if (stationActionElapsed == 0f && stationActionDuration == 0f)
            {
                stationActionDuration = journeyPlan[journeyIdx].duration;
                stationActionElapsed = 0f;
            }

            stationActionElapsed += Time.deltaTime;

            if (stationActionElapsed >= stationActionDuration)
            {
                // Charge: restore energy, loop until full
                if (journeyPlan[journeyIdx].isCharge)
                {
                    CurrentEnergy = Mathf.Min(MaxEnergy, CurrentEnergy + MapModel.ChargeEnergyGain);
                    if (CurrentEnergy < MaxEnergy)
                    {
                        // Reset timer for another charge cycle
                        stationActionElapsed = 0f;
                        return;
                    }
                }
                // Refit: enable gear management until drone moves again
                if (journeyPlan[journeyIdx].isRefit)
                    IsRefitting = true;

                stationActionElapsed = 0f;
                stationActionDuration = 0f;
                journeyIdx++;
            }
        }

        // Clear finished journey
        if (journeyPlan.Count > 0 && journeyIdx >= journeyPlan.Count)
        {
            journeyPlan.Clear();
            journeyIdx = -1;
            preview?.ClearJourney();
        }
    }

    // ── helpers ──────────────────────────────

    /// <summary>Fallback passage midpoint when no Passage entity exists yet.</summary>
    Vector3 FallbackPassagePoint(Vector2Int from, Vector2Int to)
    {
        var (midA, _) = map.PassageEndpoints(from, to);
        return new Vector3(midA.x, hoverY, midA.z);
    }

    /// <summary>Get passage park point for the 'from' side at given Y.</summary>
    Vector3 PassagePoint(Vector2Int from, Vector2Int to, float y)
    {
        var pass = fog?.GetTile(from)?.GetPassage(to);
        if (pass != null)
            return new Vector3(pass.DroneParkPoint.x, y, pass.DroneParkPoint.z);
        var (mid, _) = map.PassageEndpoints(from, to);
        return new Vector3(mid.x, y, mid.z);
    }

    Vector3 RoomWorldPos(Vector2Int room)
    {
        Vector3 c = map.HexCenter(room);
        return new Vector3(c.x, hoverY, c.z);
    }

    float SmoothStep(float t) => t * t * (3f - 2f * t);
    float EaseIn(float t)     => t * t;
    float EaseOut(float t)    => 1f - (1f - t) * (1f - t);

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        return 0.5f * (
            (-p0 + p2) +
            (4f * p0 - 10f * p1 + 8f * p2 - 2f * p3) * t +
            (-3f * p0 + 9f * p1 - 9f * p2 + 3f * p3) * t2
        );
    }

    // ── dashed ribbon mesh utility ──────────

    /// <summary>
    /// Build a dashed ribbon mesh into the given mesh from waypoints.
    /// Dashes are anchored to world-space positions; consumedDist clips
    /// the front so the line is eaten as the drone advances.
    /// </summary>
    internal static void BuildDashedRibbonInto(Mesh targetMesh, List<Vector3> waypoints, List<float> cumulDist, float consumedDist, float width, float dash, float gap)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            Vector3 a = waypoints[i];
            Vector3 b = waypoints[i + 1];
            float segStart = cumulDist[i];
            float segEnd   = cumulDist[i + 1];
            float segLen   = segEnd - segStart;
            if (segLen < 0.001f) continue;

            if (segEnd <= consumedDist) continue;

            Vector3 dir = (b - a) / segLen;
            dir.y = 0f;
            Vector3 right = new Vector3(-dir.z, 0f, dir.x);
            float hw = width * 0.5f;
            float cycle = dash + gap;

            float local = 0f;
            while (local < segLen - 0.001f)
            {
                float worldDist = segStart + local;
                float phase = worldDist % cycle;

                if (phase < dash)
                {
                    float dashRemain = dash - phase;
                    float segRemain  = segLen - local;
                    float seg = Mathf.Min(dashRemain, segRemain);
                    if (seg < 0.001f) { local += 0.001f; continue; }
                    float dStart = worldDist;
                    float dEnd   = worldDist + seg;

                    if (dEnd > consumedDist)
                    {
                        float clampStart = Mathf.Max(dStart, consumedDist);
                        Vector3 p0 = a + dir * (clampStart - segStart);
                        Vector3 p1 = a + dir * (dEnd - segStart);

                        int vi = verts.Count;
                        verts.Add(p0 - right * hw);
                        verts.Add(p0 + right * hw);
                        verts.Add(p1 - right * hw);
                        verts.Add(p1 + right * hw);

                        tris.Add(vi);     tris.Add(vi + 2); tris.Add(vi + 1);
                        tris.Add(vi + 1); tris.Add(vi + 2); tris.Add(vi + 3);
                    }

                    local += seg;
                }
                else
                {
                    float gapRemain = cycle - phase;
                    local += Mathf.Max(Mathf.Min(gapRemain, segLen - local), 0.001f);
                }
            }
        }

        targetMesh.Clear();
        if (verts.Count > 0)
        {
            targetMesh.SetVertices(verts);
            targetMesh.SetTriangles(tris, 0);
            targetMesh.RecalculateBounds();
        }
    }

    void OnDestroy()
    {
        preview?.Destroy();
    }

    // ── selection ring ───────────────────────

    void CreateSelectionRing()
    {
        selectionRing = new GameObject("SelectionRing");
        selectionRing.transform.SetParent(transform, false);
        selectionRing.transform.localPosition = new Vector3(0f, -hoverY + 0.05f, 0f);

        var mf = selectionRing.AddComponent<MeshFilter>();
        var mr = selectionRing.AddComponent<MeshRenderer>();

        mf.sharedMesh = MakeRingMesh(0.45f, 0.35f, 12);

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        ringMat = new Material(sh);
        Color col = Palette.WithAlpha(Palette.SelectionRing, 0.8f);
        ringMat.color = col;
        ringMat.SetColor("_BaseColor", col);
        ringMat.SetFloat("_Surface", 1f);
        ringMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ringMat.SetOverrideTag("RenderType", "Transparent");
        ringMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        ringMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ringMat.SetInt("_ZWrite", 0);
        ringMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mr.sharedMaterial = ringMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        selectionRing.SetActive(false);
    }

    Mesh MakeRingMesh(float outerR, float innerR, int segments)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            verts.Add(new Vector3(Mathf.Cos(a) * outerR, 0f, Mathf.Sin(a) * outerR));
            verts.Add(new Vector3(Mathf.Cos(a) * innerR, 0f, Mathf.Sin(a) * innerR));
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int o = i * 2, n = i * 2 + 1;
            int no = next * 2, nn = next * 2 + 1;
            tris.Add(o);  tris.Add(no); tris.Add(n);
            tris.Add(n);  tris.Add(no); tris.Add(nn);
        }

        var m = new Mesh { name = "SelectionRing" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        return m;
    }
}
