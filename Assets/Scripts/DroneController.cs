using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Minimal drone controller. State machine chains room navigation and wall animations.
/// Room owns in-room movement. Wall owns through-wall movement.
/// </summary>
public class DroneController : MonoBehaviour
{
    public DroneModel Model { get; private set; }
    public Vector2Int CurrentRoom { get; private set; }
    public bool IsSelected { get; set; }
    public int DroneIndex { get; private set; }
    public bool IsMoving => state != State.Idle;
    public bool IsPerformingInteraction => state == State.WallAnimating;

    // Events
    public event Action<Vector2Int, Vector2Int> OnWallInteractionCompleted;
    public event Action<DroneController> OnDroneDestroyed;

    enum State { Idle, RoomNavigating, WallAnimating }
    State state = State.Idle;

    HexMapGenerator map;
    FogOfWar fog;
    float hoverY = 1f;
    LowPolyDrone droneVisual;
    SelectionRing selectionRing;
    RoutePreview routePreview;

    // Active journey
    DroneJourney activeJourney;
    public DroneJourney ActiveJourney => activeJourney;

    // Journey steps for UI (one per wall crossing + optional goal interaction)
    readonly List<JourneyStep> journeySteps = new List<JourneyStep>();
    int journeyCurrentIndex = -1;
    float stepStartTime;
    float journeyStartTime;

    // Route line segment tracking
    int moveSegIdx;
    float moveSegT;

    // Last completed interaction (for IsRefitting)
    WallInteractionConfig lastCompletedInteraction;

    public void Init(HexMapGenerator mapGen, FogOfWar fogOfWar, Vector2Int startRoom, string droneName = "Drone", int droneIndex = 0)
    {
        map = mapGen;
        fog = fogOfWar;
        DroneIndex = droneIndex;
        CurrentRoom = startRoom;

        Model = new DroneModel
        {
            Name = droneName,
            MaxEnergy = 10,
            CurrentEnergy = 10,
        };
        Model.InitSlots();

        var tile = fog.GetTile(startRoom);
        transform.position = new Vector3(tile.Center.x, hoverY, tile.Center.z);

        tile.OnDroneEnter(this);
        routePreview = new RoutePreview(this, map, fog);
    }

    void Start()
    {
        droneVisual = GetComponentInChildren<LowPolyDrone>();
        selectionRing = gameObject.AddComponent<SelectionRing>();
        selectionRing.Init(hoverY);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        selectionRing?.SetVisible(IsSelected);
        if (IsSelected) selectionRing?.UpdatePulse();
        UpdateGlow();
        routePreview?.Update();
    }

    void OnDestroy()
    {
        routePreview?.Destroy();
    }

    // ── Movement API ────────────────────────

    /// <summary>Set a travel path. Drone moves room-by-room via walls.</summary>
    public void SetPath(List<Vector2Int> rooms, WallView goalWall = null)
    {
        if (rooms == null || rooms.Count == 0) return;
        if (state != State.Idle) Cancel();

        lastCompletedInteraction = null;
        var walls = BuildWallList(rooms);
        var path = new DronePath(walls, Model);
        activeJourney = new DroneJourney(path);
        previewPath = null;
        moveSegIdx = 0; moveSegT = 0f;
        routePreview?.ClearPreview();
        routePreview?.SetJourney(rooms, goalWall);
        BuildJourneySteps();
        StartNextHop();
    }

    /// <summary>Commit the current preview path and start traversing.</summary>
    public void CommitPreview()
    {
        if (previewPath == null) return;
        if (state != State.Idle) Cancel();

        lastCompletedInteraction = null;
        activeJourney = new DroneJourney(previewPath);
        previewPath = null;
        cachedPreviewSteps = null;
        moveSegIdx = 0; moveSegT = 0f;
        var rooms = RoomsFromWalls(activeJourney.Walls);
        routePreview?.ClearPreview();
        routePreview?.SetJourney(rooms);
        BuildJourneySteps();
        StartNextHop();
    }

