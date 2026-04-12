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

    // ── journey line (active move) ──────────
    readonly List<Vector3> journeyWaypoints = new List<Vector3>();
    readonly List<float> journeyCumulDist = new List<float>();
    readonly List<StepAnchor> journeyAnchors = new List<StepAnchor>();
    GameObject pathLineGO;
    MeshFilter pathMF;
    MeshRenderer pathMR;
    Material pathMat;
    Mesh pathMesh;

    // ── preview line (hover) ────────────────
    bool isShowing;
    readonly List<Vector3> previewWaypoints = new List<Vector3>();
    readonly List<float> previewCumulDist = new List<float>();
    readonly List<DroneController.JourneyStep> plan = new List<DroneController.JourneyStep>();
    readonly List<StepAnchor> previewAnchors = new List<StepAnchor>();
    GameObject previewLineGO;
    MeshFilter previewMF;
    MeshRenderer previewMR;
    Material previewMat;
    Mesh previewMesh;

    // ── shared constants ────────────────────
    const float pathY = 0.06f;
    const float pathWidth = 0.12f;
    const float dashLen = 0.30f;
    const float gapLen = 0.15f;

    public RoutePreview(DroneController drone, HexMapGenerator map, FogOfWar fog)
    {
        this.drone = drone;
        this.map = map;
        this.fog = fog;
    }

    // ── preview public state ────────────────

    public bool IsShowing => isShowing;
    public IReadOnlyList<DroneController.JourneyStep> Plan => plan;

    public float TotalTime
    {
        get { float t = 0; foreach (var s in plan) t += s.duration; return t; }
    }

    public int EnergyCost
    {
        get { int t = 0; foreach (var s in plan) t += s.energyCost; return t; }
    }

    public bool ExceedsEnergy =>
        isShowing && EnergyCost > (drone.CurrentEnergy - drone.JourneyEnergyCost);

    public IReadOnlyList<StepAnchor> JourneyAnchors => journeyAnchors;
    public IReadOnlyList<StepAnchor> PreviewAnchors => previewAnchors;

    // ── journey (active move) ───────────────

    public void SetJourney(List<Vector2Int> path, StationType stationAction)
    {
        journeyWaypoints.Clear();
        journeyCumulDist.Clear();
        journeyAnchors.Clear();

        if (path == null || path.Count == 0) return;

        Vector3 origin = map.HexCenter(drone.CurrentRoom);
        journeyWaypoints.Add(new Vector3(origin.x, pathY, origin.z));

        Vector2Int prev = drone.CurrentRoom;
        foreach (var room in path)
        {
            journeyWaypoints.Add(PassagePoint(prev, room, pathY));
            journeyWaypoints.Add(PassagePoint(room, prev, pathY));
            Vector3 rc = map.HexCenter(room);
            journeyWaypoints.Add(new Vector3(rc.x, pathY, rc.z));
            prev = room;
        }

        if (stationAction != StationType.None)
        {
            var lastCoord = path[path.Count - 1];
            var lastTile = fog?.GetTile(lastCoord);
            if (lastTile != null && lastTile.RModel.StationEdge >= 0)
            {
                Vector3 wallPt = map.WallMidpoint(lastCoord, lastTile.RModel.StationEdge, lastTile.RModel.Size);
                journeyWaypoints.Add(new Vector3(wallPt.x, pathY, wallPt.z));
            }
        }

        journeyCumulDist.Add(0f);
        for (int i = 1; i < journeyWaypoints.Count; i++)
            journeyCumulDist.Add(journeyCumulDist[i - 1]
                + Vector3.Distance(journeyWaypoints[i - 1], journeyWaypoints[i]));

        // Anchor positions for overlay
        journeyAnchors.Clear();
        prev = drone.CurrentRoom;
        int stepIdx = 0;
        foreach (var room in path)
        {
            Vector3 pA = PassagePoint(prev, room, 0.5f);
            Vector3 pB = PassagePoint(room, prev, 0.5f);
            journeyAnchors.Add(new StepAnchor
            {
                worldPos = (pA + pB) * 0.5f,
                roomA = prev,
                roomB = room,
                layer = 0,
            });
            stepIdx++;
            prev = room;
        }

        var destCoord = path[path.Count - 1];
        var destTile = fog?.GetTile(destCoord);
        var journey = drone.Journey;
        float destBarY = 0.5f;
        int destLayer = 0;
        while (stepIdx < journey.Count)
        {
            var step = journey[stepIdx];
            Vector3 barPos = (step.isCharge || step.isRefit) && destTile != null && destTile.RModel.StationEdge >= 0
                ? map.WallMidpoint(destCoord, destTile.RModel.StationEdge, destTile.RModel.Size)
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
        journeyWaypoints.Clear();
        journeyCumulDist.Clear();
        journeyAnchors.Clear();

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
        journeyWaypoints.Clear();
        journeyCumulDist.Clear();
        journeyAnchors.Clear();
        if (pathLineGO != null) pathLineGO.SetActive(false);
    }

    // ── preview (hover) ─────────────────────

    public void ShowPath(List<Vector2Int> previewPath, StationType stationAction = StationType.None)
    {
        if (previewPath == null || previewPath.Count == 0)
        {
            ClearPreview();
            return;
        }

        isShowing = true;

        plan.Clear();
        Vector2Int prev = drone.CurrentRoom;
        foreach (var room in previewPath)
        {
            var passage = fog?.GetTile(prev)?.GetPassage(room);
            var ptype = passage != null ? passage.Type : PassageType.Corridor;
            plan.Add(new DroneController.JourneyStep
            {
                label = MapModel.PassageLabel(ptype),
                duration = MapModel.TravelTime(ptype),
                isScan = false,
                energyCost = MapModel.StepEnergyCost(ptype),
            });
            prev = room;
        }

        var finalTile = fog?.GetTile(previewPath[previewPath.Count - 1]);
        if (finalTile != null && finalTile.State == FogState.Unknown && drone.Model.CanScan)
        {
            plan.Add(new DroneController.JourneyStep
            {
                label = "SCAN",
                duration = finalTile.ScanTotalTime,
                isScan = true,
                energyCost = MapModel.ScanEnergyCost,
            });
        }

        var pvStation = finalTile?.GetStation();
        if (pvStation != null && pvStation.StationType == stationAction)
        {
            plan.Add(new DroneController.JourneyStep
            {
                label = MapModel.StationLabel(stationAction),
                duration = MapModel.StationDuration(stationAction),
                isCharge = stationAction == StationType.Charging,
                isRefit = stationAction == StationType.Refitting,
                energyCost = 0,
            });
        }

        previewWaypoints.Clear();
        previewCumulDist.Clear();
        Vector3 origin = drone.transform.position;
        previewWaypoints.Add(new Vector3(origin.x, pathY, origin.z));

        prev = drone.CurrentRoom;
        foreach (var room in previewPath)
        {
            previewWaypoints.Add(PassagePoint(prev, room, pathY));
            previewWaypoints.Add(PassagePoint(room, prev, pathY));
            Vector3 rc = map.HexCenter(room);
            previewWaypoints.Add(new Vector3(rc.x, pathY, rc.z));
            prev = room;
        }

        if (stationAction != StationType.None)
        {
            var lastCoord = previewPath[previewPath.Count - 1];
            var lastTile = fog?.GetTile(lastCoord);
            if (lastTile != null && lastTile.RModel.StationEdge >= 0)
            {
                Vector3 wallPt = map.WallMidpoint(lastCoord, lastTile.RModel.StationEdge, lastTile.RModel.Size);
                previewWaypoints.Add(new Vector3(wallPt.x, pathY, wallPt.z));
            }
        }

        previewCumulDist.Add(0f);
        for (int i = 1; i < previewWaypoints.Count; i++)
            previewCumulDist.Add(previewCumulDist[i - 1] + Vector3.Distance(previewWaypoints[i - 1], previewWaypoints[i]));

        EnsurePreviewLine();
        previewLineGO.SetActive(true);
        bool overBudget = ExceedsEnergy;
        Color col = overBudget
            ? Palette.WithAlpha(Palette.OverBudgetLine, 0.5f)
            : Palette.WithAlpha(Palette.PreviewLine, 0.4f);
        previewMat.color = col;
        previewMat.SetColor("_BaseColor", col);
        DroneController.BuildDashedRibbonInto(previewMesh, previewWaypoints, previewCumulDist, 0f, pathWidth, dashLen, gapLen);

        previewAnchors.Clear();
        prev = drone.CurrentRoom;
        int stepIdx = 0;
        foreach (var room in previewPath)
        {
            Vector3 pA = PassagePoint(prev, room, 0.5f);
            Vector3 pB = PassagePoint(room, prev, 0.5f);
            previewAnchors.Add(new StepAnchor
            {
                worldPos = (pA + pB) * 0.5f,
                roomA = prev,
                roomB = room,
                layer = 0,
                overBudget = overBudget,
            });
            stepIdx++;
            prev = room;
        }

        var pvDestCoord = previewPath[previewPath.Count - 1];
        var pvDestTile = fog?.GetTile(pvDestCoord);
        float destBarY = 0.5f;
        int destLayer = 0;
        while (stepIdx < plan.Count)
        {
            var step = plan[stepIdx];
            Vector3 barPos = (step.isCharge || step.isRefit) && pvDestTile != null && pvDestTile.RModel.StationEdge >= 0
                ? map.WallMidpoint(pvDestCoord, pvDestTile.RModel.StationEdge, pvDestTile.RModel.Size)
                : map.HexCenter(pvDestCoord);
            previewAnchors.Add(new StepAnchor
            {
                worldPos = new Vector3(barPos.x, destBarY, barPos.z),
                roomA = pvDestCoord,
                roomB = pvDestCoord,
                layer = destLayer,
                overBudget = overBudget,
            });
            stepIdx++;
            destBarY += 0.8f;
            destLayer++;
        }
    }

    public void ShowStation(RoomTile tile, StationType stationAction = StationType.None)
    {
        if (tile == null || !tile.RModel.IsStation) return;
        if (drone.IsPerformingStationAction) return;

        var station = tile.GetStation();
        if (station == null || station.StationType != stationAction) return;

        ClearPreview();
        isShowing = true;
        plan.Clear();

        plan.Add(new DroneController.JourneyStep
        {
            label = MapModel.StationLabel(stationAction),
            duration = MapModel.StationDuration(stationAction),
            isCharge = stationAction == StationType.Charging,
            isRefit = stationAction == StationType.Refitting,
            energyCost = 0,
        });

        previewAnchors.Clear();
        Vector3 rc = tile.RModel.StationEdge >= 0
            ? map.WallMidpoint(tile.Coord, tile.RModel.StationEdge, tile.RModel.Size)
            : map.HexCenter(tile.Coord);
        previewAnchors.Add(new StepAnchor
        {
            worldPos = new Vector3(rc.x, 0.5f, rc.z),
            roomA = tile.Coord,
            roomB = tile.Coord,
            layer = 0,
        });

        previewWaypoints.Clear();
        previewCumulDist.Clear();
        Vector3 dronePos = drone.transform.position;
        previewWaypoints.Add(new Vector3(dronePos.x, pathY, dronePos.z));
        previewWaypoints.Add(new Vector3(rc.x, pathY, rc.z));
        previewCumulDist.Add(0f);
        previewCumulDist.Add(Vector3.Distance(previewWaypoints[0], previewWaypoints[1]));

        EnsurePreviewLine();
        previewLineGO.SetActive(true);
        Color col = Palette.WithAlpha(Palette.PreviewLine, 0.4f);
        previewMat.color = col;
        previewMat.SetColor("_BaseColor", col);
        DroneController.BuildDashedRibbonInto(previewMesh, previewWaypoints, previewCumulDist, 0f, pathWidth, dashLen, gapLen);
    }

    public void ClearPreview()
    {
        if (!isShowing) return;
        isShowing = false;

        if (previewLineGO != null)
            previewLineGO.SetActive(false);

        plan.Clear();
        previewWaypoints.Clear();
        previewCumulDist.Clear();
        previewAnchors.Clear();
    }

    // ── per-frame update ────────────────────

    public void Update()
    {
        UpdatePathLine();
    }

    public void Destroy()
    {
        if (pathLineGO != null) Object.Destroy(pathLineGO);
        if (previewLineGO != null) Object.Destroy(previewLineGO);
    }

    // ── private: journey path line ──────────

    void UpdatePathLine()
    {
        int journeyIdx = drone.JourneyCurrentIndex;
        var journey = drone.Journey;
        bool hasPath = journeyWaypoints.Count > 1 && journeyIdx >= 0;

        if (!hasPath || (journeyIdx < journey.Count
            && (journey[journeyIdx].isScan || journey[journeyIdx].isCharge || journey[journeyIdx].isRefit)))
        {
            if (pathLineGO != null) pathLineGO.SetActive(false);
            return;
        }

        EnsurePathLine();
        pathLineGO.SetActive(true);

        float alpha = drone.IsSelected ? 0.55f : 0.2f;
        Color col = Palette.WithAlpha(Palette.JourneyLine, alpha);
        pathMat.color = col;
        pathMat.SetColor("_BaseColor", col);

        float consumed = 0f;
        int segIdx = drone.MoveSegIdx;
        if (segIdx < journeyCumulDist.Count - 1)
        {
            consumed = journeyCumulDist[segIdx]
                + drone.MoveSegT * (journeyCumulDist[segIdx + 1] - journeyCumulDist[segIdx]);
        }
        else if (journeyCumulDist.Count > 0)
        {
            consumed = journeyCumulDist[journeyCumulDist.Count - 1];
        }

        DroneController.BuildDashedRibbonInto(pathMesh, journeyWaypoints, journeyCumulDist, consumed, pathWidth, dashLen, gapLen);
    }

    void EnsurePathLine()
    {
        if (pathLineGO != null) return;

        pathLineGO = new GameObject("PathLine_" + drone.DroneName);
        pathMF = pathLineGO.AddComponent<MeshFilter>();
        pathMR = pathLineGO.AddComponent<MeshRenderer>();

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        pathMat = new Material(sh);
        Color col = Palette.WithAlpha(Palette.JourneyLine, 0.3f);
        pathMat.color = col;
        pathMat.SetColor("_BaseColor", col);
        pathMat.SetFloat("_Surface", 1f);
        pathMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        pathMat.SetOverrideTag("RenderType", "Transparent");
        pathMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        pathMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        pathMat.SetInt("_ZWrite", 0);
        pathMat.SetFloat("_Cull", 0f);
        pathMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
        pathMR.sharedMaterial = pathMat;
        pathMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        pathMesh = new Mesh { name = "PathDashed" };
        pathMF.sharedMesh = pathMesh;
    }

    // ── private: preview line ───────────────

    void EnsurePreviewLine()
    {
        if (previewLineGO != null) return;

        previewLineGO = new GameObject("PreviewLine_" + drone.DroneName);
        previewMF = previewLineGO.AddComponent<MeshFilter>();
        previewMR = previewLineGO.AddComponent<MeshRenderer>();

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        previewMat = new Material(sh);
        Color col = Palette.WithAlpha(Palette.PreviewLine, 0.4f);
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

    // ── shared helpers ──────────────────────

    Vector3 PassagePoint(Vector2Int from, Vector2Int to, float y)
    {
        var pass = fog?.GetTile(from)?.GetPassage(to);
        if (pass != null)
            return new Vector3(pass.DroneParkPoint.x, y, pass.DroneParkPoint.z);
        var (mid, _) = map.PassageEndpoints(from, to);
        return new Vector3(mid.x, y, mid.z);
    }
}
