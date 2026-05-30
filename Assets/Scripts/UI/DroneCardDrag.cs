using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to a drone card to enable touch/mouse drag.
/// On drag, selects the drone and issues a move order to where the finger lifts.
/// </summary>
public class DroneCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public DroneController Drone { get; set; }
    public GameManager GM { get; set; }

    Camera cam;
    GameObject dragIndicator;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Drone == null || GM == null) return;
        cam = Camera.main;

        // Select this drone
        foreach (var d in GM.Drones)
            d.IsSelected = false;
        Drone.IsSelected = true;

        // Create a simple drag indicator dot on the canvas
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            dragIndicator = new GameObject("DragIndicator");
            dragIndicator.transform.SetParent(canvas.transform, false);
            var img = dragIndicator.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 0.75f, 0f, 0.6f);
            img.raycastTarget = false;
            var rt = dragIndicator.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(24, 24);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragIndicator != null)
            dragIndicator.transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (dragIndicator != null)
            Destroy(dragIndicator);

        if (Drone == null || GM == null || cam == null) return;

        // Raycast from drop position to find target tile
        Ray ray = cam.ScreenPointToRay(eventData.position);
        var hits = Physics.RaycastAll(ray, 500f);

        RoomTile bestTile = null;
        float bestDist = float.MaxValue;
        foreach (var hit in hits)
        {
            var tile = hit.collider.GetComponentInParent<RoomTile>();
            if (tile != null && hit.distance < bestDist)
            {
                bestTile = tile;
                bestDist = hit.distance;
            }
        }

        if (bestTile != null)
        {
            bestTile.FlashMoveTarget();
            var target = bestTile.Coord;

            if (Drone.CurrentRoom != target)
            {
                var path = GM.hexMap.Model.FindPath(Drone.CurrentRoom, target, coord =>
                {
                    var t = GM.fog.GetTile(coord);
                    return t != null ? t.State : FogState.Unknown;
                });
                if (path != null && path.Count > 0)
                    Drone.SetPath(path);
            }
        }
    }
}
