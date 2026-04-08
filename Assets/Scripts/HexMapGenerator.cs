using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Procedurally generates a sci-fi hex-room map with three room sizes and passage types:
///   Large (cyan) → Corridor/Duct/Vent, Medium (orange) → Duct/Vent, Small (green) → Vent only.
/// All geometry is code-built meshes. No ceiling (RTS aerial view).
/// </summary>
public class HexMapGenerator : MonoBehaviour
{
    public enum PassageType { Corridor, Duct, Vent }
    public enum RoomSize    { Large, Medium, Small }

    [Header("Map Layout")]
    [SerializeField] int roomCount = 18;
    [SerializeField] int seed = 42;

    [Header("Hex Dimensions")]
    [SerializeField] float hexRadius = 5f;
    [SerializeField] [Range(1.05f, 2f)] float gridScale = 1.35f;
    [SerializeField] [Range(0.3f, 1f)] float mediumScale = 0.7f;
    [SerializeField] [Range(0.2f, 0.8f)] float smallScale = 0.45f;

    [Header("Geometry")]
    [SerializeField] float wallHeight = 2.5f;
    [SerializeField] float wallThickness = 0.18f;
    [SerializeField] float floorThickness = 0.12f;
    [SerializeField] float corridorWidth = 1.8f;
    [SerializeField] float ductWidth = 1.2f;
    [SerializeField] float ventPipeRadius = 0.22f;

    // Public layout data — populated after Generate()
    public List<Vector2Int> RoomList { get; private set; }
    public Dictionary<Vector2Int, RoomSize> RoomSizeMap { get; private set; }
    public List<(Vector2Int a, Vector2Int b, PassageType type)> ConnectionList { get; private set; }
    public float WallHeight => wallHeight;
    public float HexRadiusValue => hexRadius;
    public float GridScaleValue => gridScale;

    [Header("Colors")]
    [SerializeField] Color floorColor      = new Color(0.05f, 0.05f, 0.07f);
    [SerializeField] Color wallColor       = new Color(0.10f, 0.10f, 0.14f);
    [SerializeField] Color corridorGlow    = new Color(0f, 0.85f, 1f);
    [SerializeField] Color ductGlow        = new Color(1f, 0.55f, 0f);
    [SerializeField] Color ventGlow        = new Color(0.2f, 1f, 0.3f);
    [SerializeField] float glowIntensity   = 4f;

