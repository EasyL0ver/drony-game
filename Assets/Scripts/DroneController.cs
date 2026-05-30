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

    // Active journey
    DroneJourney activeJourney;
    public DroneJourney ActiveJourney => activeJourney;

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
    }

    // ── Movement API ────────────────────────

    /// <summary>Set a travel path. Drone moves room-by-room via walls.</summary>
    public void SetPath(List<Vector2Int> rooms, WallView goalWall = null)
    {
        if (rooms == null || rooms.Count == 0) return;
        if (state != State.Idle) Cancel();

        WallInteractionConfig interaction = null;
        if (goalWall?.Model != null)
        {
            var interactions = goalWall.Model.GetInteractions(Model);
            if (interactions.Count > 0 && !interactions[0].BlocksPassage)
                interaction = interactions[0];
        }

        activeJourney = new DroneJourney(rooms, interaction);
        StartNextHop();
    }

    /// <summary>Set a path to perform a blocking wall interaction (rubble clear, bomb).</summary>
    public void SetPathToWallInteraction(List<Vector2Int> path, Vector2Int connA, Vector2Int connB)
    {
        if (state != State.Idle) Cancel();

        var wi = map?.Model?.GetWallInteraction(connA, connB, Model);
        if (wi == null) return;

        var rooms = path ?? new List<Vector2Int>();
        activeJourney = new DroneJourney(rooms, wi, connA, connB);
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

        var currentTile = fog.GetTile(CurrentRoom);
        Vector3 parkPoint = wall.DroneParkPoint;
        parkPoint.y = hoverY;
        float dist = Vector3.Distance(transform.position, parkPoint);

        state = State.RoomNavigating;
        currentTile.NavigateDrone(transform, parkPoint, Mathf.Max(0.2f, dist * 0.6f), () =>
        {
            state = State.WallAnimating;
            wall.PlayInteraction(transform, cfg.BaseDuration, cfg, () =>
            {
                OnInteractionComplete(cfg, wall);
            });
        });
    }

    public void Cancel()
    {
        // TODO: cancel active coroutines on room/wall
        state = State.Idle;
        activeJourney = null;
    }

    // ── Preview stubs (no-op for now) ────────────

    public void ShowPreviewPath(List<Vector2Int> path, WallView wall = null) { }
    public void ShowStationPreview(RoomTile tile, WallView wall) { }
    public void ShowWallInteractionPreview(Vector2Int approach, Vector2Int other, WallInteractionConfig wi) { }
    public void ClearPreviewPath() { }

    // ── Hop execution ────────────────────────

    void StartNextHop()
    {
        if (activeJourney == null) { state = State.Idle; return; }

        // All travel hops done — do goal interaction if any
        if (activeJourney.CurrentHopIndex >= activeJourney.Rooms.Count)
        {
            if (activeJourney.GoalInteraction != null && !activeJourney.GoalDone)
                StartGoalInteraction();
            else
            {
                activeJourney = null;
                state = State.Idle;
            }
            return;
        }

        var targetRoom = activeJourney.Rooms[activeJourney.CurrentHopIndex];
        var departure = fog?.GetTile(CurrentRoom)?.GetPassage(targetRoom);
        var arrival = fog?.GetTile(targetRoom)?.GetPassage(CurrentRoom);

        if (departure == null)
        {
            // Can't traverse — abort
            activeJourney = null;
            state = State.Idle;
            return;
        }

        // Navigate to departure passage
        var currentTile = fog.GetTile(CurrentRoom);
        Vector3 parkPoint = departure.DroneParkPoint;
        parkPoint.y = hoverY;
        float dist = Vector3.Distance(transform.position, parkPoint);
        float approachTime = Mathf.Max(0.15f, dist * 0.5f);

        var capturedTarget = targetRoom;
        var capturedDeparture = departure;
        var capturedArrival = arrival;
        float traversalDuration = MapModel.TravelTime(departure.Type);

        state = State.RoomNavigating;
        currentTile.NavigateDrone(transform, parkPoint, approachTime, () =>
        {
            // Departure half
            state = State.WallAnimating;
            float halfDur = traversalDuration * 0.5f;
            capturedDeparture.PlayTraversal(transform, halfDur, true, () =>
            {
                // Arrival half
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

    void OnHopComplete(Vector2Int newRoom)
    {
        // Room transition
        var oldTile = fog?.GetTile(CurrentRoom);
        oldTile?.OnDroneExit(this);
        CurrentRoom = newRoom;
        var newTile = fog?.GetTile(CurrentRoom);
        newTile?.OnDroneEnter(this);

        // Energy
        Model.CurrentEnergy = Mathf.Max(0, Model.CurrentEnergy - 1);

        // Navigate to room center
        Vector3 center = new Vector3(newTile.Center.x, hoverY, newTile.Center.z);
        float dist = Vector3.Distance(transform.position, center);

        activeJourney.AdvanceHop();
        state = State.RoomNavigating;
        newTile.NavigateDrone(transform, center, Mathf.Max(0.15f, dist * 0.5f), () =>
        {
            StartNextHop();
        });
    }

    void StartGoalInteraction()
    {
        var cfg = activeJourney.GoalInteraction;
        var currentTile = fog.GetTile(CurrentRoom);

        if (activeJourney.IsBlockingWallInteraction)
        {
            // Blocking wall interaction (rubble/bomb) — approach the passage
            var connA = activeJourney.WallConnA;
            var connB = activeJourney.WallConnB;
            var passage = currentTile.GetPassage(connA == CurrentRoom ? connB : connA);

            if (passage == null) { activeJourney = null; state = State.Idle; return; }

            Vector3 parkPoint = passage.DroneParkPoint;
            parkPoint.y = hoverY;
            float dist = Vector3.Distance(transform.position, parkPoint);

            state = State.RoomNavigating;
            currentTile.NavigateDrone(transform, parkPoint, Mathf.Max(0.2f, dist * 0.5f), () =>
            {
                state = State.WallAnimating;
                passage.PlayInteraction(transform, cfg.BaseDuration, cfg, () =>
                {
                    OnBlockingInteractionComplete(cfg, connA, connB);
                });
            });
        }
        else
        {
            // Non-blocking interaction at destination wall — find the wall
            WallView wall = FindWallWithInteraction(currentTile, cfg);
            if (wall == null) { activeJourney = null; state = State.Idle; return; }

            Vector3 parkPoint = wall.DroneParkPoint;
            parkPoint.y = hoverY;
            float dist = Vector3.Distance(transform.position, parkPoint);

            state = State.RoomNavigating;
            currentTile.NavigateDrone(transform, parkPoint, Mathf.Max(0.2f, dist * 0.5f), () =>
            {
                state = State.WallAnimating;
                wall.PlayInteraction(transform, cfg.BaseDuration, cfg, () =>
                {
                    OnInteractionComplete(cfg, wall);
                });
            });
        }
    }

    void OnInteractionComplete(WallInteractionConfig cfg, WallView wall)
    {
        Model.CurrentEnergy = Mathf.Max(0, Model.CurrentEnergy - cfg.EnergyCost);

        if (cfg.DestroysDrone)
        {
            Explode();
            return;
        }

        if (activeJourney != null) activeJourney.MarkGoalDone();
        activeJourney = null;
        state = State.Idle;
    }

    void OnBlockingInteractionComplete(WallInteractionConfig cfg, Vector2Int connA, Vector2Int connB)
    {
        Model.CurrentEnergy = Mathf.Max(0, Model.CurrentEnergy - cfg.EnergyCost);
        map?.Model?.CompleteWallInteraction(connA, connB);
        OnWallInteractionCompleted?.Invoke(connA, connB);

        if (cfg.DestroysDrone)
        {
            Explode();
            return;
        }

        if (activeJourney != null) activeJourney.MarkGoalDone();
        activeJourney = null;
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

    WallView FindWallWithInteraction(RoomTile tile, WallInteractionConfig cfg)
    {
        foreach (var w in tile.GetComponentsInChildren<WallView>())
        {
            if (w.Model == null) continue;
            if (w.Model.GetInteractions(Model).Contains(cfg)) return w;
        }
        return null;
    }

    // ── Compat stubs (for UI that hasn't been rewritten yet) ────

    /// <summary>Stub: old API. Use StartInteraction instead.</summary>
    public void StartStationAction(RoomTile tile, WallView wall) => StartInteraction(tile, wall);

    public bool IsPerformingStationAction => IsPerformingInteraction;
    public bool IsRefitting => false;

    public struct JourneyStep
    {
        public string label;
        public float duration;
        public bool isScan;
        public bool isInteraction;
        public WallInteractionConfig interactionConfig;
        public int energyCost;
    }

    public IReadOnlyList<JourneyStep> Journey => EmptyJourney;
    static readonly List<JourneyStep> EmptyJourney = new List<JourneyStep>();
    public int JourneyCurrentIndex => -1;
    public float JourneyTotalTime => 0f;
    public float JourneyElapsedTime => 0f;
    public float JourneyOverallProgress => 0f;
    public float PreviewTotalTime => 0f;
    public bool IsShowingPreview => false;
    public int JourneyEnergyCost => activeJourney?.RemainingEnergyCost ?? 0;
    public int PreviewEnergyCost => 0;
    public bool PreviewExceedsEnergy => false;
    public float GetJourneyStepProgress(int i) => 0f;
    public float GetJourneyStepElapsed(int i) => 0f;
    public IReadOnlyList<JourneyStep> PreviewJourney => null;
    public IReadOnlyList<StepAnchor> JourneyAnchors => null;
    public IReadOnlyList<StepAnchor> PreviewAnchors => null;
    internal int MoveSegIdx => 0;
    internal float MoveSegT => 0f;

    internal static void BuildDashedRibbonInto(Mesh m, List<Vector3> w, List<float> c, float d, float width, float dash, float gap)
        => DashedRibbon.Build(m, w, c, d, width, dash, gap);
}
