using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// RTS-style selection and movement orders.
/// Left-click / drag-box to select drones, right-click to issue move orders.
/// Shift+click for additive selection.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    GameManager gm;
    Camera cam;
    WallView hoveredWallView;
    // Tracked wall interaction target (when hovering a passage with an interaction)
    Vector2Int hoveredConnA, hoveredConnB;
    bool hoveredHasWallInteraction;

    // Drag state
    bool isDragging;
    Vector2 dragStart;
    const float dragThreshold = 5f;

    // Hover
    RoomTile hoveredTile;
    readonly Dictionary<int, Vector2Int> droneLastRoom = new Dictionary<int, Vector2Int>();
    HoverInfoPanel hoverInfoPanel;

    // Box visuals
    Color boxColor       = Palette.SelectionBoxFill;
    Color boxBorderColor = Palette.SelectionBoxBorder;
    Texture2D boxTex;
    Texture2D borderTex;

    // ── public API ───────────────────────────

    public void Init(GameManager gameManager)
    {
        gm  = gameManager;
        cam = Camera.main;

        var panelGO = new GameObject("HoverInfo");
        panelGO.transform.SetParent(transform, false);
        hoverInfoPanel = panelGO.AddComponent<HoverInfoPanel>();
        hoverInfoPanel.Init();
    }

    // ── update ───────────────────────────────

    void Update()
    {
        if (!Application.isPlaying || gm == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 mousePos = mouse.position.ReadValue();

        // Hover tracking
        UpdateHover(mousePos);

        // Refresh previews if a selected drone changed room mid-journey
        if (hoveredTile != null)
        {
            bool needRefresh = false;
            foreach (var d in gm.Drones)
            {
                if (!d.IsSelected) continue;
                droneLastRoom.TryGetValue(d.DroneIndex, out var lastRoom);
                if (lastRoom != d.CurrentRoom)
                {
                    droneLastRoom[d.DroneIndex] = d.CurrentRoom;
                    needRefresh = true;
                }
            }
            if (needRefresh)
            {
                ClearAllPreviews();
                if (hoveredHasWallInteraction)
                    ShowWallInteractionPreviews();
                else
                    ShowPreviewsForTarget(hoveredTile.Coord, hoveredWallView);
            }
        }

        // Ignore input when clicking on UI
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Left click: start drag
        if (mouse.leftButton.wasPressedThisFrame && !overUI)
        {
            dragStart = mousePos;
            isDragging = true;
        }

        // Left release: select
        if (mouse.leftButton.wasReleasedThisFrame && isDragging)
        {
            isDragging = false;

            if (Vector2.Distance(dragStart, mousePos) < dragThreshold)
                ClickSelect(mousePos);
            else
                BoxSelect(dragStart, mousePos);
        }

        // Right click: move order
        if (mouse.rightButton.wasPressedThisFrame && !overUI)
            IssueMoveOrder(mousePos);
    }

    // ── selection ────────────────────────────

    void ClickSelect(Vector2 screenPos)
    {
        bool additive = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        if (!additive)
            foreach (var d in gm.Drones) d.IsSelected = false;

        DroneController closest = null;
        float closestDist = 40f; // pixel radius

        foreach (var d in gm.Drones)
        {
            Vector3 sp = cam.WorldToScreenPoint(d.transform.position);
            if (sp.z < 0) continue;
            float dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = d;
            }
        }

        if (closest != null)
        {
            closest.IsSelected = true;
            return;
        }

        // No drone visible near click — select drones on the clicked tile (e.g. under fog)
        RoomTile tile = RaycastTile(screenPos);
        if (tile != null && tile.DronesOnTile.Count > 0)
        {
            foreach (var d in tile.DronesOnTile)
                d.IsSelected = true;
        }
    }

    void BoxSelect(Vector2 start, Vector2 end)
    {
        bool additive = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        if (!additive)
            foreach (var d in gm.Drones) d.IsSelected = false;

        Rect rect = MakeRect(start, end);

        foreach (var d in gm.Drones)
        {
            Vector3 sp = cam.WorldToScreenPoint(d.transform.position);
            if (sp.z < 0) continue;
            if (rect.Contains(new Vector2(sp.x, sp.y)))
                d.IsSelected = true;
        }
    }

    // ── hover ────────────────────────────────

    void UpdateHover(Vector2 screenPos)
    {
        var (tile, wall, hasWI, connA, connB) = RaycastTileWithEdge(screenPos);
        if (tile != hoveredTile || wall != hoveredWallView
            || hasWI != hoveredHasWallInteraction)
        {
            // Clear old previews
            ClearAllPreviews();

            if (hoveredTile != null)
                hoveredTile.SetHovered(false);
            hoveredTile = tile;
            hoveredWallView = wall;
            hoveredHasWallInteraction = hasWI;
            hoveredConnA = connA;
            hoveredConnB = connB;
            if (hoveredTile != null)
            {
                int wiEdge = -1;
                if (hoveredHasWallInteraction)
                    wiEdge = gm.hexMap.EdgeToward(hoveredConnA, hoveredConnB);

                hoveredTile.SetHovered(true, hoveredWallView, wiEdge);
                if (hoveredHasWallInteraction)
                    ShowWallInteractionPreviews();
                else
                    ShowPreviewsForTarget(hoveredTile.Coord, hoveredWallView);
            }

            // Update hover info panel
            UpdateHoverDescription();
        }
    }

    void UpdateHoverDescription()
    {
        if (hoverInfoPanel == null) return;

        if (hoveredWallView != null && !string.IsNullOrEmpty(hoveredWallView.HoverDescription))
        {
            hoverInfoPanel.SetDescription(hoveredWallView.HoverDescription);
        }
        else if (hoveredTile != null)
        {
            string sizeLabel = hoveredTile.Size.ToString();
            string stateLabel = hoveredTile.State.ToString();
            hoverInfoPanel.SetDescription($"{sizeLabel} Room — {stateLabel}");
        }
        else
        {
            hoverInfoPanel.SetDescription("");
        }
    }

    void ShowPreviewsForTarget(Vector2Int target, WallView wall = null)
    {
        var targetTile = gm.fog.GetTile(target);
        foreach (var d in gm.Drones)
        {
            if (!d.IsSelected) continue;
            droneLastRoom[d.DroneIndex] = d.CurrentRoom;
            var p = FindPath(d.CurrentRoom, target, d.Model);
            if (p != null && p.Count > 0)
                d.ShowPreview(new DroneController.PreviewRequest { path = p, wall = wall });
            else if (d.CurrentRoom == target && targetTile != null && wall != null)
                d.ShowPreview(new DroneController.PreviewRequest { wall = wall });
            else
                d.ClearPreviewPath();
        }
    }

    void ShowWallInteractionPreviews()
    {
        int edgeAB = gm.hexMap.EdgeToward(hoveredConnA, hoveredConnB);
        var tileA = gm.fog.GetTile(hoveredConnA);
        var wallModel = tileA?.RModel.Walls[edgeAB];

        foreach (var d in gm.Drones)
        {
            if (!d.IsSelected) continue;
            if (wallModel == null || wallModel.GetInteractions(d.Model).Count == 0) { d.ClearPreviewPath(); continue; }

            var pA = FindPath(d.CurrentRoom, hoveredConnA, d.Model);
            var pB = FindPath(d.CurrentRoom, hoveredConnB, d.Model);

            List<Vector2Int> best = null;
            if (pA != null && pB != null)
                best = pA.Count <= pB.Count ? pA : pB;
            else
                best = pA ?? pB;

            if (best != null && best.Count > 0)
            {
                var destRoom = best[best.Count - 1];
                var otherRoom = (destRoom == hoveredConnA) ? hoveredConnB : hoveredConnA;
                var iWall = gm.fog.GetTile(destRoom)?.GetPassage(otherRoom);
                d.ShowPreview(new DroneController.PreviewRequest { path = best, wall = iWall });
            }
            else if (d.CurrentRoom == hoveredConnA || d.CurrentRoom == hoveredConnB)
            {
                Vector2Int approach = d.CurrentRoom;
                Vector2Int other = (approach == hoveredConnA) ? hoveredConnB : hoveredConnA;
                var iWall = gm.fog.GetTile(approach)?.GetPassage(other);
                if (iWall != null)
                    d.ShowPreview(new DroneController.PreviewRequest { wall = iWall });
                else
                    d.ClearPreviewPath();
            }
            else
                d.ClearPreviewPath();
        }
    }

    void ClearAllPreviews()
    {
        foreach (var d in gm.Drones)
            d.ClearPreviewPath();
    }

    RoomTile RaycastTile(Vector2 screenPos)
    {
        return RaycastTileWithEdge(screenPos).tile;
    }

    /// <summary>
    /// Raycast to find the hovered tile, then use angle-from-center to determine
    /// which hex edge the cursor is nearest. Looks up wall data on the model.
    /// </summary>
    (RoomTile tile, WallView wall, bool hasWallInteraction, Vector2Int connA, Vector2Int connB)
    RaycastTileWithEdge(Vector2 screenPos)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        var hits = Physics.RaycastAll(ray, 500f);

        RoomTile tile = null;
        float closestDist = float.MaxValue;
        Vector3 hitPoint = Vector3.zero;

        foreach (var hit in hits)
        {
            var t = hit.collider.GetComponentInParent<RoomTile>();
            if (t != null && hit.distance < closestDist)
            {
                tile = t;
                closestDist = hit.distance;
                hitPoint = hit.point;
            }
        }

        if (tile == null) return (null, null, false, default, default);

        if (tile.State == FogState.Unknown || tile.State == FogState.Scanning)
            return (tile, null, false, default, default);

        // If click is close to room center, target the room itself (not an edge)
        Vector3 roomCenter = gm.hexMap.HexCenter(tile.Coord);
        float roomR = gm.hexMap.RoomRadius(gm.hexMap.RoomSizeMap[tile.Coord]);
        float distFromCenter = new Vector2(hitPoint.x - roomCenter.x, hitPoint.z - roomCenter.z).magnitude;
        if (distFromCenter < roomR * 0.45f)
            return (tile, null, false, default, default);

        int edge = gm.hexMap.NearestEdge(hitPoint, tile.Coord);
        WallView wallView = tile.GetWallView(edge);

        // If the wall at this edge is a passage, check for blocking interaction or resolve to neighbor
        if (wallView == null || wallView is Passage)
        {
            foreach (var conn in tile.Connections)
            {
                if (conn.edgeIndex == edge)
                {
                    if (gm.hexMap.Model.HasBlockingInteraction(tile.Coord, conn.neighbor.Coord))
                        return (tile, null, true, tile.Coord, conn.neighbor.Coord);

                    // No interaction — resolve to neighbor tile as before
                    var neighborTile = conn.neighbor;
                    if (neighborTile != null)
                        return (neighborTile, null, false, default, default);
                    break;
                }
            }
            wallView = null;
        }

        return (tile, wallView, false, default, default);
    }

    // ── move orders ──────────────────────────

    void IssueMoveOrder(Vector2 screenPos)
    {
        var (tile, clickedWall, hasWI, connA, connB) = RaycastTileWithEdge(screenPos);
        if (tile == null) return;

        ClearAllPreviews();

        // Wall interaction click (rubble, etc.)
        if (hasWI)
        {
            int edgeAB = gm.hexMap.EdgeToward(connA, connB);
            var wallTile = gm.fog.GetTile(connA);
            var wallModel = wallTile?.RModel.Walls[edgeAB];

            foreach (var d in gm.Drones)
            {
                if (!d.IsSelected) continue;
                if (d.IsPerformingInteraction) continue;
                if (wallModel == null || wallModel.GetInteractions(d.Model).Count == 0) continue;

                // Path to whichever side is reachable (prefer shorter)
                var pA = FindPath(d.CurrentRoom, connA, d.Model);
                var pB = FindPath(d.CurrentRoom, connB, d.Model);
                List<Vector2Int> best = null;
                if (pA != null && pB != null)
                    best = pA.Count <= pB.Count ? pA : pB;
                else
                    best = pA ?? pB;

                if (best == null) best = new List<Vector2Int>();

                d.SetPathToWallInteraction(
                    best.Count > 0 ? best : null,
                    connA, connB);
            }
            return;
        }

        tile.FlashMoveTarget();
        Vector2Int target = tile.Coord;

        foreach (var d in gm.Drones)
        {
            if (!d.IsSelected) continue;
            if (d.IsPerformingInteraction) continue;

            // Drone already on this tile — try wall action if structure was clicked
            if (d.CurrentRoom == target && clickedWall != null)
            {
                d.StartInteraction(tile, clickedWall);
                continue;
            }

            var p = FindPath(d.CurrentRoom, target, d.Model);
            if (p != null && p.Count > 0)
                d.SetPath(p, clickedWall);
        }
    }

    // ── pathfinding (Dijkstra) ───────────────

    List<Vector2Int> FindPath(Vector2Int from, Vector2Int to, DroneModel drone = null)
    {
        var fog = gm.fog;
        return gm.hexMap.Model.FindPath(from, to, coord =>
        {
            var tile = fog.GetTile(coord);
            return tile != null ? tile.State : FogState.Unknown;
        }, drone);
    }

    // ── selection box GUI ────────────────────

    void OnGUI()
    {
        if (!isDragging) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 mousePos = mouse.position.ReadValue();
        if (Vector2.Distance(dragStart, mousePos) < dragThreshold) return;

        EnsureTextures();

        // Screen coords → GUI coords (Y flipped)
        Vector2 start = new Vector2(dragStart.x, Screen.height - dragStart.y);
        Vector2 end   = new Vector2(mousePos.x,  Screen.height - mousePos.y);
        Rect rect = MakeRect(start, end);

        GUI.DrawTexture(rect, boxTex);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2), borderTex);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2, rect.width, 2), borderTex);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 2, rect.height), borderTex);
        GUI.DrawTexture(new Rect(rect.xMax - 2, rect.y, 2, rect.height), borderTex);
    }

    void EnsureTextures()
    {
        if (boxTex == null)
        {
            boxTex = new Texture2D(1, 1);
            boxTex.SetPixel(0, 0, boxColor);
            boxTex.Apply();
        }
        if (borderTex == null)
        {
            borderTex = new Texture2D(1, 1);
            borderTex.SetPixel(0, 0, boxBorderColor);
            borderTex.Apply();
        }
    }

    // ── util ─────────────────────────────────

    Rect MakeRect(Vector2 a, Vector2 b)
    {
        return new Rect(
            Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
            Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }
}