    /// <summary>Set a path to perform a blocking wall interaction (rubble clear, bomb).</summary>
    public void SetPathToWallInteraction(List<Vector2Int> pathRooms, Vector2Int connA, Vector2Int connB)
    {
        if (state != State.Idle) Cancel();

        lastCompletedInteraction = null;
        var rooms = pathRooms ?? new List<Vector2Int>();
        var walls = BuildWallList(rooms);
        var targetWall = fog?.GetTile(connA)?.GetPassage(connB)?.Model;
        if (targetWall == null) targetWall = fog?.GetTile(connB)?.GetPassage(connA)?.Model;
        if (targetWall != null) walls.Add(targetWall);

        var path = new DronePath(walls, Model);
        activeJourney = new DroneJourney(path);
        previewPath = null;
        cachedPreviewSteps = null;
        moveSegIdx = 0; moveSegT = 0f;
        routePreview?.ClearPreview();
        routePreview?.SetJourney(rooms);
        BuildJourneySteps();
        StartNextHop();
    }

    /// <summary>Start interaction on a wall in the current room (drone already there).</summary>
    public void StartInteraction(RoomTile tile, WallView wall)
    {
        if (tile == null || wall == null) return;
        if (state != State.Idle) return;
        if (CurrentRoom != tile.Coord) return;

        var interactions = wall.Model?.GetInteractions(Model);
        if (interactions == null || interactions.Count == 0) return;
        var cfg = interactions[0];

        // Build single-step journey for UI
        journeySteps.Clear();
        journeySteps.Add(new JourneyStep
        {
            label = cfg.Label,
            duration = cfg.BaseDuration,
            isInteraction = true,
            interactionConfig = cfg,
            energyCost = cfg.EnergyCost,
        });
        journeyCurrentIndex = 0;
        journeyStartTime = Time.time;
        stepStartTime = Time.time;

        var currentTile = fog.GetTile(CurrentRoom);
        Vector3 parkPoint = wall.DroneParkPoint;
        parkPoint.y = hoverY;
        float dist = Vector3.Distance(transform.position, parkPoint);

        state = State.RoomNavigating;
        currentTile.NavigateDrone(transform, parkPoint, Mathf.Max(0.2f, dist * 0.6f), () =>
        {
            stepStartTime = Time.time; // reset when actual interaction starts
            state = State.WallAnimating;
            wall.PlayInteraction(transform, cfg.BaseDuration, cfg, () =>
            {
                OnInteractionComplete(cfg, wall);
            });
        });
    }

    public void Cancel()
    {
        state = State.Idle;
        activeJourney = null;
        ClearJourneySteps();
        routePreview?.ClearJourney();
    }

    void BuildWallList(List<Vector2Int> rooms, List<WallModel> result)
    {
        result.Clear();
        Vector2Int prev = CurrentRoom;
        foreach (var room in rooms)
        {
            var passage = fog?.GetTile(prev)?.GetPassage(room);
            if (passage?.Model != null)
                result.Add(passage.Model);
            prev = room;
        }
    }

    List<WallModel> BuildWallList(List<Vector2Int> rooms)
    {
        var result = new List<WallModel>();
        BuildWallList(rooms, result);
        return result;
    }

    List<Vector2Int> RoomsFromWalls(IReadOnlyList<WallModel> walls)
    {
        var rooms = new List<Vector2Int>();
        foreach (var wall in walls)
        {
            if (wall.Neighbor?.Owner != null)
                rooms.Add(wall.Neighbor.Owner.Coord);
        }
        return rooms;
    }

    void BuildJourneySteps()
    {
        journeySteps.Clear();
        var walls = activeJourney.Walls;
        for (int i = 0; i < walls.Count; i++)
        {
            var wall = walls[i];
            bool isLast = i == walls.Count - 1;
            var interactions = wall.GetInteractions(Model);

            if (isLast && interactions.Count > 0)
            {
                var cfg = interactions[0];
                journeySteps.Add(new JourneyStep
                {
                    label = cfg.Label,
                    duration = cfg.BaseDuration,
                    isInteraction = true,
                    interactionConfig = cfg,
                    energyCost = cfg.EnergyCost,
                });
            }
            else
            {
                var pass = wall.GetPassability(Model);
                journeySteps.Add(new JourneyStep
                {
                    label = pass.Label,
                    duration = pass.Duration,
                    energyCost = pass.EnergyCost,
                });
            }
        }
        journeyCurrentIndex = 0;
        stepStartTime = Time.time;
        journeyStartTime = Time.time;
    }

