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
    public event Action<DroneController, Vector2Int> OnRoomChanged;

    enum State { Idle, RoomNavigating, WallAnimating }
    State state = State.Idle;

    MapView map;
    FogOfWar fog;
    float hoverY = 1f;
    IDroneVisual droneVisual;
    SelectionRing selectionRing;
    RoutePreview routePreview;

    /// <summary>Consistent drone movement speed in world units/second (from model).</summary>
    float DroneSpeed => Model.FullSpeed;

    Color JourneyLineColor => Palette.WithAlpha(Palette.JourneyLine, IsSelected ? 0.55f : 0.2f);
    Color PreviewLineColor => PreviewExceedsEnergy
        ? Palette.WithAlpha(Palette.OverBudgetLine, 0.5f)
        : Palette.WithAlpha(Palette.PreviewLine, 0.4f);

    // Active journey
    DroneJourney activeJourney;
    public DroneJourney ActiveJourney => activeJourney;

    // Journey steps for UI (one per wall crossing + optional goal interaction)
    readonly List<JourneyStep> journeySteps = new List<JourneyStep>();
    readonly List<StepAnchor> journeyAnchors = new List<StepAnchor>();
    int journeyCurrentIndex = -1;
    float stepStartTime;
    float journeyStartTime;

    // Last completed interaction (for IsRefitting / repeat logic)
    WallInteractionConfig lastCompletedInteraction;
    WallView activeInteractionWall;

    public void Init(MapView mapGen, FogOfWar fogOfWar, Vector2Int startRoom, string droneName = "Drone", int droneIndex = 0, DroneType droneType = DroneType.Scout)
    {
        map = mapGen;
        fog = fogOfWar;
        DroneIndex = droneIndex;
        CurrentRoom = startRoom;

        Model = new DroneModel
        {
            Name = droneName,
            Type = droneType,
            BaseEnergy = 10,
            CurrentEnergy = 10,
            CurrentRoom = startRoom,
        };
        Model.InitSlots();
        hoverY = Model.TravelHeight;

        var tile = fog.GetTile(startRoom);
        transform.position = new Vector3(tile.Center.x, hoverY, tile.Center.z);

        tile.OnDroneEnter(this);
        routePreview = new RoutePreview(this, map, fog);
    }

    void Start()
    {
        droneVisual = GetComponentInChildren<LowPolyDrone>() as IDroneVisual
                   ?? GetComponentInChildren<HaulerDrone>() as IDroneVisual;
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
        if (goalWall?.Model != null)
            walls.Add(goalWall.Model);
        var path = new DronePath(walls, Model);
        activeJourney = new DroneJourney(path);
        previewPath = null;
        
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
        activeInteractionWall = wall;
        currentTile.ShowLine(transform.position, parkPoint, JourneyLineColor);
        if (wall.Model is StationWallModel stationA) stationA.OccupiedBy = Model;
        currentTile.NavigateDrone(transform, parkPoint, DurationForDistance(dist), () =>
        {
            // Draw power before starting first cycle
            if (wall.Model is StationWallModel stationPow && !stationPow.TryDrawPower(cfg))
            {
                stationPow.OccupiedBy = null;
                activeInteractionWall = null;
                state = State.Idle;
                return;
            }
            stepStartTime = Time.time;
            state = State.WallAnimating;
            wall.PlayInteraction(transform, cfg.BaseDuration, cfg, () =>
            {
                OnInteractionComplete(cfg, wall);
            });
        });
    }

    public void Cancel()
    {
        if (activeInteractionWall != null && activeInteractionWall.Model is StationWallModel stationC)
            stationC.OccupiedBy = null;
        activeInteractionWall = null;
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
        journeyAnchors.Clear();
        var walls = activeJourney.Walls;
        Vector2Int prev = CurrentRoom;
        for (int i = 0; i < walls.Count; i++)
        {
            var wall = walls[i];
            bool isLast = i == walls.Count - 1;
            var interactions = wall.GetInteractions(Model);
            var targetCoord = wall.Neighbor?.Owner?.Coord ?? prev;

            // Anchor at the passage midpoint between prev and target
            Vector3 anchorPos = map.WallMidpoint(prev, wall.EdgeIndex,
                fog?.GetTile(prev)?.RModel?.Size ?? RoomSize.Small);

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
                journeyAnchors.Add(new StepAnchor
                {
                    worldPos = anchorPos,
                    roomA = prev,
                    roomB = targetCoord,
                    layer = 0,
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
                journeyAnchors.Add(new StepAnchor
                {
                    worldPos = anchorPos,
                    roomA = prev,
                    roomB = targetCoord,
                    layer = 0,
                });

                // If this leads to a room where an auto-activate item triggers, add step+anchor
                var autoGear = Model.GetEquipped<IAutoActivateGear>();
                if (autoGear != null)
                {
                    var destTile = fog?.GetTile(targetCoord);
                    if (destTile != null && autoGear.IsEligible(destTile.RModel))
                    {
                        journeySteps.Add(new JourneyStep
                        {
                            label = autoGear.StepLabel,
                            duration = autoGear.GetDuration(destTile.RModel),
                            isScan = true,
                            energyCost = autoGear.ActivationEnergyCost,
                        });
                        Vector3 roomCenter = map.HexCenter(targetCoord);
                        journeyAnchors.Add(new StepAnchor
                        {
                            worldPos = new Vector3(roomCenter.x, 0.5f, roomCenter.z),
                            roomA = targetCoord,
                            roomB = targetCoord,
                            layer = 1,
                        });
                    }
                }

                prev = targetCoord;
            }
        }
        journeyCurrentIndex = 0;
        stepStartTime = -1f; // not started yet
        journeyStartTime = Time.time;
    }

    void AdvanceJourneyStep()
    {
        journeyCurrentIndex++;
        stepStartTime = -1f; // will be set when actual traversal/interaction starts
    }

    void ClearJourneySteps()
    {
        journeySteps.Clear();
        journeyAnchors.Clear();
        journeyCurrentIndex = -1;
    }

    // ── Preview ────────────────────────

    DronePath previewPath;
    List<JourneyStep> cachedPreviewSteps;
    List<StepAnchor> cachedPreviewAnchors;

    public void ShowPreview(PreviewRequest req)
    {
        var walls = req.path != null && req.path.Count > 0
            ? BuildWallList(req.path)
            : new List<WallModel>();

        // Append terminal wall if provided and not already the last in path
        if (req.wall?.Model != null && (walls.Count == 0 || walls[walls.Count - 1] != req.wall.Model))
            walls.Add(req.wall.Model);

        if (walls.Count == 0) { ClearPreviewPath(); return; }

        previewPath = new DronePath(walls, Model);
        cachedPreviewAnchors = new List<StepAnchor>();
        cachedPreviewSteps = BuildStepsFromWalls(walls, cachedPreviewAnchors);
        routePreview?.ShowPreview(req);
    }

    public void ClearPreviewPath()
    {
        previewPath = null;
        cachedPreviewSteps = null;
        cachedPreviewAnchors = null;
        routePreview?.ClearPreview();
    }

    List<JourneyStep> BuildStepsFromWalls(List<WallModel> walls, List<StepAnchor> anchors)
    {
        var steps = new List<JourneyStep>();
        Vector2Int prev = CurrentRoom;
        for (int i = 0; i < walls.Count; i++)
        {
            var wall = walls[i];
            bool isLast = i == walls.Count - 1;
            var interactions = wall.GetInteractions(Model);
            var targetCoord = wall.Neighbor?.Owner?.Coord ?? prev;

            Vector3 anchorPos = map.WallMidpoint(prev, wall.EdgeIndex,
                fog?.GetTile(prev)?.RModel?.Size ?? RoomSize.Small);

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
                anchors.Add(new StepAnchor
                {
                    worldPos = anchorPos,
                    roomA = prev,
                    roomB = targetCoord,
                    layer = 0,
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
                anchors.Add(new StepAnchor
                {
                    worldPos = anchorPos,
                    roomA = prev,
                    roomB = targetCoord,
                    layer = 0,
                });

                // If traversal leads to a room where auto-activate item triggers, add step
                var autoItem = Model.GetEquipped<IAutoActivateGear>();
                if (autoItem != null && wall.Neighbor?.Owner != null)
                {
                    var destTile = fog?.GetTile(wall.Neighbor.Owner.Coord);
                    if (destTile != null && autoItem.IsEligible(destTile.RModel))
                    {
                        steps.Add(new JourneyStep
                        {
                            label = autoItem.StepLabel,
                            duration = autoItem.GetDuration(destTile.RModel),
                            isScan = true,
                            energyCost = autoItem.ActivationEnergyCost,
                        });
                        Vector3 roomCenter = map.HexCenter(targetCoord);
                        anchors.Add(new StepAnchor
                        {
                            worldPos = new Vector3(roomCenter.x, 0.5f, roomCenter.z),
                            roomA = targetCoord,
                            roomB = targetCoord,
                            layer = 1,
                        });
                    }
                }

                prev = targetCoord;
            }
        }
        return steps;
    }

    // ── Hop execution ────────────────────────

    float DurationForDistance(float dist) => Mathf.Max(0.08f, dist / DroneSpeed);

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
        wall.BeforeTraversal();
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
        float approachDist = Vector3.Distance(transform.position, parkPoint);

        var capturedTarget = targetRoom;
        var capturedDeparture = departure;
        var capturedArrival = arrival;
        var pass = wall.GetPassability(Model);
        float totalDur = pass.Duration;

        // Split duration proportionally to actual distances
        Vector3 depMid = departure.transform.position;
        depMid.y = hoverY;
        Vector3 arrPark = capturedArrival != null ? capturedArrival.DroneParkPoint : depMid;
        arrPark.y = hoverY;
        float depDist = departure.GetTraversalDistance(parkPoint);
        float arrDist = capturedArrival != null ? Vector3.Distance(capturedArrival.transform.position, arrPark) : depDist;
        float totalDist = depDist + arrDist;
        float depDur = totalDist > 0.01f ? totalDur * (depDist / totalDist) : totalDur * 0.5f;
        float arrDur = totalDist > 0.01f ? totalDur * (arrDist / totalDist) : totalDur * 0.5f;

        state = State.RoomNavigating;
        Color lineCol = JourneyLineColor;
        currentTile.ShowLine(transform.position, parkPoint, lineCol);
        currentTile.NavigateDrone(transform, parkPoint, DurationForDistance(approachDist), () =>
        {
            stepStartTime = Time.time;
            state = State.WallAnimating;
            capturedDeparture.ShowLine(parkPoint, depMid, lineCol);
            capturedDeparture.PlayTraversal(transform, depDur, true, () =>
            {
                if (capturedArrival != null)
                {
                    capturedArrival.ShowLine(depMid, arrPark, lineCol);
                    capturedArrival.PlayTraversal(transform, arrDur, false, () =>
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
        var passage = currentTile.GetWallView(wallModel.EdgeIndex);

        if (passage == null) { activeJourney = null; ClearJourneySteps(); state = State.Idle; return; }

        Vector3 parkPoint = passage.DroneParkPoint;
        parkPoint.y = hoverY;
        float dist = Vector3.Distance(transform.position, parkPoint);

        state = State.RoomNavigating;
        activeInteractionWall = passage;
        if (wallModel is StationWallModel stationB) stationB.OccupiedBy = Model;
        Color lineCol = JourneyLineColor;
        currentTile.ShowLine(transform.position, parkPoint, lineCol);
        currentTile.NavigateDrone(transform, parkPoint, DurationForDistance(dist), () =>
        {
            stepStartTime = Time.time;
            state = State.WallAnimating;
            passage.PlayInteraction(transform, cfg.BaseDuration, cfg, () =>
            {
                OnInteractionComplete(cfg, passage);
            });
        });
    }

    void OnHopComplete(Vector2Int newRoom)
    {
        int hopIdx = activeJourney.CurrentHopIndex;
        var wall = activeJourney.Walls[hopIdx];

        // Room transition
        var oldTile = fog?.GetTile(CurrentRoom);
        oldTile?.OnDroneExit(this);
        CurrentRoom = newRoom;
        Model.CurrentRoom = newRoom;
        var newTile = fog?.GetTile(CurrentRoom);
        newTile?.OnDroneEnter(this);
        OnRoomChanged?.Invoke(this, newRoom);

        // Energy — use the wall's cost
        int cost = wall.GetPassability(Model).EnergyCost;
        Model.CurrentEnergy = Mathf.Max(0, Model.CurrentEnergy - cost);

        // Advance journey + UI step + route line segment
        activeJourney.AdvanceHop();
        AdvanceJourneyStep();

        // Check if auto-activate item should trigger
        var activeGear = Model.GetEquipped<IAutoActivateGear>();
        bool needsActivation = activeGear != null && newTile != null && activeGear.IsEligible(newTile.RModel);
        bool hasMoreHops = activeJourney.CurrentHopIndex < activeJourney.Walls.Count;

        if (needsActivation || !hasMoreHops)
        {
            // Navigate to room center
            Vector3 center = new Vector3(newTile.Center.x, hoverY, newTile.Center.z);
            float dist = Vector3.Distance(transform.position, center);

            state = State.RoomNavigating;
            newTile.ShowLine(transform.position, center, JourneyLineColor);
            newTile.NavigateDrone(transform, center, DurationForDistance(dist), () =>
            {
                if (needsActivation)
                {
                    newTile.OnDroneArrived(true);
                    StartAutoActivation(newTile, activeGear);
                }
                else
                {
                    newTile.OnDroneArrived(false);
                    StartNextHop();
                }
            });
        }
        else
        {
            // More hops ahead — straight line to next departure park point
            newTile.OnDroneArrived(false);
            var nextWall = activeJourney.Walls[activeJourney.CurrentHopIndex];

            // Next wall is an interaction (station/rubble) — no traversal, go to center
            if (nextWall.Neighbor == null)
            {
                StartNextHop();
                return;
            }

            var nextTargetRoom = nextWall.Neighbor.Owner.Coord;
            var nextDeparture = fog?.GetTile(CurrentRoom)?.GetPassage(nextTargetRoom);

            if (nextDeparture == null) { StartNextHop(); return; }

            Vector3 parkPoint = nextDeparture.DroneParkPoint;
            parkPoint.y = hoverY;
            float dist = Vector3.Distance(transform.position, parkPoint);

            state = State.RoomNavigating;
            newTile.ShowLine(transform.position, parkPoint, JourneyLineColor);
            newTile.NavigateDrone(transform, parkPoint, DurationForDistance(dist), () =>
            {
                StartNextHopFromPark(nextWall, nextTargetRoom, nextDeparture);
            });
        }
    }

    /// <summary>Start next hop when drone is already at the departure park point.</summary>
    void StartNextHopFromPark(WallModel wall, Vector2Int targetRoom, WallView departure)
    {
        if (activeJourney == null) { state = State.Idle; return; }

        bool isLast = activeJourney.CurrentHopIndex == activeJourney.Walls.Count - 1;
        var interactions = wall.GetInteractions(Model);

        if (isLast && interactions.Count > 0)
        {
            StartLastWallInteraction(wall, interactions[0]);
            return;
        }

        var arrival = fog?.GetTile(targetRoom)?.GetPassage(CurrentRoom);
        var pass = wall.GetPassability(Model);
        float totalDur = pass.Duration;

        Vector3 parkPos = transform.position;
        Vector3 depMid = departure.transform.position;
        depMid.y = hoverY;
        Vector3 arrPark = arrival != null ? arrival.DroneParkPoint : depMid;
        arrPark.y = hoverY;
        float depDist = departure.GetTraversalDistance(parkPos);
        float arrDist = arrival != null ? Vector3.Distance(arrival.transform.position, arrPark) : depDist;
        float totalDist = depDist + arrDist;
        float depDur = totalDist > 0.01f ? totalDur * (depDist / totalDist) : totalDur * 0.5f;
        float arrDur = totalDist > 0.01f ? totalDur * (arrDist / totalDist) : totalDur * 0.5f;

        stepStartTime = Time.time;
        state = State.WallAnimating;
        Color lineCol = JourneyLineColor;
        departure.ShowLine(parkPos, depMid, lineCol);
        departure.PlayTraversal(transform, depDur, true, () =>
        {
            if (arrival != null)
            {
                arrival.ShowLine(depMid, arrPark, lineCol);
                arrival.PlayTraversal(transform, arrDur, false, () =>
                {
                    OnHopComplete(targetRoom);
                });
            }
            else
            {
                OnHopComplete(targetRoom);
            }
        });
    }

    void StartAutoActivation(RoomTile tile, IAutoActivateGear gear)
    {
        stepStartTime = Time.time;
        state = State.WallAnimating;

        // Wait for scan/activation to complete
        Action handler = null;
        handler = () =>
        {
            tile.RModel.OnScanComplete -= handler;
            OnAutoActivationComplete(tile, gear);
        };
        tile.RModel.OnScanComplete += handler;
    }

    void OnAutoActivationComplete(RoomTile tile, IAutoActivateGear gear)
    {
        Model.CurrentEnergy = Mathf.Max(0, Model.CurrentEnergy - gear.ActivationEnergyCost);
        gear.OnActivationComplete(tile.RModel);
        AdvanceJourneyStep();
        StartNextHop();
    }


    void OnInteractionComplete(WallInteractionConfig cfg, WallView wall)
    {
        Model.CurrentEnergy = Mathf.Max(0, Model.CurrentEnergy - cfg.EnergyCost);
        Model.CurrentEnergy = Mathf.Min(Model.MaxEnergy, Model.CurrentEnergy + cfg.EnergyGainPerCycle);
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

        // Award cargo if applicable
        if (cfg.LootItem != null && Model.HasFreeSlot(cfg.LootItem.Size))
        {
            Model.Equip(cfg.LootItem);
            if (cfg.LootItem.Size == SlotSize.Large) OnCargoPickedUp();
            // Hide the cache
            if (wall != null) wall.gameObject.SetActive(false);
        }
        else if (cfg.CargoReward != CargoType.None && cfg.LootItem == null && Model.HasFreeSlot(SlotSize.Large))
        {
            Model.Equip(GearCatalog.FuelCell);
            OnCargoPickedUp();
            if (wall != null) wall.gameObject.SetActive(false);
        }

        // Scan loot cache
        if (cfg.Label == "SCAN" && wall != null)
        {
            var cache = wall.GetComponent<LootCache>();
            if (cache != null) cache.OnScanned();
        }

        // Repeat if the config says so (e.g. charging until full)
        if (cfg.RepeatCondition != null && cfg.RepeatCondition(Model))
        {
            // Draw power for next cycle — stop if insufficient
            if (wall.Model is StationWallModel stationPow2 && !stationPow2.TryDrawPower(cfg))
            {
                // Power ran out — end interaction
            }
            else
            {
                stepStartTime = Time.time;
                wall.PlayInteraction(transform, cfg.BaseDuration, cfg, () =>
                {
                    OnInteractionComplete(cfg, wall);
                });
                return;
            }
        }

        activeJourney?.AdvanceHop();
        activeJourney = null;
        ClearJourneySteps();
        routePreview?.ClearJourney();
        if (wall.Model is StationWallModel stationD) stationD.OccupiedBy = null;
        activeInteractionWall = null;
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

    // ── Cargo visual ────────────────────

    GameObject cargoVisual;

    void OnCargoPickedUp()
    {
        if (cargoVisual != null) return;
        var hauler = GetComponentInChildren<HaulerDrone>();
        if (hauler == null) return;

        // Build a small crate in the cargo bay
        cargoVisual = new GameObject("CargoItem");
        cargoVisual.transform.SetParent(hauler.transform, false);
        // Position in cargo bay (top of chassis, slightly back)
        cargoVisual.transform.localPosition = new Vector3(0, 0.12f, -0.05f);
        cargoVisual.transform.localScale = Vector3.one;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        var matBody = new Material(sh) { color = new Color(0.25f, 0.22f, 0.15f) };
        Color glowCol = new Color(1f, 0.6f, 0.05f);
        var matGlow = new Material(sh) { color = glowCol };
        matGlow.EnableKeyword("_EMISSION");
        matGlow.SetColor("_EmissionColor", glowCol * 4f);

        LootBarrelMesh.Build(cargoVisual.transform, matBody, matBody, matGlow);
    }

    public void ClearCargoVisual()
    {
        if (cargoVisual != null) { Destroy(cargoVisual); cargoVisual = null; }
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

    public bool IsRefitting => lastCompletedInteraction != null && lastCompletedInteraction.EnablesRefit;
    public bool IsSelling => lastCompletedInteraction != null && lastCompletedInteraction.EnablesSell;

    public struct JourneyStep
    {
        public string label;
        public float duration;
        public bool isScan;
        public bool isInteraction;
        public WallInteractionConfig interactionConfig;
        public int energyCost;
    }

    /// <summary>Unified preview request — path + optional terminal wall.</summary>
    public struct PreviewRequest
    {
        public List<Vector2Int> path;   // rooms to traverse (null/empty if already at target)
        public WallView wall;           // terminal wall (station, rubble, etc.)
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

    public float PreviewTotalTime => cachedPreviewSteps != null
        ? SumDuration(cachedPreviewSteps) : 0f;
    public bool IsShowingPreview => previewPath != null;
    public int JourneyEnergyCost => activeJourney?.RemainingEnergyCost ?? 0;
    public int PreviewEnergyCost => cachedPreviewSteps != null
        ? SumEnergy(cachedPreviewSteps) : 0;
    public bool PreviewExceedsEnergy => cachedPreviewSteps != null
        && SumEnergy(cachedPreviewSteps) > (Model.CurrentEnergy - JourneyEnergyCost);

    static float SumDuration(List<JourneyStep> steps)
    {
        float t = 0; foreach (var s in steps) t += s.duration; return t;
    }
    static int SumEnergy(List<JourneyStep> steps)
    {
        int e = 0; foreach (var s in steps) e += s.energyCost; return e;
    }

    public float GetJourneyStepProgress(int i)
    {
        if (i != journeyCurrentIndex || journeyCurrentIndex < 0) return i < journeyCurrentIndex ? 1f : 0f;
        if (stepStartTime < 0f) return 0f; // not started yet
        float dur = journeySteps[i].duration;
        if (dur <= 0) return 1f;
        return Mathf.Clamp01((Time.time - stepStartTime) / dur);
    }

    public float GetJourneyStepElapsed(int i)
    {
        if (i != journeyCurrentIndex || journeyCurrentIndex < 0) return 0f;
        if (stepStartTime < 0f) return 0f;
        return Time.time - stepStartTime;
    }

    public IReadOnlyList<JourneyStep> PreviewJourney => cachedPreviewSteps;
    public IReadOnlyList<StepAnchor> JourneyAnchors => journeyAnchors;
    public IReadOnlyList<StepAnchor> PreviewAnchors => cachedPreviewAnchors;
}
