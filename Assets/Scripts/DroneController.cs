using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Per-drone component: tracks current room, travel path, selection state.
/// Movement is graph-based — drone hops room-to-room with visual lerp.
/// </summary>
public class DroneController : MonoBehaviour
{
    public Vector2Int CurrentRoom { get; private set; }
    public bool IsSelected { get; set; }
    public bool IsMoving => path.Count > 0 || travelProgress < 1f;

    HexMapGenerator map;
    FogOfWar fog;
    Queue<Vector2Int> path = new Queue<Vector2Int>();

    // Current hop
    Vector2Int fromRoom;
    Vector2Int toRoom;
    float travelProgress = 1f;  // 1 = arrived
    float travelDuration;
    [SerializeField] float hoverY = 1f;

    // Selection visuals
    GameObject selectionRing;
    Material ringMat;
    LowPolyDrone droneModel;

    // Idle animation
    float idlePhase;
    float idleBlend;       // 0 = moving, 1 = fully idle
    Vector3 wanderTarget;
    float wanderWait;
    bool hasWanderTarget;

    // ── public API ───────────────────────────

    public void Init(HexMapGenerator mapGen, FogOfWar fogOfWar, Vector2Int startRoom)
    {
        map = mapGen;
        fog = fogOfWar;
        CurrentRoom = startRoom;
        fromRoom = startRoom;
        toRoom = startRoom;
        travelProgress = 1f;
        transform.position = RoomWorldPos(startRoom);
        CreateSelectionRing();
        idlePhase = Random.Range(0f, Mathf.PI * 2f);

        // Notify start tile
        var tile = fog.GetTile(startRoom);
        if (tile != null)
            tile.OnDroneEnter();
    }

    public void SetPath(List<Vector2Int> newPath)
    {
        path.Clear();

        // If mid-travel, snap to nearest room
        if (travelProgress < 1f)
        {
            CurrentRoom = travelProgress < 0.5f ? fromRoom : toRoom;
            travelProgress = 1f;
            transform.position = RoomWorldPos(CurrentRoom);
        }

        foreach (var room in newPath)
            path.Enqueue(room);
    }

    // ── lifecycle ────────────────────────────

    void Start()
    {
        // Find the LowPolyDrone model (child)
        droneModel = GetComponentInChildren<LowPolyDrone>();
    }

    void Update()
    {
        if (!Application.isPlaying || map == null) return;

        UpdateMovement();
        UpdateIdleAnimation();
        UpdateSelectionVisuals();
    }

