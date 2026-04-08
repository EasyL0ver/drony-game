using UnityEngine;
using UnityEngine.InputSystem;
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

        // Left click: start drag
        if (mouse.leftButton.wasPressedThisFrame)
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
        if (mouse.rightButton.wasPressedThisFrame)
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

    // ── move orders ──────────────────────────

    void IssueMoveOrder(Vector2 screenPos)
    {
        Vector2Int? target = ScreenToRoom(screenPos);
        if (target == null) return;

        foreach (var d in gm.Drones)
        {
            if (!d.IsSelected) continue;
            var p = FindPath(d.CurrentRoom, target.Value);
            if (p != null && p.Count > 0)
                d.SetPath(p);
        }
    }

    // ── screen → room ────────────────────────

    Vector2Int? ScreenToRoom(Vector2 screenPos)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (ray.direction.y >= 0) return null;
        float t = -ray.origin.y / ray.direction.y;
        Vector3 hit = ray.origin + ray.direction * t;

        float bestDist = float.MaxValue;
        Vector2Int? bestRoom = null;

        // Check rooms
        foreach (var room in gm.hexMap.RoomList)
        {
            Vector3 c = gm.hexMap.HexCenter(room);
            float dist = new Vector2(hit.x - c.x, hit.z - c.z).magnitude;
            float radius = gm.hexMap.RoomRadius(gm.hexMap.RoomSizeMap[room]);
            if (dist < radius && dist < bestDist)
            {
                bestDist = dist;
                bestRoom = room;
            }
        }

        // Check passages if no room hit
        if (bestRoom == null)
        {
            foreach (var (a, b, type) in gm.hexMap.ConnectionList)
            {
                var (midA, midB) = gm.hexMap.PassageEndpoints(a, b);
                Vector3 passCenter = (midA + midB) * 0.5f;
                float passW = gm.hexMap.PassageWidth(type);
                float dist = new Vector2(hit.x - passCenter.x, hit.z - passCenter.z).magnitude;
                if (dist < passW)
                {
                    // Pick the room on the far side from the click
                    Vector3 cA = gm.hexMap.HexCenter(a);
                    Vector3 cB = gm.hexMap.HexCenter(b);
                    float dA = new Vector2(hit.x - cA.x, hit.z - cA.z).magnitude;
                    float dB = new Vector2(hit.x - cB.x, hit.z - cB.z).magnitude;
                    bestRoom = dA < dB ? a : b;
                    break;
                }
            }
        }

        return bestRoom;
    }

    // ── pathfinding (Dijkstra) ───────────────

    List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
    {
        if (from == to) return null;

        // Build adjacency
        var adj = new Dictionary<Vector2Int, List<(Vector2Int neighbor, float cost)>>();
        foreach (var room in gm.hexMap.RoomList)
            adj[room] = new List<(Vector2Int, float)>();

        foreach (var (a, b, type) in gm.hexMap.ConnectionList)
        {
            float cost = TravelTime(type);
            adj[a].Add((b, cost));
            adj[b].Add((a, cost));
        }

        // Dijkstra — simple list PQ (fine for <50 rooms)
        var dist    = new Dictionary<Vector2Int, float>();
        var prev    = new Dictionary<Vector2Int, Vector2Int?>();
        var visited = new HashSet<Vector2Int>();
        var open    = new List<(float cost, Vector2Int room)>();

        foreach (var room in gm.hexMap.RoomList)
        {
            dist[room] = float.MaxValue;
            prev[room] = null;
        }

        dist[from] = 0f;
        open.Add((0f, from));

        while (open.Count > 0)
        {
            int minIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].cost < open[minIdx].cost) minIdx = i;

            var (curCost, cur) = open[minIdx];
            open.RemoveAt(minIdx);

            if (visited.Contains(cur)) continue;
            visited.Add(cur);
            if (cur == to) break;

            if (!adj.ContainsKey(cur)) continue;
            foreach (var (neighbor, edgeCost) in adj[cur])
            {
                if (visited.Contains(neighbor)) continue;
                float nd = curCost + edgeCost;
                if (nd < dist[neighbor])
                {
                    dist[neighbor] = nd;
                    prev[neighbor] = cur;
                    open.Add((nd, neighbor));
                }
            }
        }

        if (prev[to] == null) return null; // unreachable

        var result = new List<Vector2Int>();
        var step = to;
        while (step != from)
        {
            result.Add(step);
            step = prev[step].Value;
        }
        result.Reverse();
        return result;
    }

    float TravelTime(HexMapGenerator.PassageType type)
    {
        switch (type)
        {
            case HexMapGenerator.PassageType.Corridor: return 2f;
            case HexMapGenerator.PassageType.Duct:     return 4f;
            case HexMapGenerator.PassageType.Vent:     return 6f;
            default: return 2f;
        }
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