    // Flat-top hex: 6 axial neighbor directions
    static readonly Vector2Int[] HexDirs =
    {
        new Vector2Int( 1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int(-1,  1),
        new Vector2Int(-1,  0),
        new Vector2Int( 0, -1),
        new Vector2Int( 1, -1),
    };

    // ═══════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════

    void OnEnable()
    {
        if (transform.childCount == 0)
            Generate();
    }

    [ContextMenu("Regenerate Map")]
    public void Generate()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        var rng = new System.Random(seed);

        // --- 1. Layout ---
        HashSet<Vector2Int> rooms;
        Dictionary<Vector2Int, RoomSize> roomSizes;
        List<(Vector2Int a, Vector2Int b, PassageType type)> connections;
        BuildLayout(rng, out rooms, out roomSizes, out connections);

        // Store for external systems (fog of war, pathfinding, etc.)
        RoomList = new List<Vector2Int>(rooms);
        RoomSizeMap = roomSizes;
        ConnectionList = connections;

        // --- 2. Open edges per room ---
        var openEdges = new Dictionary<Vector2Int, Dictionary<int, PassageInfo>>();
        foreach (var r in rooms)
            openEdges[r] = new Dictionary<int, PassageInfo>();

        foreach (var (a, b, type) in connections)
        {
            if (type == PassageType.Vent) continue;
            int ea = EdgeToward(a, b);
            float gapW = PassageWidth(type);
            openEdges[a][ea]           = new PassageInfo { width = gapW, type = type };
            openEdges[b][(ea + 3) % 6] = new PassageInfo { width = gapW, type = type };
        }

        // --- 3. Mesh builders ---
        var floorMB    = new MB();
        var wallMB     = new MB();
        var ventPipeMB = new MB();
        var corrGlowMB = new MB();
        var ductGlowMB = new MB();
        var ventGlowMB = new MB();

        foreach (var room in rooms)
        {
            Vector3 c = HexCenter(room);
            float r = RoomRadius(roomSizes[room]);
            float wh = RoomWallHeight(roomSizes[room]);
            MB accentMB = roomSizes[room] == RoomSize.Large  ? corrGlowMB
                        : roomSizes[room] == RoomSize.Medium ? ductGlowMB
                        :                                      ventGlowMB;
            EmitHexFloor(floorMB, c, r);
            EmitFloorAccents(accentMB, c, r);
            EmitHexWalls(wallMB, accentMB, c, r, wh, openEdges[room]);
        }

        foreach (var (a, b, type) in connections)
        {
            if (type == PassageType.Vent)
            {
                EmitVentPipe(ventPipeMB, ventGlowMB, a, b, roomSizes[a], roomSizes[b]);
            }
            else
            {
                float w  = PassageWidth(type);
                float wh = PassageWallHeight(type);
                MB glow  = type == PassageType.Corridor ? corrGlowMB : ductGlowMB;
                EmitPassage(floorMB, wallMB, glow, a, b, w, wh, type,
                            roomSizes[a], roomSizes[b]);
            }
        }

        // --- 4. Instantiate ---
        var matFloor    = MakeMat(floorColor, 0.25f, 0.40f);
        var matWall     = MakeMat(wallColor, 0.65f, 0.50f);
        var matVentPipe = MakeMat(new Color(0.06f, 0.06f, 0.08f), 0.70f, 0.55f);
        var matCorrGlow = MakeEmissive(corridorGlow, glowIntensity);
        var matDuctGlow = MakeEmissive(ductGlow, glowIntensity);
        var matVentGlow = MakeEmissive(ventGlow, glowIntensity);

        SpawnChild("Floors",       floorMB.ToMesh("FloorMesh"),    matFloor);
        SpawnChild("Walls",        wallMB.ToMesh("WallMesh"),      matWall);
        SpawnChild("Vent_Pipes",   ventPipeMB.ToMesh("VentPipe"),  matVentPipe);
        SpawnChild("Glow_Corridor",corrGlowMB.ToMesh("CorrGlow"), matCorrGlow);
        SpawnChild("Glow_Duct",    ductGlowMB.ToMesh("DuctGlow"), matDuctGlow);
        SpawnChild("Glow_Vent",    ventGlowMB.ToMesh("VentGlow"), matVentGlow);
    }

    public float RoomRadius(RoomSize s)
    {
        switch (s)
        {
            case RoomSize.Large:  return hexRadius;
            case RoomSize.Medium: return hexRadius * mediumScale;
            case RoomSize.Small:  return hexRadius * smallScale;
            default:              return hexRadius;
        }
    }

    public float RoomWallHeight(RoomSize s)
    {
        switch (s)
        {
            case RoomSize.Large:  return wallHeight;
            case RoomSize.Medium: return wallHeight * 0.55f;
            case RoomSize.Small:  return wallHeight * 0.45f;
            default:              return wallHeight;
        }
    }

    public float PassageWidth(PassageType t)
    {
        switch (t)
        {
            case PassageType.Corridor: return corridorWidth;
            case PassageType.Duct:     return ductWidth;
            case PassageType.Vent:     return ventPipeRadius * 2f;
            default:                   return corridorWidth;
        }
    }

    float PassageWallHeight(PassageType t)
    {
        switch (t)
        {
            case PassageType.Corridor: return wallHeight * 0.88f;
            case PassageType.Duct:     return wallHeight * 0.38f;
            case PassageType.Vent:     return wallHeight * 0.65f; // pipe center height
            default:                   return wallHeight;
        }
    }