    void UpdateSelectionVisuals()
    {
        // Ring: show + pulse when selected
        if (selectionRing != null)
        {
            selectionRing.SetActive(IsSelected);
            if (IsSelected && ringMat != null)
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 4f);
                Color col = new Color(0f, 0.85f, 1f, pulse);
                ringMat.color = col;
                ringMat.SetColor("_BaseColor", col);
            }
        }

        // Drone glow: boost when selected
        if (droneModel != null && droneModel.GlowMaterial != null)
        {
            Color baseCol = droneModel.BaseGlowColor;
            float baseInt = droneModel.BaseGlowIntensity;

            if (IsSelected)
            {
                float boost = 1.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                droneModel.GlowMaterial.SetColor("_EmissionColor", baseCol * baseInt * boost);
            }
            else
            {
                droneModel.GlowMaterial.SetColor("_EmissionColor", baseCol * baseInt);
            }
        }
    }

    void UpdateMovement()
    {
        if (travelProgress >= 1f)
        {
            if (toRoom != CurrentRoom)
            {
                // Left old room, arrived at new room
                var oldTile = fog?.GetTile(CurrentRoom);
                if (oldTile != null)
                    oldTile.OnDroneExit();

                CurrentRoom = toRoom;

                var newTile = fog?.GetTile(CurrentRoom);
                if (newTile != null)
                    newTile.OnDroneEnter();
            }

            if (path.Count > 0)
            {
                fromRoom = CurrentRoom;
                toRoom = path.Dequeue();
                travelDuration = GetTravelTime(fromRoom, toRoom);
                travelProgress = 0f;
            }
        }
        else
        {
            travelProgress += Time.deltaTime / travelDuration;
            travelProgress = Mathf.Clamp01(travelProgress);

            Vector3 a = RoomWorldPos(fromRoom);
            Vector3 b = RoomWorldPos(toRoom);
            transform.position = Vector3.Lerp(a, b, SmoothStep(travelProgress));
        }
    }

    void UpdateIdleAnimation()
    {
        float target = IsMoving ? 0f : 1f;
        idleBlend = Mathf.MoveTowards(idleBlend, target, Time.deltaTime * 3f);

        if (idleBlend < 0.001f)
        {
            transform.rotation = Quaternion.identity;
            hasWanderTarget = false;
            return;
        }

        float t = Time.time + idlePhase;

        // ── pick wander targets within the room ──
        if (!hasWanderTarget || wanderWait <= 0f)
        {
            wanderTarget = PickWanderPoint();
            wanderWait = Random.Range(1.5f, 4f);
            hasWanderTarget = true;
        }

        Vector3 pos = transform.position;
        Vector3 toTarget = wanderTarget - pos;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist < 0.1f)
        {
            // Close enough — wait then pick next
            wanderWait -= Time.deltaTime;
        }
        else
        {
            // Fly toward target
            float speed = 0.6f * idleBlend;
            Vector3 move = toTarget.normalized * Mathf.Min(speed * Time.deltaTime, dist);
            pos += move;
            transform.position = pos;
        }

        // ── yaw toward movement direction + wobble ──
        float yawTarget = 0f;
        if (dist > 0.15f)
            yawTarget = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        else
            yawTarget = Mathf.Sin(t * 0.4f) * 18f + Mathf.Sin(t * 0.17f) * 10f;

        float pitch = Mathf.Sin(t * 0.7f) * 3f;
        float roll  = Mathf.Cos(t * 0.53f) * 2.5f;

        // Lean into movement direction
        if (dist > 0.15f)
            pitch += 5f;

        Quaternion desired = Quaternion.Euler(
            pitch * idleBlend,
            yawTarget,
            roll * idleBlend);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * 4f);
    }

    Vector3 PickWanderPoint()
    {
        Vector3 center = RoomWorldPos(CurrentRoom);
        float roomR = map.RoomRadius(map.RoomSizeMap[CurrentRoom]);
        float maxR = roomR * 0.55f;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt(Random.Range(0f, 1f)) * maxR; // sqrt for uniform distribution
        return new Vector3(
            center.x + Mathf.Cos(angle) * r,
            center.y,
            center.z + Mathf.Sin(angle) * r);
    }

    // ── helpers ──────────────────────────────

    float GetTravelTime(Vector2Int a, Vector2Int b)
    {
        foreach (var (ca, cb, type) in map.ConnectionList)
        {
            if ((ca == a && cb == b) || (ca == b && cb == a))
            {
                switch (type)
                {
                    case HexMapGenerator.PassageType.Corridor: return 2f;
                    case HexMapGenerator.PassageType.Duct:     return 4f;
                    case HexMapGenerator.PassageType.Vent:     return 6f;
                }
            }
        }
        return 2f;
    }

    Vector3 RoomWorldPos(Vector2Int room)
    {
        Vector3 c = map.HexCenter(room);
        return new Vector3(c.x, hoverY, c.z);
    }

    float SmoothStep(float t) => t * t * (3f - 2f * t);

    // ── selection ring ───────────────────────

    void CreateSelectionRing()
    {
        selectionRing = new GameObject("SelectionRing");
        selectionRing.transform.SetParent(transform, false);
        selectionRing.transform.localPosition = new Vector3(0f, -hoverY + 0.05f, 0f);

        var mf = selectionRing.AddComponent<MeshFilter>();
        var mr = selectionRing.AddComponent<MeshRenderer>();

        mf.sharedMesh = MakeRingMesh(0.45f, 0.35f, 12);

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        ringMat = new Material(sh);
        Color col = new Color(0f, 0.85f, 1f, 0.8f);
        ringMat.color = col;
        ringMat.SetColor("_BaseColor", col);
        ringMat.SetFloat("_Surface", 1f);
        ringMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ringMat.SetOverrideTag("RenderType", "Transparent");
        ringMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        ringMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ringMat.SetInt("_ZWrite", 0);
        ringMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mr.sharedMaterial = ringMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        selectionRing.SetActive(false);
    }

    Mesh MakeRingMesh(float outerR, float innerR, int segments)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            verts.Add(new Vector3(Mathf.Cos(a) * outerR, 0f, Mathf.Sin(a) * outerR));
            verts.Add(new Vector3(Mathf.Cos(a) * innerR, 0f, Mathf.Sin(a) * innerR));
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int o = i * 2, n = i * 2 + 1;
            int no = next * 2, nn = next * 2 + 1;
            tris.Add(o);  tris.Add(no); tris.Add(n);
            tris.Add(n);  tris.Add(no); tris.Add(nn);
        }

        var m = new Mesh { name = "SelectionRing" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        return m;
    }
}
