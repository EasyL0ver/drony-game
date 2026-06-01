using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages all route visualization for a single drone:
/// journey path line + journey step bars (active move) and
/// preview path line + preview step bars (hover).
/// Owned by DroneController.
/// </summary>
public class RoutePreview
{
    readonly DroneController drone;
    readonly HexMapGenerator map;
    readonly FogOfWar fog;

    // ── journey ────────────────────────────
    readonly List<StepAnchor> journeyAnchors = new List<StepAnchor>();

    // ── preview (hover) ──────────────────────
    bool isShowing;
    readonly List<StepAnchor> previewAnchors = new List<StepAnchor>();

    public RoutePreview(DroneController drone, HexMapGenerator map, FogOfWar fog)
    {
        this.drone = drone;
        this.map = map;
        this.fog = fog;
    }

    // ── preview public state ────────────────

    public bool IsShowing => isShowing;
    public IReadOnlyList<DroneController.JourneyStep> Plan => drone.PreviewJourney;
    public float TotalTime => drone.PreviewTotalTime;
    public int EnergyCost => drone.PreviewEnergyCost;
    public bool ExceedsEnergy => drone.PreviewExceedsEnergy;

    public IReadOnlyList<StepAnchor> JourneyAnchors => journeyAnchors;
    public IReadOnlyList<StepAnchor> PreviewAnchors => previewAnchors;

    // ── journey (active move) ───────────────

    readonly List<WallView> journeyWalls = new List<WallView>();
    readonly List<RoomTile> journeyRoomLines = new List<RoomTile>();

    public void SetJourney(List<Vector2Int> path, WallView station = null)
    {
        journeyAnchors.Clear();
        ClearJourneyLines();

        if (path == null || path.Count == 0) return;

        Color col = Palette.WithAlpha(Palette.JourneyLine, 0.55f);
        Vector2Int prev = drone.CurrentRoom;

        // First room segment: drone → first departure
        var firstDep = fog?.GetTile(prev)?.GetPassage(path[0]);
        if (firstDep != null)
        {
            var roomTile = fog?.GetTile(prev);
            if (roomTile != null)
            {
                roomTile.ShowLine(drone.transform.position, firstDep.DroneParkPoint, col);
                journeyRoomLines.Add(roomTile);
            }
        }

        for (int i = 0; i < path.Count; i++)
        {
            var room = path[i];

            // Corridor segment
            var dep = fog?.GetTile(prev)?.GetPassage(room);
            var arr = fog?.GetTile(room)?.GetPassage(prev);
            if (dep != null && arr != null)
            {
                dep.ShowLine(dep.DroneParkPoint, arr.DroneParkPoint, col);
                journeyWalls.Add(dep);
            }
            else if (dep != null)
            {
                dep.ShowLine(dep.DroneParkPoint, dep.transform.position, col);
                journeyWalls.Add(dep);
            }

            // Room-internal segment to next departure or terminal
            if (i < path.Count - 1)
            {
                var nextRoom = path[i + 1];
                var nextDep = fog?.GetTile(room)?.GetPassage(nextRoom);
                var roomTile = fog?.GetTile(room);
                if (arr != null && nextDep != null && roomTile != null)
                {
                    roomTile.ShowLine(arr.DroneParkPoint, nextDep.DroneParkPoint, col);
                    journeyRoomLines.Add(roomTile);
                }
            }
            else if (station != null)
            {
                var roomTile = fog?.GetTile(room);
                if (arr != null && roomTile != null)
                {
                    roomTile.ShowLine(arr.DroneParkPoint, station.DroneParkPoint, col);
                    journeyRoomLines.Add(roomTile);
                }
            }

            // Anchors
            Vector3 pA = dep != null ? dep.DroneParkPoint : map.HexCenter(prev);
            Vector3 pB = arr != null ? arr.DroneParkPoint : map.HexCenter(room);
            journeyAnchors.Add(new StepAnchor
            {
                worldPos = (pA + pB) * 0.5f,
                roomA = prev,
                roomB = room,
                layer = 0,
            });

            prev = room;
        }

        // Extra anchors for scan/interaction at destination
        var destCoord = path[path.Count - 1];
        var journey = drone.Journey;
        int stepIdx = path.Count;
        float destBarY = 0.5f;
        int destLayer = 0;
        while (stepIdx < journey.Count)
        {
            Vector3 barPos = station != null
                ? station.DroneParkPoint
                : map.HexCenter(destCoord);
            journeyAnchors.Add(new StepAnchor
            {
                worldPos = new Vector3(barPos.x, destBarY, barPos.z),
                roomA = destCoord,
                roomB = destCoord,
                layer = destLayer,
            });
            stepIdx++;
            destBarY += 0.8f;
            destLayer++;
        }
    }

