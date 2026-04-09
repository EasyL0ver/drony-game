using UnityEngine;
using UnityEngine.UI;
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
    public bool IsMoving => path.Count > 0 || travelProgress < 1f || isCentering;

    public string DroneName { get; private set; } = "Drone";

    // Discrete energy — delegates to model
    public int MaxEnergy => Model != null ? Model.MaxEnergy : 10;
    public int CurrentEnergy
    {
        get => Model != null ? Model.CurrentEnergy : 10;
        set { if (Model != null) Model.CurrentEnergy = value; }
    }
    public float EnergyFraction => Model != null ? Model.EnergyFraction : 1f;

    /// <summary>True when the preview path costs more energy than available.</summary>
    public bool PreviewExceedsEnergy =>
        isShowingPreview && PreviewEnergyCost > (CurrentEnergy - JourneyEnergyCost);

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
    Queue<Vector2Int> path = new Queue<Vector2Int>();

    // Current hop
    Vector2Int fromRoom;
    Vector2Int toRoom;
    float travelProgress = 1f;  // 1 = arrived
    float travelDuration;
    [SerializeField] float hoverY = 1f;

    // Multi-waypoint hop path (routes through passage openings)
    List<Vector3> hopPoints = new List<Vector3>();
    List<float> hopCumulDist = new List<float>();
    float hopTotalDist;

    // Selection visuals
    GameObject selectionRing;
    Material ringMat;
    LowPolyDrone droneModel;

    // Idle animation
    float idlePhase;
    float idleBlend;       // 0 = moving, 1 = fully idle
    Vector3 wanderTarget;
    float wanderWait;
    bool hasWanderTarget;

    // Centering phase: drone glides to room center before scanning starts
    bool isCentering;
    Vector3 centerFrom;
    Vector3 centerTo;
    float centerProgress;
    const float centerDuration = 0.45f;

    // Swarm
    Vector3 swarmOffset;   // per-drone random position offset
    float speedJitter;     // per-drone speed multiplier

    // Journey plan — full ordered list of steps (travel + scan) for UI display
    public struct JourneyStep
    {
        public string label;
        public float duration;
        public bool isScan;
        public int energyCost;
    }

    readonly List<JourneyStep> journeyPlan = new List<JourneyStep>();
    int journeyIdx = -1;

    // Floor path visualization (world-space dashed ribbon showing planned route)
    readonly List<Vector3> journeyWaypoints = new List<Vector3>();
    readonly List<float> journeyCumulDist = new List<float>();
    GameObject pathLineGO;
    MeshFilter pathMF;
    MeshRenderer pathMR;
    Material pathMat;
    Mesh pathMesh;
    const float pathY = 0.06f;
    const float pathWidth = 0.12f;
    const float dashLen = 0.30f;
    const float gapLen  = 0.15f;
    const float dashCycle = dashLen + gapLen;

    // Screen-space UI progress bars for each journey step (projected from world coords)
    struct WorldStepBar
    {
        public GameObject root;
        public RectTransform rect;
        public Image bgImage;
        public Image fillImage;
        public RectTransform fillRect;
        public Text labelText;
        public Text timeText;
        public Vector3 worldPos;   // world position to project
    }
    readonly List<WorldStepBar> worldStepBars = new List<WorldStepBar>();
    static Canvas stepBarCanvas;
    static CanvasScaler stepBarScaler;
    const float barUIWidth = 160f;
    const float barUIHeight = 22f;

    // Preview path (hover) — separate from the real journey
    bool isShowingPreview;
    readonly List<Vector3> previewWaypoints = new List<Vector3>();
    readonly List<float> previewCumulDist = new List<float>();
    readonly List<JourneyStep> previewPlan = new List<JourneyStep>();
    readonly List<WorldStepBar> previewStepBars = new List<WorldStepBar>();
    GameObject previewLineGO;
    MeshFilter previewMF;
    MeshRenderer previewMR;
    Material previewMat;
    Mesh previewMesh;

    public IReadOnlyList<JourneyStep> Journey => journeyPlan;
    public int JourneyCurrentIndex => journeyIdx;
    public IReadOnlyList<JourneyStep> PreviewJourney => previewPlan;
    public bool IsShowingPreview => isShowingPreview;

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
    public float PreviewTotalTime
    {
        get { float t = 0; foreach (var s in previewPlan) t += s.duration; return t; }
    }

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
    public int PreviewEnergyCost
    {
        get
        {
            int total = 0;
            foreach (var s in previewPlan) total += s.energyCost;
            return total;
        }
    }

    static int StepEnergyCost(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor: return 1;
            case PassageType.Duct:     return 2;
            case PassageType.Vent:     return 3;
            default: return 1;
        }
    }

    const int scanEnergyCost = 2;

    public float GetJourneyStepProgress(int i)
    {
        if (journeyIdx < 0 || i < 0 || i >= journeyPlan.Count) return 0f;
        if (i < journeyIdx) return 1f;
        if (i > journeyIdx) return 0f;
        // Active step
        if (journeyPlan[i].isScan)
        {
            var tile = fog?.GetTile(CurrentRoom);
            return tile != null ? tile.ScanProgress : 0f;
        }
        return travelProgress;
    }

    public float GetJourneyStepElapsed(int i)
    {
        return GetJourneyStepProgress(i) * journeyPlan[i].duration;
    }

    // ── public API ───────────────────────────

    public void Init(HexMapGenerator mapGen, FogOfWar fogOfWar, Vector2Int startRoom, string droneName = "Drone")
    {
        map = mapGen;
        fog = fogOfWar;

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
        fromRoom = startRoom;
        toRoom = startRoom;
        travelProgress = 1f;
        DroneName = droneName;
        CreateSelectionRing();
        idlePhase = Random.Range(0f, Mathf.PI * 2f);

        // Swarm: each drone gets a unique offset + speed
        float sAngle = Random.Range(0f, Mathf.PI * 2f);
        float sDist  = Random.Range(0.1f, 0.35f);
        swarmOffset = new Vector3(Mathf.Cos(sAngle) * sDist, 0f, Mathf.Sin(sAngle) * sDist);
        speedJitter = Random.Range(0.8f, 1.2f);
        Model.SpeedJitter = speedJitter;
        Model.InitSlots();

        transform.position = RoomWorldPos(startRoom) + swarmOffset;

        // Notify start tile
        var tile = fog.GetTile(startRoom);
        if (tile != null)
            tile.OnDroneEnter();
    }

    public void SetPath(List<Vector2Int> newPath)
    {
        // Calculate total energy cost of this path before committing
        int cost = 0;
        Vector2Int prev = CurrentRoom;
        foreach (var room in newPath)
        {
            cost += StepEnergyCost(GetPassageType(prev, room));
            prev = room;
        }
        // Add scan cost if final room is unknown and drone can scan
        var checkTile = fog?.GetTile(newPath[newPath.Count - 1]);
        if (checkTile != null && checkTile.State == FogState.Unknown && Model.CanScan)
            cost += scanEnergyCost;

        int available = CurrentEnergy - JourneyEnergyCost;
        if (cost > available) return; // Not enough energy — block move

        path.Clear();
        hopPoints.Clear();
        hopCumulDist.Clear();
        isCentering = false;

        // If mid-travel, resolve which room we're in logically
        if (travelProgress < 1f)
        {
            CurrentRoom = travelProgress < 0.5f ? fromRoom : toRoom;
            travelProgress = 1f;
        }

        // Reset so the "hop arrived" check doesn't fire spuriously
        fromRoom = CurrentRoom;
        toRoom = CurrentRoom;

        foreach (var room in newPath)
            path.Enqueue(room);

        // Build journey plan for UI
        journeyPlan.Clear();
        journeyIdx = -1;

        if (newPath.Count > 0)
        {
            journeyIdx = 0;
            prev = CurrentRoom;
            foreach (var room in newPath)
            {
                var ptype = GetPassageType(prev, room);
                float dur = GetTravelTime(prev, room) * speedJitter;
                journeyPlan.Add(new JourneyStep
                {
                    label = PassageLabel(ptype),
                    duration = dur,
                    isScan = false,
                    energyCost = StepEnergyCost(ptype),
                });
                prev = room;
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
        }

        // Build floor-level waypoints for dashed path line (origin + 3 per hop)
        journeyWaypoints.Clear();
        journeyCumulDist.Clear();
        if (newPath.Count > 0)
        {
            // Origin room center
            Vector3 origin = map.HexCenter(CurrentRoom);
            journeyWaypoints.Add(new Vector3(origin.x, pathY, origin.z));

            prev = CurrentRoom;            foreach (var room in newPath)
            {
                var (midA, midB) = map.PassageEndpoints(prev, room);
                journeyWaypoints.Add(new Vector3(midA.x, pathY, midA.z));
                journeyWaypoints.Add(new Vector3(midB.x, pathY, midB.z));
                Vector3 rc = map.HexCenter(room);
                journeyWaypoints.Add(new Vector3(rc.x, pathY, rc.z));
                prev = room;
            }

            // Precompute cumulative distances
            journeyCumulDist.Add(0f);
            for (int i = 1; i < journeyWaypoints.Count; i++)
                journeyCumulDist.Add(journeyCumulDist[i - 1]
                    + Vector3.Distance(journeyWaypoints[i - 1], journeyWaypoints[i]));
        }

        // Build world-space progress bars at each step location
        BuildWorldStepBars(newPath);

        // Clear any hover preview since we're now moving
        ClearPreviewPath();
    }

    /// <summary>
    /// Show a preview path + step bars for a potential move (on hover).
    /// Does NOT start movement.
    /// </summary>
    public void ShowPreviewPath(List<Vector2Int> previewPath)
    {
        if (previewPath == null || previewPath.Count == 0)
        {
            ClearPreviewPath();
            return;
        }

        isShowingPreview = true;

        // Build preview journey plan
        previewPlan.Clear();
        Vector2Int prev = CurrentRoom;
        foreach (var room in previewPath)
        {
            var ptype = GetPassageType(prev, room);
            float dur = GetTravelTime(prev, room) * speedJitter;
            previewPlan.Add(new JourneyStep
            {
                label = PassageLabel(ptype),
                duration = dur,
                isScan = false,
                energyCost = StepEnergyCost(ptype),
            });
            prev = room;
        }

        var finalTile = fog?.GetTile(previewPath[previewPath.Count - 1]);
        if (finalTile != null && finalTile.State == FogState.Unknown && Model.CanScan)
        {
            previewPlan.Add(new JourneyStep
            {
                label = "SCAN",
                duration = finalTile.ScanTotalTime,
                isScan = true,
                energyCost = scanEnergyCost,
            });
        }

        // Build preview waypoints
        previewWaypoints.Clear();
        previewCumulDist.Clear();
        Vector3 origin = map.HexCenter(CurrentRoom);
        previewWaypoints.Add(new Vector3(origin.x, pathY, origin.z));

        prev = CurrentRoom;
        foreach (var room in previewPath)
        {
            var (midA, midB) = map.PassageEndpoints(prev, room);
            previewWaypoints.Add(new Vector3(midA.x, pathY, midA.z));
            previewWaypoints.Add(new Vector3(midB.x, pathY, midB.z));
            Vector3 rc = map.HexCenter(room);
            previewWaypoints.Add(new Vector3(rc.x, pathY, rc.z));
            prev = room;
        }

        previewCumulDist.Add(0f);
        for (int i = 1; i < previewWaypoints.Count; i++)
            previewCumulDist.Add(previewCumulDist[i - 1]
                + Vector3.Distance(previewWaypoints[i - 1], previewWaypoints[i]));

        // Build preview dashed line
        EnsurePreviewLine();
        previewLineGO.SetActive(true);
        bool overBudget = PreviewExceedsEnergy;
        Color col = overBudget
            ? new Color(1f, 0.15f, 0.10f, 0.5f)   // red — can't afford
            : new Color(1f, 0.75f, 0f, 0.4f);      // orange — ok
        previewMat.color = col;
        previewMat.SetColor("_BaseColor", col);
        BuildDashedRibbonInto(previewMesh, previewWaypoints, previewCumulDist, 0f);

        // Build preview step bars
        DestroyPreviewStepBars();
        EnsureStepBarCanvas();
        prev = CurrentRoom;
        int stepIdx = 0;
        foreach (var room in previewPath)
        {
            var (midA, midB) = map.PassageEndpoints(prev, room);
            Vector3 passageMid = (midA + midB) * 0.5f;
            CreatePreviewStepBar(new Vector3(passageMid.x, 0.5f, passageMid.z), stepIdx, overBudget);
            stepIdx++;
            prev = room;
        }
        if (finalTile != null && finalTile.State == FogState.Unknown)
        {
            Vector3 rc = map.HexCenter(previewPath[previewPath.Count - 1]);
            CreatePreviewStepBar(new Vector3(rc.x, 0.5f, rc.z), stepIdx, overBudget);
        }
    }

    /// <summary>
    /// Hide the preview path + step bars.
    /// </summary>
    public void ClearPreviewPath()
    {
        if (!isShowingPreview) return;
        isShowingPreview = false;

        if (previewLineGO != null)
            previewLineGO.SetActive(false);

        previewPlan.Clear();
        previewWaypoints.Clear();
        previewCumulDist.Clear();
        DestroyPreviewStepBars();
    }

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
        UpdatePathLine();
        UpdateWorldStepBars();
        UpdatePreviewStepBars();
        UpdateIdleAnimation();
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
                Color col = new Color(0f, 0.85f, 1f, pulse);
                ringMat.color = col;
                ringMat.SetColor("_BaseColor", col);
            }
        }

        // Drone glow: boost when selected
        if (droneModel != null && droneModel.GlowMaterial != null)
        {
            Color baseCol = droneModel.BaseGlowColor;
            float baseInt = droneModel.BaseGlowIntensity;

            if (IsSelected)
            {
                float boost = 1.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                droneModel.GlowMaterial.SetColor("_EmissionColor", baseCol * baseInt * boost);
            }
            else
            {
                droneModel.GlowMaterial.SetColor("_EmissionColor", baseCol * baseInt);
            }
        }
    }

    void UpdateMovement()
    {
        // Centering phase: glide to room center before scanning
        if (isCentering)
        {
            centerProgress += Time.deltaTime / centerDuration;
            centerProgress = Mathf.Clamp01(centerProgress);
            float t = SmoothStep(centerProgress);
            transform.position = Vector3.Lerp(centerFrom, centerTo, t);

            if (centerProgress >= 1f)
            {
                isCentering = false;
                // Now trigger scan / reveal
                var arrivedTile = fog?.GetTile(CurrentRoom);
                if (arrivedTile != null)
                    arrivedTile.OnDroneArrived(Model.CanScan);
            }
            return;
        }

        if (travelProgress >= 1f)
        {
            if (toRoom != CurrentRoom)
            {
                // Left old room, arrived at new room
                var oldTile = fog?.GetTile(CurrentRoom);
                if (oldTile != null)
                    oldTile.OnDroneExit();

                CurrentRoom = toRoom;

                // Advance journey (travel step complete) and consume energy
                if (journeyIdx >= 0 && journeyIdx < journeyPlan.Count
                    && !journeyPlan[journeyIdx].isScan)
                {
                    CurrentEnergy = Mathf.Max(0, CurrentEnergy - journeyPlan[journeyIdx].energyCost);
                    journeyIdx++;
                }

                // If room needs scanning and drone has Scanner and this is the last hop, center first
                var arrivedTile = fog?.GetTile(CurrentRoom);
                bool needsScan = arrivedTile != null && arrivedTile.State == FogState.Unknown && Model.CanScan;
                if (needsScan && path.Count == 0)
                {
                    centerFrom = transform.position;
                    centerTo = RoomWorldPos(CurrentRoom);
                    centerProgress = 0f;
                    isCentering = true;
                    return;
                }

                // No scan needed — trigger immediately
                if (arrivedTile != null)
                    arrivedTile.OnDroneArrived(Model.CanScan);
            }

            if (path.Count > 0)
            {
                fromRoom = CurrentRoom;
                toRoom = path.Dequeue();
                travelDuration = GetTravelTime(fromRoom, toRoom) * speedJitter;
                travelProgress = 0f;

                // Build multi-waypoint path through passage opening
                hopPoints.Clear();
                hopCumulDist.Clear();

                hopPoints.Add(transform.position);
                var (midA, midB) = map.PassageEndpoints(fromRoom, toRoom);
                hopPoints.Add(new Vector3(midA.x, hoverY, midA.z));
                hopPoints.Add(new Vector3(midB.x, hoverY, midB.z));
                hopPoints.Add(RoomWorldPos(toRoom) + swarmOffset);

                // Cumulative distance table for proportional interpolation
                hopTotalDist = 0f;
                hopCumulDist.Add(0f);
                for (int i = 1; i < hopPoints.Count; i++)
                {
                    hopTotalDist += Vector3.Distance(hopPoints[i - 1], hopPoints[i]);
                    hopCumulDist.Add(hopTotalDist);
                }

                // Reveal destination the moment the drone enters the corridor
                var destTile = fog?.GetTile(toRoom);
                if (destTile != null)
                    destTile.OnDroneEnter();
            }
        }
        else
        {
            travelProgress += Time.deltaTime / travelDuration;
            travelProgress = Mathf.Clamp01(travelProgress);

            transform.position = EvalHopPath(SmoothStep(travelProgress));
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
                DestroyWorldStepBars();
                Vector3 rc = map.HexCenter(CurrentRoom);
                CreateStepBar(new Vector3(rc.x, 0.5f, rc.z), 0);
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

        // Clear finished journey
        if (journeyPlan.Count > 0 && journeyIdx >= journeyPlan.Count)
        {
            journeyPlan.Clear();
            journeyWaypoints.Clear();
            journeyCumulDist.Clear();
            journeyIdx = -1;
            DestroyWorldStepBars();
        }
    }

    void UpdateIdleAnimation()
    {
        float target = IsMoving ? 0f : 1f;
        idleBlend = Mathf.MoveTowards(idleBlend, target, Time.deltaTime * 3f);

        if (idleBlend < 0.001f)
        {
            transform.rotation = Quaternion.identity;
            hasWanderTarget = false;
            return;
        }

        float t = Time.time + idlePhase;

        // ── pick wander targets within the room ──
        if (!hasWanderTarget || wanderWait <= 0f)
        {
            wanderTarget = PickWanderPoint();
            wanderWait = Random.Range(1.5f, 4f);
            hasWanderTarget = true;
        }

        Vector3 pos = transform.position;
        Vector3 toTarget = wanderTarget - pos;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist < 0.1f)
        {
            // Close enough — wait then pick next
            wanderWait -= Time.deltaTime;
        }
        else
        {
            // Fly toward target
            float speed = 0.6f * idleBlend;
            Vector3 move = toTarget.normalized * Mathf.Min(speed * Time.deltaTime, dist);
            pos += move;

            // Clamp to hex room boundary so drone can't drift through walls
            Vector3 center = RoomWorldPos(CurrentRoom);
            if (map.RoomSizeMap.TryGetValue(CurrentRoom, out var size))
            {
                float roomR = map.RoomRadius(size);
                pos = ClampToHex(pos, center, roomR * 0.9f);
            }

            transform.position = pos;
        }

        // ── yaw toward movement direction + wobble ──
        float yawTarget = 0f;
        if (dist > 0.15f)
            yawTarget = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        else
            yawTarget = Mathf.Sin(t * 0.4f) * 18f + Mathf.Sin(t * 0.17f) * 10f;

        float pitch = Mathf.Sin(t * 0.7f) * 3f;
        float roll  = Mathf.Cos(t * 0.53f) * 2.5f;

        // Lean into movement direction
        if (dist > 0.15f)
            pitch += 5f;

        Quaternion desired = Quaternion.Euler(
            pitch * idleBlend,
            yawTarget,
            roll * idleBlend);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * 4f);
    }

    Vector3 PickWanderPoint()
    {
        Vector3 center = RoomWorldPos(CurrentRoom);
        if (!map.RoomSizeMap.TryGetValue(CurrentRoom, out var roomSize))
            return center;
        float roomR = map.RoomRadius(roomSize);
        float maxR = roomR * 0.45f;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt(Random.Range(0f, 1f)) * maxR;
        return new Vector3(
            center.x + Mathf.Cos(angle) * r,
            center.y,
            center.z + Mathf.Sin(angle) * r);
    }

    /// <summary>
    /// Clamp a world position to stay inside a flat-top hex of given radius.
    /// Tests against all 6 hex edge half-planes; pushes inward if outside any.
    /// </summary>
    Vector3 ClampToHex(Vector3 pos, Vector3 center, float hexR)
    {
        float dx = pos.x - center.x;
        float dz = pos.z - center.z;

        // Inner radius (apothem) = distance from center to edge midpoint
        float apothem = hexR * 0.866025f; // cos(30°)

        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * 60f * i;
            // Outward normal of edge i
            float nx = Mathf.Cos(a);
            float nz = Mathf.Sin(a);
            float dot = dx * nx + dz * nz;
            if (dot > apothem)
            {
                float push = dot - apothem;
                dx -= nx * push;
                dz -= nz * push;
            }
        }

        return new Vector3(center.x + dx, pos.y, center.z + dz);
    }

    // ── helpers ──────────────────────────────

    float GetTravelTime(Vector2Int a, Vector2Int b)
    {
        foreach (var (ca, cb, type) in map.ConnectionList)
        {
            if ((ca == a && cb == b) || (ca == b && cb == a))
            {
                switch (type)
                {
                    case PassageType.Corridor: return 2f;
                    case PassageType.Duct:     return 4f;
                    case PassageType.Vent:     return 6f;
                }
            }
        }
        return 2f;
    }

    PassageType GetPassageType(Vector2Int a, Vector2Int b)
    {
        foreach (var (ca, cb, type) in map.ConnectionList)
            if ((ca == a && cb == b) || (ca == b && cb == a))
                return type;
        return PassageType.Corridor;
    }

    static string PassageLabel(PassageType type)
    {
        switch (type)
        {
            case PassageType.Corridor: return "CORRIDOR";
            case PassageType.Duct:     return "DUCT";
            case PassageType.Vent:     return "VENT";
            default:                                    return "TRAVEL";
        }
    }

    Vector3 RoomWorldPos(Vector2Int room)
    {
        Vector3 c = map.HexCenter(room);
        return new Vector3(c.x, hoverY, c.z);
    }

    float SmoothStep(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// Evaluate position along the multi-waypoint hop path at normalized time t (0–1).
    /// Distributes t proportionally across segment lengths so speed stays uniform.
    /// </summary>
    Vector3 EvalHopPath(float t)
    {
        if (hopPoints.Count < 2 || hopTotalDist < 0.001f)
            return hopPoints.Count > 0 ? hopPoints[hopPoints.Count - 1] : transform.position;

        float d = t * hopTotalDist;
        for (int i = 1; i < hopPoints.Count; i++)
        {
            if (d <= hopCumulDist[i] || i == hopPoints.Count - 1)
            {
                float segLen = hopCumulDist[i] - hopCumulDist[i - 1];
                float segT = segLen > 0.001f ? (d - hopCumulDist[i - 1]) / segLen : 0f;
                return Vector3.Lerp(hopPoints[i - 1], hopPoints[i], segT);
            }
        }
        return hopPoints[hopPoints.Count - 1];
    }

    // ── floor path line ─────────────────────

    void UpdatePathLine()
    {
        bool hasPath = journeyWaypoints.Count > 1 && journeyIdx >= 0;

        // Hide when no journey or on a scan-only step with no remaining travel
        if (!hasPath || (journeyIdx < journeyPlan.Count && journeyPlan[journeyIdx].isScan))
        {
            if (pathLineGO != null) pathLineGO.SetActive(false);
            return;
        }

        EnsurePathLine();
        pathLineGO.SetActive(true);

        // Brightness: selected = bright, unselected = dim
        float alpha = IsSelected ? 0.55f : 0.2f;
        Color col = new Color(0f, 0.85f, 1f, alpha);
        pathMat.color = col;
        pathMat.SetColor("_BaseColor", col);

        // Compute how much of the polyline the drone has consumed
        // Hop k occupies waypoints k*3 .. (k+1)*3  (origin is at index 0)
        float consumed = 0f;
        int hopIdx = journeyIdx; // which travel hop we're on
        int hopEndWP = hopIdx * 3 + 3;
        if (hopEndWP < journeyCumulDist.Count)
        {
            float hopStart = journeyCumulDist[hopIdx * 3];
            float hopEnd   = journeyCumulDist[hopEndWP];
            consumed = hopStart + travelProgress * (hopEnd - hopStart);
        }
        else
        {
            consumed = journeyCumulDist[journeyCumulDist.Count - 1];
        }

        BuildDashedRibbon(consumed);
    }

    void EnsurePathLine()
    {
        if (pathLineGO != null) return;

        pathLineGO = new GameObject("PathLine_" + DroneName);
        pathMF = pathLineGO.AddComponent<MeshFilter>();
        pathMR = pathLineGO.AddComponent<MeshRenderer>();

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        pathMat = new Material(sh);
        Color col = new Color(0f, 0.85f, 1f, 0.3f);
        pathMat.color = col;
        pathMat.SetColor("_BaseColor", col);
        pathMat.SetFloat("_Surface", 1f);
        pathMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        pathMat.SetOverrideTag("RenderType", "Transparent");
        pathMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        pathMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        pathMat.SetInt("_ZWrite", 0);
        pathMat.SetFloat("_Cull", 0f); // double-sided
        pathMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
        pathMR.sharedMaterial = pathMat;
        pathMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        pathMesh = new Mesh { name = "PathDashed" };
        pathMF.sharedMesh = pathMesh;
    }

    void BuildDashedRibbon(float consumedDist)
    {
        BuildDashedRibbonInto(pathMesh, journeyWaypoints, journeyCumulDist, consumedDist);
    }

    /// <summary>
    /// Build a dashed ribbon mesh into the given mesh from waypoints.
    /// Dashes are anchored to world-space positions; consumedDist clips
    /// the front so the line is eaten as the drone advances.
    /// </summary>
    void BuildDashedRibbonInto(Mesh targetMesh, List<Vector3> waypoints, List<float> cumulDist, float consumedDist)
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
            float hw = pathWidth * 0.5f;

            float local = 0f;
            while (local < segLen - 0.001f)
            {
                float worldDist = segStart + local;
                float phase = worldDist % dashCycle;

                if (phase < dashLen)
                {
                    float dashRemain = dashLen - phase;
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
                    float gapRemain = dashCycle - phase;
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

    // ── screen-space step bars ────────────────

    static void EnsureStepBarCanvas()
    {
        if (stepBarCanvas != null) return;

        var go = new GameObject("StepBarCanvas");
        Object.DontDestroyOnLoad(go);
        stepBarCanvas = go.AddComponent<Canvas>();
        stepBarCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        stepBarCanvas.sortingOrder = 5;

        stepBarScaler = go.AddComponent<CanvasScaler>();
        stepBarScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        stepBarScaler.referenceResolution = new Vector2(1920, 1080);
        stepBarScaler.matchWidthOrHeight = 0.5f;
    }

    void BuildWorldStepBars(List<Vector2Int> newPath)
    {
        DestroyWorldStepBars();
        if (newPath == null || newPath.Count == 0) return;

        EnsureStepBarCanvas();

        Vector2Int prev = CurrentRoom;
        int stepIdx = 0;

        foreach (var room in newPath)
        {
            // Travel step bar at passage midpoint (slightly elevated for projection)
            var (midA, midB) = map.PassageEndpoints(prev, room);
            Vector3 passageMid = (midA + midB) * 0.5f;
            CreateStepBar(new Vector3(passageMid.x, 0.5f, passageMid.z), stepIdx);
            stepIdx++;
            prev = room;
        }

        // Scan step bar at destination room center
        var finalTile = fog?.GetTile(newPath[newPath.Count - 1]);
        if (finalTile != null && finalTile.State == FogState.Unknown)
        {
            Vector3 rc = map.HexCenter(newPath[newPath.Count - 1]);
            CreateStepBar(new Vector3(rc.x, 0.5f, rc.z), stepIdx);
        }
    }

    void CreateStepBar(Vector3 worldPos, int idx)
    {
        EnsureStepBarCanvas();

        var bar = new WorldStepBar();
        bar.worldPos = worldPos;

        // Root container
        bar.root = new GameObject($"StepBar_{DroneName}_{idx}");
        bar.root.transform.SetParent(stepBarCanvas.transform, false);
        bar.rect = bar.root.AddComponent<RectTransform>();
        bar.rect.sizeDelta = new Vector2(barUIWidth, barUIHeight + 18f);

        // Background bar
        var bgGO = new GameObject("Bg");
        bgGO.transform.SetParent(bar.root.transform, false);
        bar.bgImage = bgGO.AddComponent<Image>();
        bar.bgImage.color = new Color(0.02f, 0.04f, 0.08f, 0.88f);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0);
        bgRT.anchorMax = new Vector2(1, 0);
        bgRT.pivot = new Vector2(0.5f, 0);
        bgRT.offsetMin = new Vector2(0, 0);
        bgRT.offsetMax = new Vector2(0, barUIHeight);

        // Fill bar
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(bgGO.transform, false);
        bar.fillImage = fillGO.AddComponent<Image>();
        bar.fillImage.color = new Color(0f, 0.85f, 1f, 0.9f);
        bar.fillRect = fillGO.GetComponent<RectTransform>();
        bar.fillRect.anchorMin = new Vector2(0, 0);
        bar.fillRect.anchorMax = new Vector2(0, 1);
        bar.fillRect.pivot = new Vector2(0, 0.5f);
        float inset = 2f;
        bar.fillRect.offsetMin = new Vector2(inset, inset);
        bar.fillRect.offsetMax = new Vector2(inset, -inset);

        // Label text (step name — above the bar)
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(bar.root.transform, false);
        bar.labelText = labelGO.AddComponent<Text>();
        bar.labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (bar.labelText.font == null)
            bar.labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        bar.labelText.fontSize = 14;
        bar.labelText.fontStyle = FontStyle.Bold;
        bar.labelText.alignment = TextAnchor.LowerCenter;
        bar.labelText.color = new Color(0f, 0.85f, 1f, 1f);
        bar.labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bar.labelText.verticalOverflow = VerticalWrapMode.Overflow;
        var lblRT = labelGO.GetComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0, 1);
        lblRT.anchorMax = new Vector2(1, 1);
        lblRT.pivot = new Vector2(0.5f, 0);
        lblRT.offsetMin = new Vector2(0, 0);
        lblRT.offsetMax = new Vector2(0, 16f);

        // Time text (elapsed/total — inside the bar)
        var timeGO = new GameObject("Time");
        timeGO.transform.SetParent(bgGO.transform, false);
        bar.timeText = timeGO.AddComponent<Text>();
        bar.timeText.font = bar.labelText.font;
        bar.timeText.fontSize = 12;
        bar.timeText.fontStyle = FontStyle.Bold;
        bar.timeText.alignment = TextAnchor.MiddleCenter;
        bar.timeText.color = Color.white;
        bar.timeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var timeRT = timeGO.GetComponent<RectTransform>();
        timeRT.anchorMin = Vector2.zero;
        timeRT.anchorMax = Vector2.one;
        timeRT.offsetMin = Vector2.zero;
        timeRT.offsetMax = Vector2.zero;

        // Outline for pop
        var outline = bgGO.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0.6f, 0.9f, 0.5f);
        outline.effectDistance = new Vector2(1, -1);

        worldStepBars.Add(bar);
    }

    void UpdateWorldStepBars()
    {
        Camera cam = Camera.main;

        for (int i = 0; i < worldStepBars.Count; i++)
        {
            var bar = worldStepBars[i];
            if (bar.root == null) continue;

            bool active = journeyIdx >= 0 && i < journeyPlan.Count;
            bar.root.SetActive(active);
            if (!active) continue;

            // Project world position to screen, then to canvas
            if (cam != null)
            {
                Vector3 screen = cam.WorldToScreenPoint(bar.worldPos);
                if (screen.z > 0)
                {
                    // Convert screen coords to canvas coords
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        stepBarCanvas.transform as RectTransform, screen, null, out var canvasPos);
                    bar.rect.localPosition = canvasPos;
                    bar.root.SetActive(true);
                }
                else
                {
                    bar.root.SetActive(false);
                    continue;
                }
            }

            float progress = GetJourneyStepProgress(i);
            float elapsed = GetJourneyStepElapsed(i);
            float total = journeyPlan[i].duration;
            string lbl = journeyPlan[i].label;

            bool isDone = i < journeyIdx;
            bool isActive = i == journeyIdx;

            // Fill width
            if (bar.fillRect != null)
            {
                float fillW = (barUIWidth - 4f) * Mathf.Clamp01(progress);
                bar.fillRect.offsetMax = new Vector2(2f + fillW, -2f);
            }

            // Colors
            Color fillCol, labelCol;
            if (isDone)
            {
                fillCol = new Color(0.15f, 0.55f, 0.15f, 0.85f);
                labelCol = new Color(0.3f, 0.75f, 0.3f, 0.7f);
            }
            else if (isActive)
            {
                fillCol = new Color(0f, 0.85f, 1f, 0.9f);
                labelCol = new Color(0f, 0.85f, 1f, 1f);
            }
            else
            {
                fillCol = new Color(0.25f, 0.35f, 0.45f, 0.55f);
                labelCol = new Color(0.5f, 0.6f, 0.7f, 0.6f);
            }

            if (bar.fillImage != null) bar.fillImage.color = fillCol;
            if (bar.bgImage != null)
                bar.bgImage.color = isActive
                    ? new Color(0.02f, 0.04f, 0.08f, 0.88f)
                    : new Color(0.02f, 0.04f, 0.08f, 0.55f);

            // Label with energy cost
            if (bar.labelText != null)
            {
                int cost = journeyPlan[i].energyCost;
                string costTag = cost > 0 ? $" ⚡{cost}" : "";
                bar.labelText.text = isDone ? $"✓ {lbl}" : $"{lbl}{costTag}";
                bar.labelText.color = labelCol;
            }

            // Time
            if (bar.timeText != null)
            {
                if (isDone)
                    bar.timeText.text = "";
                else if (isActive)
                    bar.timeText.text = $"{elapsed:F1}s / {total:F1}s";
                else
                    bar.timeText.text = $"{total:F1}s";
                bar.timeText.color = Color.white;
            }
        }
    }

    void DestroyWorldStepBars()
    {
        foreach (var bar in worldStepBars)
        {
            if (bar.root != null) Destroy(bar.root);
        }
        worldStepBars.Clear();
    }

    // ── preview path helpers ─────────────────

    void EnsurePreviewLine()
    {
        if (previewLineGO != null) return;

        previewLineGO = new GameObject("PreviewLine_" + DroneName);
        previewMF = previewLineGO.AddComponent<MeshFilter>();
        previewMR = previewLineGO.AddComponent<MeshRenderer>();

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        previewMat = new Material(sh);
        Color col = new Color(1f, 0.75f, 0f, 0.4f);
        previewMat.color = col;
        previewMat.SetColor("_BaseColor", col);
        previewMat.SetFloat("_Surface", 1f);
        previewMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        previewMat.SetOverrideTag("RenderType", "Transparent");
        previewMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        previewMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        previewMat.SetInt("_ZWrite", 0);
        previewMat.SetFloat("_Cull", 0f);
        previewMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
        previewMR.sharedMaterial = previewMat;
        previewMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        previewMesh = new Mesh { name = "PreviewDashed" };
        previewMF.sharedMesh = previewMesh;
    }

    void CreatePreviewStepBar(Vector3 worldPos, int idx, bool overBudget = false)
    {
        EnsureStepBarCanvas();

        Color barBg   = overBudget ? new Color(0.12f, 0.02f, 0.02f, 0.80f) : new Color(0.08f, 0.06f, 0.01f, 0.75f);
        Color barFill = overBudget ? new Color(1f, 0.15f, 0.10f, 0.50f)    : new Color(1f, 0.75f, 0f, 0.45f);
        Color lblCol  = overBudget ? new Color(1f, 0.30f, 0.25f, 1f)       : new Color(1f, 0.85f, 0.3f, 1f);
        Color outCol  = overBudget ? new Color(1f, 0.20f, 0.15f, 0.6f)     : new Color(1f, 0.75f, 0f, 0.5f);

        var bar = new WorldStepBar();
        bar.worldPos = worldPos;

        bar.root = new GameObject($"PreviewBar_{DroneName}_{idx}");
        bar.root.transform.SetParent(stepBarCanvas.transform, false);
        bar.rect = bar.root.AddComponent<RectTransform>();
        bar.rect.sizeDelta = new Vector2(barUIWidth, barUIHeight + 18f);

        // Background
        var bgGO = new GameObject("Bg");
        bgGO.transform.SetParent(bar.root.transform, false);
        bar.bgImage = bgGO.AddComponent<Image>();
        bar.bgImage.color = barBg;
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0);
        bgRT.anchorMax = new Vector2(1, 0);
        bgRT.pivot = new Vector2(0.5f, 0);
        bgRT.offsetMin = new Vector2(0, 0);
        bgRT.offsetMax = new Vector2(0, barUIHeight);

        // Fill (full width = total duration preview)
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(bgGO.transform, false);
        bar.fillImage = fillGO.AddComponent<Image>();
        bar.fillImage.color = barFill;
        bar.fillRect = fillGO.GetComponent<RectTransform>();
        bar.fillRect.anchorMin = new Vector2(0, 0);
        bar.fillRect.anchorMax = new Vector2(1, 1);
        bar.fillRect.offsetMin = new Vector2(2, 2);
        bar.fillRect.offsetMax = new Vector2(-2, -2);

        // Label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(bar.root.transform, false);
        bar.labelText = labelGO.AddComponent<Text>();
        bar.labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (bar.labelText.font == null)
            bar.labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        bar.labelText.fontSize = 14;
        bar.labelText.fontStyle = FontStyle.Bold;
        bar.labelText.alignment = TextAnchor.LowerCenter;
        bar.labelText.color = lblCol;
        bar.labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bar.labelText.verticalOverflow = VerticalWrapMode.Overflow;
        var lblRT = labelGO.GetComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0, 1);
        lblRT.anchorMax = new Vector2(1, 1);
        lblRT.pivot = new Vector2(0.5f, 0);
        lblRT.offsetMin = new Vector2(0, 0);
        lblRT.offsetMax = new Vector2(0, 16f);

        // Time inside bar
        var timeGO = new GameObject("Time");
        timeGO.transform.SetParent(bgGO.transform, false);
        bar.timeText = timeGO.AddComponent<Text>();
        bar.timeText.font = bar.labelText.font;
        bar.timeText.fontSize = 12;
        bar.timeText.fontStyle = FontStyle.Bold;
        bar.timeText.alignment = TextAnchor.MiddleCenter;
        bar.timeText.color = Color.white;
        bar.timeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var timeRT = timeGO.GetComponent<RectTransform>();
        timeRT.anchorMin = Vector2.zero;
        timeRT.anchorMax = Vector2.one;
        timeRT.offsetMin = Vector2.zero;
        timeRT.offsetMax = Vector2.zero;

        // Outline
        var outline = bgGO.AddComponent<Outline>();
        outline.effectColor = outCol;
        outline.effectDistance = new Vector2(1, -1);

        // Set text content from preview plan
        if (idx < previewPlan.Count)
        {
            int cost = previewPlan[idx].energyCost;
            string costTag = cost > 0 ? $" ⚡{cost}" : "";
            bar.labelText.text = $"{previewPlan[idx].label}{costTag}";
            bar.timeText.text = $"{previewPlan[idx].duration:F1}s";
        }

        previewStepBars.Add(bar);
    }

    void UpdatePreviewStepBars()
    {
        if (!isShowingPreview) return;

        Camera mainCam = Camera.main;
        for (int i = 0; i < previewStepBars.Count; i++)
        {
            var bar = previewStepBars[i];
            if (bar.root == null) continue;

            if (mainCam != null)
            {
                Vector3 screen = mainCam.WorldToScreenPoint(bar.worldPos);
                if (screen.z > 0)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        stepBarCanvas.transform as RectTransform, screen, null, out var canvasPos);
                    bar.rect.localPosition = canvasPos;
                    bar.root.SetActive(true);
                }
                else
                {
                    bar.root.SetActive(false);
                }
            }
        }
    }

    void DestroyPreviewStepBars()
    {
        foreach (var bar in previewStepBars)
        {
            if (bar.root != null) Destroy(bar.root);
        }
        previewStepBars.Clear();
    }

    void OnDestroy()
    {
        if (pathLineGO != null) Destroy(pathLineGO);
        if (previewLineGO != null) Destroy(previewLineGO);
        DestroyWorldStepBars();
        DestroyPreviewStepBars();
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
        Color col = new Color(0f, 0.85f, 1f, 0.8f);
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