    /// <summary>Returns the top Y of a passage type (for fog overlay positioning).</summary>
    public float PassageTopY(PassageType t)
    {
        return PassageWallHeight(t);
    }

    /// <summary>Returns the top Y of a specific vent passage between two rooms.</summary>
    public float VentTopY(Vector2Int roomA, Vector2Int roomB)
    {
        float smallerWH = Mathf.Min(RoomWallHeight(RoomSizeMap[roomA]), RoomWallHeight(RoomSizeMap[roomB]));
        float pipeCenter = smallerWH * 0.5f;
        return pipeCenter + ventPipeRadius;
    }

    struct PassageInfo
    {
        public float width;
        public PassageType type;
    }

    // ═══════════════════════════════════════
    //  MAP LAYOUT GENERATION
    // ═══════════════════════════════════════

    void BuildLayout(System.Random rng,
                     out HashSet<Vector2Int> rooms,
                     out Dictionary<Vector2Int, RoomSize> roomSizes,
                     out List<(Vector2Int, Vector2Int, PassageType)> connections)
    {
        rooms = new HashSet<Vector2Int>();
        roomSizes = new Dictionary<Vector2Int, RoomSize>();
        connections = new List<(Vector2Int, Vector2Int, PassageType)>();
        var connSet = new HashSet<long>();
        var list = new List<Vector2Int>();

        // Start room is always Large
        rooms.Add(Vector2Int.zero);
        roomSizes[Vector2Int.zero] = RoomSize.Large;
        list.Add(Vector2Int.zero);

        // Expand rooms
        int tries = 0;
        while (rooms.Count < roomCount && tries < roomCount * 50)
        {
            tries++;
            Vector2Int src = list[rng.Next(list.Count)];
            Vector2Int nb  = src + HexDirs[rng.Next(6)];
            if (!rooms.Contains(nb))
            {
                rooms.Add(nb);
                RoomSize sz = RandomRoomSize(rng);
                roomSizes[nb] = sz;
                list.Add(nb);

                PassageType pt = DerivePassageType(roomSizes[src], sz);
                TryAddConn(connections, connSet, src, nb, pt);
            }
        }

        // Extra neighbor connections for loops
        foreach (var r in list)
        {
            for (int d = 0; d < 6; d++)
            {
                Vector2Int nb = r + HexDirs[d];
                if (rooms.Contains(nb) && rng.NextDouble() < 0.20)
                {
                    PassageType pt = DerivePassageType(roomSizes[r], roomSizes[nb]);
                    TryAddConn(connections, connSet, r, nb, pt);
                }
            }
        }
    }

    RoomSize RandomRoomSize(System.Random rng)
    {
        double r = rng.NextDouble();
        if (r < 0.50) return RoomSize.Large;
        if (r < 0.80) return RoomSize.Medium;
        return RoomSize.Small;
    }

    /// <summary>
    /// Passage type is determined by the smallest room on either end:
    ///   Large↔Large = Corridor, involves Medium = Duct, involves Small = Vent
    /// </summary>
    PassageType DerivePassageType(RoomSize a, RoomSize b)
    {
        RoomSize smallest = (RoomSize)Mathf.Max((int)a, (int)b);
        switch (smallest)
        {
            case RoomSize.Large:  return PassageType.Corridor;
            case RoomSize.Medium: return PassageType.Duct;
            case RoomSize.Small:  return PassageType.Vent;
            default:              return PassageType.Corridor;
        }
    }

    void TryAddConn(List<(Vector2Int, Vector2Int, PassageType)> list,
                    HashSet<long> set,
                    Vector2Int a, Vector2Int b, PassageType type)
    {
        long k = ConnKey(a, b);
        if (set.Add(k))
            list.Add((a, b, type));
    }

