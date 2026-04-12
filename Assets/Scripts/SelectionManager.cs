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
    StationType hoveredStation;

    // Drag state
    bool isDragging;
    Vector2 dragStart;
    const float dragThreshold = 5f;

    // Hover
    RoomTile hoveredTile;

    // Box visuals
    [SerializeField] Color boxColor       = new Color(0f, 0.85f, 1f, 0.15f);
    [SerializeField] Color boxBorderColor = new Color(0f, 0.85f, 1f, 0.8f);
    Texture2D boxTex;
    Texture2D borderTex;

    // ── public API ───────────────────────────

    public void Init(GameManager gameManager)
    {
        gm  = gameManager;
        cam = Camera.main;
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
        var (tile, station) = RaycastTileWithEdge(screenPos);
        if (tile != hoveredTile || station != hoveredStation)
        {
            // Clear old previews
            ClearAllPreviews();

            if (hoveredTile != null)
                hoveredTile.SetHovered(false);
            hoveredTile = tile;
            hoveredStation = station;
            if (hoveredTile != null)
            {
                hoveredTile.SetHovered(true, hoveredStation);
                ShowPreviewsForTarget(hoveredTile.Coord, hoveredStation);
            }
        }
    }

    void ShowPreviewsForTarget(Vector2Int target, StationType stationAction)
    {
        var targetTile = gm.fog.GetTile(target);
        foreach (var d in gm.Drones)
        {
            if (!d.IsSelected) continue;
            var p = FindPath(d.CurrentRoom, target);
            if (p != null && p.Count > 0)
                d.ShowPreviewPath(p, stationAction);
            else if (d.CurrentRoom == target && targetTile != null
                     && stationAction != StationType.None)
                d.ShowStationPreview(targetTile, stationAction);
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
    /// which hex edge the cursor is nearest. Looks up wall station data on the model.
    /// No need to raycast individual station meshes.
    /// </summary>
    (RoomTile tile, StationType station) RaycastTileWithEdge(Vector2 screenPos)
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

        if (tile == null) return (null, StationType.None);

        int edge = gm.hexMap.Model.NearestEdge(hitPoint, tile.Coord);
        StationType wallStation = tile.RModel.GetWallStation(edge);

        // If the edge has a passage (not a station), resolve to the neighbor tile
        if (wallStation == StationType.None)
        {
            foreach (var conn in tile.RModel.Connections)
            {
                if (conn.edgeIndex == edge)
                {
                    var neighborTile = gm.fog.GetTile(conn.neighbor.Coord);
                    if (neighborTile != null)
                        return (neighborTile, StationType.None);
                    break;
                }
            }
        }

        return (tile, wallStation);
    }

    // ── move orders ──────────────────────────

    void IssueMoveOrder(Vector2 screenPos)
    {
        var (tile, clickedStation) = RaycastTileWithEdge(screenPos);
        if (tile == null) return;

        ClearAllPreviews();
        tile.FlashMoveTarget();
        Vector2Int target = tile.Coord;

        foreach (var d in gm.Drones)
        {
            if (!d.IsSelected) continue;
            if (d.IsPerformingStationAction) continue;

            // Drone already on this tile — try station action if structure was clicked
            if (d.CurrentRoom == target && clickedStation != StationType.None)
            {
                d.StartStationAction(tile, clickedStation);
                continue;
            }

            var p = FindPath(d.CurrentRoom, target);
            if (p != null && p.Count > 0)
                d.SetPath(p, clickedStation);
        }
    }

    // ── pathfinding (Dijkstra) ───────────────

    List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
    {
        var fog = gm.fog;
        return gm.hexMap.Model.FindPath(from, to, coord =>
        {
            var tile = fog.GetTile(coord);
            return tile != null ? tile.State : FogState.Unknown;
        });
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