    void AdvanceJourneyStep()
    {
        journeyCurrentIndex++;
        stepStartTime = Time.time;
    }

    void ClearJourneySteps()
    {
        journeySteps.Clear();
        journeyCurrentIndex = -1;
    }

    // ── Preview ────────────────────────

    DronePath previewPath;
    List<JourneyStep> cachedPreviewSteps;

    public void ShowPreviewPath(List<Vector2Int> path, WallView wall = null)
    {
        if (path == null || path.Count == 0) { ClearPreviewPath(); return; }
        var walls = BuildWallList(path);
        previewPath = new DronePath(walls, Model);
        cachedPreviewSteps = BuildStepsFromWalls(walls);
        routePreview?.ShowPath(path, wall);
    }

    public void ShowStationPreview(RoomTile tile, WallView wall)
    {
        if (tile == null || wall?.Model == null) { ClearPreviewPath(); return; }
        var walls = new List<WallModel> { wall.Model };
        previewPath = new DronePath(walls, Model);
        cachedPreviewSteps = BuildStepsFromWalls(walls);
        routePreview?.ShowStation(tile, wall);
    }

    public void ShowWallInteractionPreview(Vector2Int approach, Vector2Int other, WallInteractionConfig wi)
    {
        var wallModel = fog?.GetTile(approach)?.GetPassage(other)?.Model;
        if (wallModel == null) { ClearPreviewPath(); return; }
        var walls = new List<WallModel> { wallModel };
        previewPath = new DronePath(walls, Model);
        cachedPreviewSteps = BuildStepsFromWalls(walls);
        routePreview?.ShowWallInteraction(approach, other, wi);
    }

    public void ClearPreviewPath()
    {
        previewPath = null;
        cachedPreviewSteps = null;
        routePreview?.ClearPreview();
    }

    List<JourneyStep> BuildStepsFromWalls(List<WallModel> walls)
    {
        var steps = new List<JourneyStep>();
        for (int i = 0; i < walls.Count; i++)
        {
            var wall = walls[i];
            bool isLast = i == walls.Count - 1;
            var interactions = wall.GetInteractions(Model);
            if (isLast && interactions.Count > 0)
            {
                var cfg = interactions[0];
                steps.Add(new JourneyStep
                {
                    label = cfg.Label,
                    duration = cfg.BaseDuration,
                    isInteraction = true,
                    interactionConfig = cfg,
                    energyCost = cfg.EnergyCost,
                });
            }
            else
            {
                var pass = wall.GetPassability(Model);
                steps.Add(new JourneyStep
                {
                    label = pass.Label,
                    duration = pass.Duration,
                    energyCost = pass.EnergyCost,
                });
            }
        }
        return steps;
    }

    // ── Hop execution ────────────────────────