    static long ConnKey(Vector2Int a, Vector2Int b)
    {
        if (a.x > b.x || (a.x == b.x && a.y > b.y))
        { var t = a; a = b; b = t; }
        long ax = a.x + 500, ay = a.y + 500;
        long bx = b.x + 500, by = b.y + 500;
        return (ax << 30) | (ay << 20) | (bx << 10) | by;
    }

    // ═══════════════════════════════════════
    //  HEX MATH
    // ═══════════════════════════════════════

    public Vector3 HexCenter(Vector2Int h)
    {
        float s = hexRadius * gridScale;
        float x = s * 1.5f * h.x;
        float z = s * Mathf.Sqrt(3f) * (h.y + h.x * 0.5f);
        return new Vector3(x, 0f, z);
    }

    Vector3 Corner(Vector3 center, int i)
    {
        return Corner(center, i, hexRadius);
    }

    public Vector3 Corner(Vector3 center, int i, float r)
    {
        float a = Mathf.Deg2Rad * 60f * i;
        return center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
    }

    int EdgeToward(Vector2Int from, Vector2Int to)
    {
        Vector2Int d = to - from;
        for (int i = 0; i < 6; i++)
            if (HexDirs[i].x == d.x && HexDirs[i].y == d.y) return i;
        return 0;
    }

    /// <summary>Returns world-space wall-exit midpoints for a passage between two rooms.</summary>
    public (Vector3 midA, Vector3 midB) PassageEndpoints(Vector2Int roomA, Vector2Int roomB)
    {
        int eA = EdgeToward(roomA, roomB);
        int eB = (eA + 3) % 6;
        Vector3 cA = HexCenter(roomA);
        Vector3 cB = HexCenter(roomB);
        float rA = RoomRadius(RoomSizeMap[roomA]);
        float rB = RoomRadius(RoomSizeMap[roomB]);
        Vector3 midA = (Corner(cA, eA, rA) + Corner(cA, (eA + 1) % 6, rA)) * 0.5f;
        Vector3 midB = (Corner(cB, eB, rB) + Corner(cB, (eB + 1) % 6, rB)) * 0.5f;
        return (midA, midB);
    }

    // ═══════════════════════════════════════
    //  ROOM MESH EMITTERS
    // ═══════════════════════════════════════

    void EmitHexFloor(MB mb, Vector3 c, float r)
    {
        for (int i = 0; i < 6; i++)
            mb.Tri(c, Corner(c, (i + 1) % 6, r), Corner(c, i, r));

        Vector3 dn = Vector3.down * floorThickness;
        for (int i = 0; i < 6; i++)
        {
            Vector3 a = Corner(c, i, r);
            Vector3 b = Corner(c, (i + 1) % 6, r);
            mb.Quad(a, a + dn, b + dn, b);
        }
    }

    void EmitFloorAccents(MB mb, Vector3 c, float r)
    {
        Vector3 up = Vector3.up * 0.005f;
        float cr = r * 0.12f;
        for (int i = 0; i < 6; i++)
        {
            float a1 = Mathf.Deg2Rad * 60f * ((i + 1) % 6);
            float a2 = Mathf.Deg2Rad * 60f * i;
            mb.Tri(c + up,
                c + up + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * cr,
                c + up + new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * cr);
        }
        EmitHexRing(mb, c + up, r * 0.85f, r * 0.90f);
        EmitHexRing(mb, c + up, r * 0.96f, r * 0.99f);
    }

