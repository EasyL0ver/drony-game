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
            closest.IsSelected = true;
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
        RoomTile tile = RaycastTile(screenPos);
        if (tile != hoveredTile)
        {
            // Clear old previews
            ClearAllPreviews();

            if (hoveredTile != null)
                hoveredTile.SetHovered(false);
            hoveredTile = tile;
            if (hoveredTile != null)
            {
                hoveredTile.SetHovered(true);
                ShowPreviewsForTarget(hoveredTile.Coord);
            }
        }
    }

    void ShowPreviewsForTarget(Vector2Int target)
    {
        foreach (var d in gm.Drones)
        {
            if (!d.IsSelected) continue;
            var p = FindPath(d.CurrentRoom, target);
            if (p != null && p.Count > 0)
                d.ShowPreviewPath(p);
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
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f))
            return hit.collider.GetComponentInParent<RoomTile>();
        return null;
    }

    // ── move orders ──────────────────────────

    void IssueMoveOrder(Vector2 screenPos)
    {
        RoomTile tile = RaycastTile(screenPos);
        if (tile == null) return;

        ClearAllPreviews();
        tile.FlashMoveTarget();
        Vector2Int target = tile.Coord;

        foreach (var d in gm.Drones)
        {
            if (!d.IsSelected) continue;
            var p = FindPath(d.CurrentRoom, target);
            if (p != null && p.Count > 0)
                d.SetPath(p);
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