    void StartNextHop()
    {
        if (activeJourney == null) { state = State.Idle; return; }

        // All walls done
        if (activeJourney.CurrentHopIndex >= activeJourney.Walls.Count)
        {
            activeJourney = null;
            ClearJourneySteps();
            state = State.Idle;
            return;
        }

        var wall = activeJourney.Walls[activeJourney.CurrentHopIndex];
        bool isLast = activeJourney.CurrentHopIndex == activeJourney.Walls.Count - 1;
        var interactions = wall.GetInteractions(Model);

        // Last wall with interaction → do interaction (not traversal)
        if (isLast && interactions.Count > 0)
        {
            StartLastWallInteraction(wall, interactions[0]);
            return;
        }

        // Normal traversal
        var targetRoom = wall.Neighbor.Owner.Coord;
        var departure = fog?.GetTile(CurrentRoom)?.GetPassage(targetRoom);
        var arrival = fog?.GetTile(targetRoom)?.GetPassage(CurrentRoom);

        if (departure == null)
        {
            activeJourney = null;
            ClearJourneySteps();
            state = State.Idle;
            return;
        }

        var currentTile = fog.GetTile(CurrentRoom);
        Vector3 parkPoint = departure.DroneParkPoint;
        parkPoint.y = hoverY;
        float dist = Vector3.Distance(transform.position, parkPoint);
        float approachTime = Mathf.Max(0.15f, dist * 0.5f);

        var capturedTarget = targetRoom;
        var capturedDeparture = departure;
        var capturedArrival = arrival;
        var pass = wall.GetPassability(Model);
        float traversalDuration = pass.Duration;

        state = State.RoomNavigating;
        currentTile.NavigateDrone(transform, parkPoint, approachTime, () =>
        {
            state = State.WallAnimating;
            float halfDur = traversalDuration * 0.5f;
            capturedDeparture.PlayTraversal(transform, halfDur, true, () =>
            {
                if (capturedArrival != null)
                {
                    capturedArrival.PlayTraversal(transform, halfDur, false, () =>
                    {
                        OnHopComplete(capturedTarget);
                    });
                }
                else
                {
                    OnHopComplete(capturedTarget);
                }
            });
        });
    }

    void StartLastWallInteraction(WallModel wallModel, WallInteractionConfig cfg)
    {
        var currentTile = fog.GetTile(CurrentRoom);
        // Find the passage view for this wall
        var targetCoord = wallModel.Neighbor.Owner.Coord;
        var passage = currentTile.GetPassage(targetCoord);

        if (passage == null) { activeJourney = null; ClearJourneySteps(); state = State.Idle; return; }

        Vector3 parkPoint = passage.DroneParkPoint;
        parkPoint.y = hoverY;
        float dist = Vector3.Distance(transform.position, parkPoint);

        state = State.RoomNavigating;
        currentTile.NavigateDrone(transform, parkPoint, Mathf.Max(0.2f, dist * 0.5f), () =>
        {
            state = State.WallAnimating;
            passage.PlayInteraction(transform, cfg.BaseDuration, cfg, () =>
            {
                OnInteractionComplete(cfg, passage);
            });
        });
    }

    void OnHopComplete(Vector2Int newRoom)
    {
        // Room transition
        var oldTile = fog?.GetTile(CurrentRoom);
        oldTile?.OnDroneExit(this);
        CurrentRoom = newRoom;
        var newTile = fog?.GetTile(CurrentRoom);
        newTile?.OnDroneEnter(this);

        // Energy — use the wall's cost
        int hopIdx = activeJourney.CurrentHopIndex;
        int cost = activeJourney.Walls[hopIdx].GetPassability(Model).EnergyCost;
        Model.CurrentEnergy = Mathf.Max(0, Model.CurrentEnergy - cost);

        // Advance journey + UI step + route line segment
        activeJourney.AdvanceHop();
        AdvanceJourneyStep();
        moveSegIdx += 3; // passA + passB + roomCenter
        moveSegT = 0f;

        // Navigate to room center
        Vector3 center = new Vector3(newTile.Center.x, hoverY, newTile.Center.z);
        float dist = Vector3.Distance(transform.position, center);

        state = State.RoomNavigating;
        newTile.NavigateDrone(transform, center, Mathf.Max(0.15f, dist * 0.5f), () =>
        {
            StartNextHop();
        });
    }


    void OnInteractionComplete(WallInteractionConfig cfg, WallView wall)
    {
        Model.CurrentEnergy = Mathf.Max(0, Model.CurrentEnergy - cfg.EnergyCost);
        lastCompletedInteraction = cfg;

        // Notify if this was a blocking wall interaction
        if (cfg.BlocksPassage)
        {
            var wallModel = wall.Model;
            if (wallModel?.Owner != null && wallModel?.Neighbor?.Owner != null)
            {
                var connA = wallModel.Owner.Coord;
                var connB = wallModel.Neighbor.Owner.Coord;
                map?.Model?.CompleteWallInteraction(connA, connB);
                OnWallInteractionCompleted?.Invoke(connA, connB);
            }
        }

        if (cfg.DestroysDrone)
        {
            Explode();
            return;
        }

        activeJourney?.AdvanceHop();
        activeJourney = null;
        ClearJourneySteps();
        routePreview?.ClearJourney();
        state = State.Idle;
    }