    void EmitHexRing(MB mb, Vector3 c, float ri, float ro)
    {
        for (int i = 0; i < 6; i++)
        {
            float a1 = Mathf.Deg2Rad * 60f * i;
            float a2 = Mathf.Deg2Rad * 60f * ((i + 1) % 6);
            Vector3 d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Vector3 d2 = new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2));
            mb.Quad(c + d1 * ro, c + d1 * ri, c + d2 * ri, c + d2 * ro);
        }
    }

    void EmitHexWalls(MB wallMB, MB glowMB, Vector3 c, float r, float rWallH,
                      Dictionary<int, PassageInfo> openEdges)
    {
        for (int i = 0; i < 6; i++)
        {
            Vector3 c0 = Corner(c, i, r);
            Vector3 c1 = Corner(c, (i + 1) % 6, r);

            if (openEdges.ContainsKey(i))
            {
                PassageInfo pi = openEdges[i];
                Vector3 mid     = (c0 + c1) * 0.5f;
                Vector3 edgeDir = (c1 - c0).normalized;
                float   hw      = pi.width * 0.5f;
                Vector3 gapA    = mid - edgeDir * hw;
                Vector3 gapB    = mid + edgeDir * hw;

                if (Vector3.Distance(c0, gapA) > 0.05f)
                {
                    EmitWallPanel(wallMB, c0, gapA, rWallH);
                    EmitWallTrim(glowMB, c0, gapA, rWallH);
                }
                if (Vector3.Distance(gapB, c1) > 0.05f)
                {
                    EmitWallPanel(wallMB, gapB, c1, rWallH);
                    EmitWallTrim(glowMB, gapB, c1, rWallH);
                }

                // Lintel (header wall) above the opening
                float openH = PassageWallHeight(pi.type);
                if (openH < rWallH - 0.01f)
                {
                    EmitWallPanel(wallMB, gapA, gapB, rWallH, openH);
                    EmitWallTrim(glowMB, gapA, gapB, rWallH);
                }
            }
            else
            {
                EmitWallPanel(wallMB, c0, c1, rWallH);
                EmitWallTrim(glowMB, c0, c1, rWallH);
            }
        }

        // Corner pillars to fill notches where wall panels meet at 120°
        float hw = wallThickness * 0.5f;
        for (int i = 0; i < 6; i++)
        {
            Vector3 corner = Corner(c, i, r);

            // Direction of the two edges meeting at this corner
            Vector3 prev = Corner(c, (i + 5) % 6, r);
            Vector3 next = Corner(c, (i + 1) % 6, r);
            Vector3 dirPrev = (corner - prev).normalized;
            Vector3 dirNext = (next - corner).normalized;

            // Perpendicular offsets (outward normals of each edge)
            Vector3 nPrev = Vector3.Cross(Vector3.up, dirPrev).normalized;
            Vector3 nNext = Vector3.Cross(Vector3.up, dirNext).normalized;

            // Build a small triangular prism to fill the gap
            Vector3 p0 = corner;
            Vector3 p1 = corner + nPrev * hw;
            Vector3 p2 = corner + nNext * hw;
            Vector3 up = Vector3.up * rWallH;

            wallMB.Quad(p0, p0 + up, p1 + up, p1);
            wallMB.Quad(p1, p1 + up, p2 + up, p2);
            wallMB.Quad(p2, p2 + up, p0 + up, p0);
            wallMB.Tri(p0 + up, p2 + up, p1 + up);
        }
    }

    // ═══════════════════════════════════════
    //  WALL GEOMETRY
    // ═══════════════════════════════════════

    /// <summary>Wall panel from floor (y=0) to height h.</summary>
    void EmitWallPanel(MB mb, Vector3 a, Vector3 b, float h)
    {
        Vector3 dir = (b - a).normalized;
        Vector3 n   = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 p   = n * (wallThickness * 0.5f);
        Vector3 up  = Vector3.up * h;

        Vector3 ao = a + p, ai = a - p;
        Vector3 bo = b + p, bi = b - p;

        mb.Quad(ao, ao + up, bo + up, bo);           // outer
        mb.Quad(bi, bi + up, ai + up, ai);           // inner
        mb.Quad(ao + up, ai + up, bi + up, bo + up); // top
        mb.Quad(ao, ai, ai + up, ao + up);           // cap a
        mb.Quad(bi, bo, bo + up, bi + up);           // cap b
    }

    /// <summary>Wall panel from minH to maxH (for lintels above duct openings).</summary>
    void EmitWallPanel(MB mb, Vector3 a, Vector3 b, float maxH, float minH)
    {
        Vector3 dir = (b - a).normalized;
        Vector3 n   = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 p   = n * (wallThickness * 0.5f);
        Vector3 lo  = Vector3.up * minH;
        Vector3 hi  = Vector3.up * maxH;

        Vector3 ao = a + p, ai = a - p;
        Vector3 bo = b + p, bi = b - p;

        mb.Quad(ao + lo, ao + hi, bo + hi, bo + lo);           // outer
        mb.Quad(bi + lo, bi + hi, ai + hi, ai + lo);           // inner
        mb.Quad(ao + hi, ai + hi, bi + hi, bo + hi);           // top
        mb.Quad(ao + lo, ai + lo, ai + hi, ao + hi);           // cap a
        mb.Quad(bi + lo, bo + lo, bo + hi, bi + hi);           // cap b
        mb.Quad(ai + lo, ao + lo, bo + lo, bi + lo);           // bottom (soffit)
    }

    void EmitWallTrim(MB mb, Vector3 a, Vector3 b, float h)
    {
        EmitWallTrimTop(mb, a, b, h);
        EmitWallStripe(mb, a, b, h);
    }

    void EmitWallTrimTop(MB mb, Vector3 a, Vector3 b, float h)
    {
        Vector3 dir = (b - a).normalized;
        Vector3 n   = Vector3.Cross(Vector3.up, dir).normalized;
        float   tw  = wallThickness * 0.5f + 0.025f;
        Vector3 p   = n * tw;
        Vector3 top = Vector3.up * h;
        Vector3 th  = Vector3.up * 0.03f;

        mb.Quad(a + p + top + th, a - p + top + th,
                b - p + top + th, b + p + top + th);
    }

    void EmitWallStripe(MB mb, Vector3 a, Vector3 b, float h)
    {
        Vector3 dir = (b - a).normalized;
        Vector3 n   = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 p2 = n * (wallThickness * 0.5f + 0.003f);
        Vector3 h1 = Vector3.up * (h * 0.82f);
        Vector3 h2 = Vector3.up * (h * 0.90f);
        mb.Quad(a + p2 + h1, a + p2 + h2, b + p2 + h2, b + p2 + h1);
    }

    // ═══════════════════════════════════════
    //  CORRIDOR / DUCT GEOMETRY
    // ═══════════════════════════════════════

    void EmitPassage(MB floorMB, MB wallMB, MB glowMB,
                     Vector2Int roomA, Vector2Int roomB,
                     float width, float wHeight, PassageType type,
                     RoomSize sizeA, RoomSize sizeB)
    {
        int eA = EdgeToward(roomA, roomB);
        int eB = (eA + 3) % 6;

        Vector3 cA = HexCenter(roomA);
        Vector3 cB = HexCenter(roomB);

        float rA = RoomRadius(sizeA);
        float rB = RoomRadius(sizeB);
        Vector3 midA = (Corner(cA, eA, rA) + Corner(cA, (eA + 1) % 6, rA)) * 0.5f;
        Vector3 midB = (Corner(cB, eB, rB) + Corner(cB, (eB + 1) % 6, rB)) * 0.5f;

        Vector3 corrDir  = (midB - midA).normalized;
        Vector3 corrPerp = Vector3.Cross(Vector3.up, corrDir).normalized;
        float   hw       = width * 0.5f;
        Vector3 off      = corrPerp * hw;

        // Floor
        floorMB.Quad(midA + off, midA - off, midB - off, midB + off);
        Vector3 dn = Vector3.down * floorThickness;
        floorMB.Quad(midA + off, midB + off, midB + off + dn, midA + off + dn);
        floorMB.Quad(midB - off, midA - off, midA - off + dn, midB - off + dn);

        // Side walls — slightly shorter than room walls for corridors, matching ceiling for enclosed
        float sideH = type == PassageType.Corridor ? wallHeight * 0.88f : wHeight;
        EmitWallPanel(wallMB, midA - off, midB - off, sideH);
        EmitWallPanel(wallMB, midB + off, midA + off, sideH);

        // Trim on passage side walls
        EmitWallTrim(glowMB, midA - off, midB - off, sideH);
        EmitWallTrim(glowMB, midB + off, midA + off, sideH);

        // Floor glow edge strips
        float sw = 0.05f;
        Vector3 up = Vector3.up * 0.005f;
        glowMB.Quad(midA + off + up,
                    midA + off - corrPerp * sw + up,
                    midB + off - corrPerp * sw + up,
                    midB + off + up);
        glowMB.Quad(midA - off + corrPerp * sw + up,
                    midA - off + up,
                    midB - off + up,
                    midB - off + corrPerp * sw + up);

        // Ceiling slab for enclosed passages (duct + vent)
        if (type != PassageType.Corridor)
        {
            Vector3 ceilY = Vector3.up * wHeight;
            float ceilT = wallThickness * 0.5f;
            Vector3 cUp = Vector3.up * ceilT;
            // Top face
            floorMB.Quad(midA - off + ceilY + cUp, midA + off + ceilY + cUp,
                         midB + off + ceilY + cUp, midB - off + ceilY + cUp);
            // Bottom face (soffit, visible when looking in)
            floorMB.Quad(midA + off + ceilY, midA - off + ceilY,
                         midB - off + ceilY, midB + off + ceilY);
        }
    }

    // ═══════════════════════════════════════
    //  VENT PIPE GEOMETRY
    // ═══════════════════════════════════════

    void EmitVentPipe(MB pipeMB, MB glowMB,
                      Vector2Int roomA, Vector2Int roomB,
                      RoomSize sizeA, RoomSize sizeB)
    {
        int eA = EdgeToward(roomA, roomB);
        int eB = (eA + 3) % 6;

        Vector3 cA = HexCenter(roomA);
        Vector3 cB = HexCenter(roomB);

        float rA = RoomRadius(sizeA);
        float rB = RoomRadius(sizeB);
        Vector3 midA = (Corner(cA, eA, rA) + Corner(cA, (eA + 1) % 6, rA)) * 0.5f;
        Vector3 midB = (Corner(cB, eB, rB) + Corner(cB, (eB + 1) % 6, rB)) * 0.5f;

        // Extend pipe past the walls into rooms so entrances are visible
        Vector3 pipeHoriz = (midB - midA).normalized;
        float extend = wallThickness * 0.5f + ventPipeRadius * 0.5f;
        Vector3 pipeA = midA - pipeHoriz * extend;
        Vector3 pipeB = midB + pipeHoriz * extend;

        // Pipe height = half the shorter room's wall height
        float smallerWH = Mathf.Min(RoomWallHeight(sizeA), RoomWallHeight(sizeB));
        float pipeY = smallerWH * 0.5f;
        Vector3 startA = new Vector3(pipeA.x, pipeY, pipeA.z);
        Vector3 startB = new Vector3(pipeB.x, pipeY, pipeB.z);

        EmitPipeSegment(pipeMB, startA, startB);

        Vector3 pipeDir = startB - startA;

        // Glow bands at the room-side entrances
        EmitPipeBand(glowMB, startA, pipeDir);
        EmitPipeBand(glowMB, startB, pipeDir);

        // Interior light rings evenly spaced along the pipe
        float pipeLen = pipeDir.magnitude;
        float spacing = ventPipeRadius * 6f;
        int ringCount = Mathf.Max(1, Mathf.FloorToInt(pipeLen / spacing));
        for (int i = 1; i <= ringCount; i++)
        {
            float t = i / (float)(ringCount + 1);
            Vector3 pos = Vector3.Lerp(startA, startB, t);
            EmitPipeRing(glowMB, pos, pipeDir, ventPipeRadius * 1.02f);
        }
    }

    void EmitPipeSegment(MB mb, Vector3 from, Vector3 to)
    {
        int seg = 8;
        float r = ventPipeRadius;
        Vector3 dir = (to - from).normalized;

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.99f)
            up = Vector3.right;
        Vector3 right = Vector3.Cross(dir, up).normalized;
        Vector3 fwd   = Vector3.Cross(right, dir).normalized;

        Vector3[] ringA = new Vector3[seg];
        Vector3[] ringB = new Vector3[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = Mathf.PI * 2f * i / seg;
            Vector3 offset = (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * r;
            ringA[i] = from + offset;
            ringB[i] = to   + offset;
        }

        for (int i = 0; i < seg; i++)
        {
            int n = (i + 1) % seg;
            mb.Quad(ringA[i], ringB[i], ringB[n], ringA[n]);
        }

        for (int i = 1; i < seg - 1; i++)
        {
            mb.Tri(ringA[0], ringA[i + 1], ringA[i]);
            mb.Tri(ringB[0], ringB[i], ringB[i + 1]);
        }
    }

    /// <summary>Glowing band wrapping around the pipe — visible from all angles.</summary>
    void EmitPipeBand(MB mb, Vector3 center, Vector3 pipeDir)
    {
        float r = ventPipeRadius * 1.15f; // slightly larger than pipe so it's on the outside
        float bandW = ventPipeRadius * 1.2f;
        Vector3 dir = pipeDir.normalized;
        Vector3 halfD = dir * bandW * 0.5f;

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.99f)
            up = Vector3.right;
        Vector3 right = Vector3.Cross(dir, up).normalized;
        Vector3 fwd   = Vector3.Cross(right, dir).normalized;

        int seg = 8;
        for (int i = 0; i < seg; i++)
        {
            float a1 = Mathf.PI * 2f * i / seg;
            float a2 = Mathf.PI * 2f * ((i + 1) % seg) / seg;
            Vector3 d1 = (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * r;
            Vector3 d2 = (right * Mathf.Cos(a2) + fwd * Mathf.Sin(a2)) * r;
            // Outer face of the band
            mb.Quad(center + d1 - halfD, center + d1 + halfD,
                    center + d2 + halfD, center + d2 - halfD);
        }
    }

    /// <summary>Thin glowing ring inside the pipe — narrower than entrance bands.</summary>
    void EmitPipeRing(MB mb, Vector3 center, Vector3 pipeDir, float r)
    {
        float bandW = ventPipeRadius * 0.3f;
        Vector3 dir = pipeDir.normalized;
        Vector3 halfD = dir * bandW * 0.5f;

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.99f)
            up = Vector3.right;
        Vector3 right = Vector3.Cross(dir, up).normalized;
        Vector3 fwd   = Vector3.Cross(right, dir).normalized;

        int seg = 8;
        for (int i = 0; i < seg; i++)
        {
            float a1 = Mathf.PI * 2f * i / seg;
            float a2 = Mathf.PI * 2f * ((i + 1) % seg) / seg;
            Vector3 d1 = (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * r;
            Vector3 d2 = (right * Mathf.Cos(a2) + fwd * Mathf.Sin(a2)) * r;
            mb.Quad(center + d1 - halfD, center + d1 + halfD,
                    center + d2 + halfD, center + d2 - halfD);
        }
    }

    // ═══════════════════════════════════════
    //  MATERIALS
    // ═══════════════════════════════════════

    Material MakeMat(Color c, float metallic, float smoothness)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh);
        m.color = c;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    Material MakeEmissive(Color c, float intensity)
    {
        var m = MakeMat(c * 0.30f, 0.20f, 0.80f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * intensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return m;
    }

    // ═══════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════

    void SpawnChild(string name, Mesh mesh, Material mat)
    {
        if (mesh.vertexCount == 0) return;
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ═══════════════════════════════════════
    //  MESH BUILDER
    // ═══════════════════════════════════════

    class MB
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<int>     t = new List<int>();

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65535)
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