    public void SetStationJourney(Vector3 parkPos)
    {
        journeyAnchors.Clear();
        ClearJourneyLines();

        journeyAnchors.Add(new StepAnchor
        {
            worldPos = new Vector3(parkPos.x, 0.5f, parkPos.z),
            roomA = drone.CurrentRoom,
            roomB = drone.CurrentRoom,
            layer = 0,
        });
    }

    public void ClearJourney()
    {
        journeyAnchors.Clear();
        ClearJourneyLines();
    }

    void ClearJourneyLines()
    {
        foreach (var w in journeyWalls) w.HideLine();
        foreach (var r in journeyRoomLines) r.HideLine();
        journeyWalls.Clear();
        journeyRoomLines.Clear();
    }

    // ── preview (hover) ─────────────────────

    readonly List<WallView> previewWalls = new List<WallView>();
    readonly List<RoomTile> previewRooms = new List<RoomTile>();

    public void ShowPreview(DroneController.PreviewRequest req)
    {
        ClearPreview();
        isShowing = true;

        var path = req.path;
        bool hasPath = path != null && path.Count > 0;
        bool overBudget = ExceedsEnergy;
        Color col = overBudget
            ? Palette.WithAlpha(Palette.OverBudgetLine, 0.5f)
            : Palette.WithAlpha(Palette.PreviewLine, 0.4f);

        Vector3 dronePos = drone.transform.position;
        Vector2Int prev = drone.CurrentRoom;

        if (hasPath)
        {
            // Room segment: drone → first departure parkPoint
            var firstDep = fog?.GetTile(prev)?.GetPassage(path[0]);
            if (firstDep != null)
            {
                var roomTile = fog?.GetTile(prev);
                if (roomTile != null)
                {
                    roomTile.ShowLine(dronePos, firstDep.DroneParkPoint, col);
                    previewRooms.Add(roomTile);
                }
            }

            for (int i = 0; i < path.Count; i++)
            {
                var room = path[i];
                // Corridor segment: departure parkPoint → arrival parkPoint
                var dep = fog?.GetTile(prev)?.GetPassage(room);
                var arr = fog?.GetTile(room)?.GetPassage(prev);
                if (dep != null && arr != null)
                {
                    dep.ShowLine(dep.DroneParkPoint, arr.DroneParkPoint, col);
                    previewWalls.Add(dep);
                }
                else if (dep != null)
                {
                    dep.ShowLine(dep.DroneParkPoint, dep.transform.position, col);
                    previewWalls.Add(dep);
                }

                // Room segment within destination: arrival parkPoint → room center (or next departure)
                if (i < path.Count - 1)
                {
                    var nextRoom = path[i + 1];
                    var nextDep = fog?.GetTile(room)?.GetPassage(nextRoom);
                    var roomTile = fog?.GetTile(room);
                    if (arr != null && nextDep != null && roomTile != null)
                    {
                        roomTile.ShowLine(arr.DroneParkPoint, nextDep.DroneParkPoint, col);
                        previewRooms.Add(roomTile);
                    }
                }
                else
                {
                    // Last room — line to terminal wall or room center
                    var roomTile = fog?.GetTile(room);
                    if (roomTile != null && arr != null)
                    {
                        Vector3 lineTo = req.wall != null
                            ? req.wall.DroneParkPoint
                            : map.HexCenter(room);
                        roomTile.ShowLine(arr.DroneParkPoint, lineTo, col);
                        previewRooms.Add(roomTile);
                    }
                }

                prev = room;
            }
        }
        else if (req.wall != null)
        {
            // Drone already in room, just line to wall
            var roomTile = fog?.GetTile(drone.CurrentRoom);
            if (roomTile != null)
            {
                roomTile.ShowLine(dronePos, req.wall.DroneParkPoint, col);
                previewRooms.Add(roomTile);
            }
        }

        // Anchors for overlay bars
        previewAnchors.Clear();
        var cached = drone.PreviewAnchors;
        if (cached != null)
        {
            foreach (var a in cached)
                previewAnchors.Add(new StepAnchor
                {
                    worldPos = a.worldPos,
                    roomA = a.roomA,
                    roomB = a.roomB,
                    layer = a.layer,
                    overBudget = overBudget,
                });
        }
    }

    public void ClearPreview()
    {
        if (!isShowing) return;
        isShowing = false;

        foreach (var w in previewWalls) w.HideLine();
        foreach (var r in previewRooms) r.HideLine();
        previewWalls.Clear();
        previewRooms.Clear();
        previewAnchors.Clear();
    }

    // ── per-frame update ────────────────────

    public void Update() { }

    public void Destroy()
    {
        ClearJourneyLines();
        ClearPreview();
    }
}