    void Explode()
    {
        ExplosionVFX.Spawn(transform.position);
        var tile = fog?.GetTile(CurrentRoom);
        tile?.OnDroneExit(this);
        OnDroneDestroyed?.Invoke(this);
        Destroy(gameObject);
    }

    // ── Visuals ────────────────────────

    void UpdateGlow()
    {
        if (droneVisual == null || droneVisual.GlowMaterial == null) return;
        float baseInt = droneVisual.BaseGlowIntensity;
        Color col;
        if (Model.CurrentEnergy <= 0) col = Palette.DroneDepleted;
        else if (IsMoving) col = Palette.DroneMoving;
        else if (IsSelected) col = Palette.DroneSelected;
        else col = Palette.DroneIdle;

        droneVisual.GlowMaterial.color = col;
        float boost = IsSelected ? 1.5f + 0.5f * Mathf.Sin(Time.time * 3f) : 1f;
        droneVisual.GlowMaterial.SetColor("_EmissionColor", col * baseInt * boost);
    }

    // ── Compat stubs (for UI that hasn't been rewritten yet) ────

    /// <summary>Stub: old API. Use StartInteraction instead.</summary>
    public void StartStationAction(RoomTile tile, WallView wall) => StartInteraction(tile, wall);

    public bool IsPerformingStationAction => IsPerformingInteraction;
    public bool IsRefitting => lastCompletedInteraction != null && lastCompletedInteraction.EnablesRefit;

    public struct JourneyStep
    {
        public string label;
        public float duration;
        public bool isScan;
        public bool isInteraction;
        public WallInteractionConfig interactionConfig;
        public int energyCost;
    }

    public IReadOnlyList<JourneyStep> Journey => journeySteps;
    public int JourneyCurrentIndex => journeyCurrentIndex;

    public float JourneyTotalTime
    {
        get { float t = 0; foreach (var s in journeySteps) t += s.duration; return t; }
    }

    public float JourneyElapsedTime => journeyCurrentIndex >= 0 ? Time.time - journeyStartTime : 0f;

    public float JourneyOverallProgress
    {
        get
        {
            float total = JourneyTotalTime;
            if (total <= 0) return 0f;
            return Mathf.Clamp01(JourneyElapsedTime / total);
        }
    }

    public float PreviewTotalTime => previewPath?.TotalTime ?? 0f;
    public bool IsShowingPreview => previewPath != null;
    public int JourneyEnergyCost => activeJourney?.RemainingEnergyCost ?? 0;
    public int PreviewEnergyCost => previewPath?.TotalEnergyCost ?? 0;
    public bool PreviewExceedsEnergy => previewPath != null
        && previewPath.TotalEnergyCost > (Model.CurrentEnergy - JourneyEnergyCost);

    public float GetJourneyStepProgress(int i)
    {
        if (i != journeyCurrentIndex || journeyCurrentIndex < 0) return i < journeyCurrentIndex ? 1f : 0f;
        float dur = journeySteps[i].duration;
        if (dur <= 0) return 1f;
        return Mathf.Clamp01((Time.time - stepStartTime) / dur);
    }

    public float GetJourneyStepElapsed(int i)
    {
        if (i != journeyCurrentIndex || journeyCurrentIndex < 0) return 0f;
        return Time.time - stepStartTime;
    }

    public IReadOnlyList<JourneyStep> PreviewJourney => cachedPreviewSteps;
    public IReadOnlyList<StepAnchor> JourneyAnchors => routePreview?.JourneyAnchors;
    public IReadOnlyList<StepAnchor> PreviewAnchors => routePreview?.PreviewAnchors;
    internal int MoveSegIdx => moveSegIdx;
    internal float MoveSegT => moveSegT;

    internal static void BuildDashedRibbonInto(Mesh m, List<Vector3> w, List<float> c, float d, float width, float dash, float gap)
        => DashedRibbon.Build(m, w, c, d, width, dash, gap);
}
