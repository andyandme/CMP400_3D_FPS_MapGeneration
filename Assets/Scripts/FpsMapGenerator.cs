using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;


public class FpsMapGenerator : MonoBehaviour
{

    public System.Action OnMapRegenerated;
    //Prefabs Face north by default
    public enum Direction
    {
        None,
        North,
        East,
        South,
        West
    }

    public enum GenerationMode
    { 
    ManuallyMadeMap1, //Premade Map 
    ProcedurallyGeneratedMap // Generated map 
  
    }

    [System.Serializable]
    public class TilePrefab
    {
        public int groupId;          // Tile Group ID
        public GameObject prefab;    
    }

    [System.Serializable]
    public struct TileCode
    {
        public int group;        // Tile Group ID
        public Direction dir;    // Rotation

        public TileCode(int group, Direction dir = Direction.None)
        {
            this.group = group;
            this.dir = dir;
        }

        public bool IsEmpty => group == 0;

        public static TileCode Empty => new TileCode(0, Direction.None);
    }


   
    [Header("Procedural: Exterior Cull")]
    [Range(0, 4)] public int minWalkableNeighborsToKeep = 1;  
    [Range(0, 10)] public int exteriorCullIterations = 1;
   


 

    [Header("Grid Settings")]
    public int width = 24;
    public int depth = 24;
    public int levels = 3;          // 0 = ground, 1 = mid, 2 = top
    public float moduleSize = 10f;  // tile size in world units






    [Header("Generation Mode")]
    public GenerationMode generationMode = GenerationMode.ManuallyMadeMap1;

    public int proceduralFloorGroupId = 10;

    public bool keepBorderBlocked = true;


    //public int buildingAttempts = 30;

    

    [Range(0, 50)]
    public int extraRooms = 8;

    [Range(1,6)]
    public int RoomMaxSize = 4;

    public int RoomMinSize = 2;

    [Range(0, 2)]
    public int corridorThickness = 2;


    [Header("Procedural: buildings")]

    public bool enableBuildings = true;

    public int buildingAttempts = 60;
    public int buildingMinSize = 3;

    [Range(0, 4)] public int buildingClearance = 1;
    [Range(0f, 1)] public float clearanceWalkableFraction = 0.8f;

    private bool[,] buildingMask;



    [Header("DEBUG: Force Validation Failure")]
    public bool forceConnectivityFail = false;
    [Range(0, 999)] public int forceBlockRowZ = 8; // Row index to block


    [Header("Validation")]
    [Range(1, 100)]
    public int maxGenerationAttempts = 25;

    [Range(0f, 1f)]
    public float minReachableFraction = 0.95f; // represent percentage of cells connected to spawn 1

    [Range(0, 999)]
    public int minSpawnPathLength = 12;

    public bool logValidationDetails = true;

    [Header("Procedural: Bitmasking Walls")]
    public int floorNoWallsGroupId = 10;
    public int floorOneWallGroupId = 11; //N facing
    public int floorTwoWallsOppositeGroupId = 12; //N+S facing walls at default
    public int floorTwoWallsCornerGroupId = 13; //N+W facing by default
    public int floorThreeWallsGroupId = 14;
    public int floorFourWallsGroupId = 16;


    [Header("Prefabs")]
    public TilePrefab[] tilePrefabs;
    public Transform mapParent;

    [Header("Doors")]
    public int doorGroupId = 15;
    public int doorsPerBuilding = 2;
    public int doorCornerLeftWallGroupId = 151;  
    public int doorCornerRightWallGroupId = 152; 


    [Header("Cover Layer")]

    public bool enableCoverLayer = true;
    public TilePrefab[] coverPrefabs; // a seperate layer for the cover to be implemented
    public Transform coverParent;
    public Vector3 coverWorldOffset = Vector3.zero;


    [Header("Cover Layer Spawn Points")]
    public int player1SpawnGroupId = 101;
    public int player2SpawnGroupId = 102;
    public int spawnLevel = 0;
    public bool placeSpawnsForProcedural = true;

    [Header("Seed")]
    public bool useRandomSeed = false;
    public int seed = 12345;


    [Header("Regenerate")]
    public bool generateOnStart = true;
    public bool regenWithRkey = true;
    private int lastUsedSeed;
   


    private TileCode[,,] layout;                   // [x, z, level]
    private TileCode[,,] coverLayout;              // [x, z, level]
    private bool[,] walkable;

    private Dictionary<int, GameObject> prefabLookup;
    private Dictionary<int, GameObject> coverPrefabLookup;

    private System.Random rng;

    private void Awake()
    {
        BuildPrefabLookup();
        BuildCoverPrefabLookup();
        EnsureCoverParent();
    }

    private void Start()
    {
        if (generateOnStart)
        {
            Regenerate();
        }
    }


    private void Update()
    {
        if (regenWithRkey && Application.isPlaying && Input.GetKeyDown(KeyCode.R))
        {
            Regenerate();
        }
    }
    

    private bool IsFloorGroup(int group)
    {
        return group == floorNoWallsGroupId
            || group == floorOneWallGroupId
            || group == floorTwoWallsOppositeGroupId
            || group == floorTwoWallsCornerGroupId
            || group == floorThreeWallsGroupId
            || group == floorFourWallsGroupId;
    }

    private int WalkableNeighborCount(bool[,] g, int x, int z)
    {
        int c = 0;
        if (IsWalkable(g, x, z + 1)) c++;
        if (IsWalkable(g, x + 1, z)) c++;
        if (IsWalkable(g, x, z - 1)) c++;
        if (IsWalkable(g, x - 1, z)) c++;
        return c;
    }

    //spawn Requires a floor and at least two exits
    private bool IsValidSpawnCell(bool[,] g, int x, int z)
    {
        if (!IsWalkable(g, x, z)) return false;
        if (layout == null) return false;

        var t = layout[x, z, 0];
        if (t.IsEmpty) return false;
        if (!IsFloorGroup(t.group)) return false;


        if (WalkableNeighborCount(g, x, z) < 2) return false;

        return true;
    }


    [ContextMenu("Regenerate")]
    public void Regenerate()
    {
        int attempts = 0;
        bool accepted = false;
        string lastRejectReason = "";

        //Base seed stays stable during attempt loop
        int baseSeed = seed; 

        for (attempts = 1; attempts <= maxGenerationAttempts; attempts++)
        {
            int attemptSeed;

            //Random seed per attempt
            if (useRandomSeed)
            {
                attemptSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                seed = attemptSeed; 
            }
            else
            {
                attemptSeed = baseSeed + (attempts - 1);
            }
            //Fixed seed: base and attempt offset
            lastUsedSeed = attemptSeed;
            rng = new System.Random(lastUsedSeed);

            AllocateLayout();
            if (enableCoverLayer) AllocateCoverLayout();


            if (generationMode == GenerationMode.ManuallyMadeMap1)
            {
                InitializeLayoutManually();
                if (enableCoverLayer)
                    InitializeCoverLayoutManually();

                // For manual maps skip validation
                accepted = true;
                break;
            }
            if (generationMode == GenerationMode.ProcedurallyGeneratedMap)
            {
                
                walkable = GenerateWalkablePlan();
                CullExteriorWalkable(walkable);

                
                if (enableBuildings)
                {
                    ApplyBuildingsToWalkable(walkable);
                }
            }


            if (forceConnectivityFail)
            {
                ForceDisconnectByBlockingRow(walkable, forceBlockRowZ);

            }

            InitializeLayoutFromWalkable(walkable);
         


            if (enableCoverLayer && placeSpawnsForProcedural)
            {
                PlaceSpawnPointsFromWalkable(walkable);
            }

            if (enableBuildings)
                PlaceDoorsForAllBuildings(walkable);


            // Validate
            accepted = ValidateConnectivityAndQuality(walkable, out lastRejectReason);

            if (accepted)
                break;

            if (logValidationDetails)
                Debug.LogWarning($"[FpsMapGenerator] Reject attempt {attempts}/{maxGenerationAttempts} Seed={lastUsedSeed} Reason: {lastRejectReason}");

        }

        if (!accepted)
        {
            Debug.LogError($"[FpsMapGenerator] FAILED to generate a valid map after {maxGenerationAttempts} atempts. Last reason for rejection: {lastRejectReason} ");
        }




        BuildPrefabLookup();
        BuildCoverPrefabLookup();
        EnsureCoverParent();


        //clear old children
        ClearSpawnedChildren(mapParent != null ? mapParent : transform);

        if (enableCoverLayer)
        {
            ClearSpawnedChildren(coverParent != null ? coverParent : transform);
        }



        BuildGeometry(layout, mapParent != null ? mapParent : transform, prefabLookup, Vector3.zero);

        if (enableCoverLayer)
        {
            BuildGeometry(coverLayout, coverParent, coverPrefabLookup, coverWorldOffset);
        }

        Debug.Log($"[FpsMapGenerator] Regenerated. Seed={lastUsedSeed} (useRandomSeed={useRandomSeed})");


        int subs = (OnMapRegenerated == null) ? 0 : OnMapRegenerated.GetInvocationList().Length;
        Debug.Log($"[FpsMapGenerator] Invoking OnMapRegenerated. Subscribers={subs}");

        OnMapRegenerated?.Invoke();
    }


    private bool[,] GenerateWalkablePlan()
    {
        bool[,] g = new bool[width, depth];

        int minX = keepBorderBlocked ? 1 : 0;
        int maxX = keepBorderBlocked ? width - 2 : width - 1;
        int minZ = keepBorderBlocked ? 1 : 0;
        int maxZ = keepBorderBlocked ? depth - 2 : depth - 1;

        if (maxX < minX || maxZ < minZ)
        {
            Debug.LogWarning("Grid too small for keepBorderBlocked. Disable it or increase width/depth.");
            return g;
        }

        int targetRooms = Mathf.Clamp(extraRooms, 3, 50);
        int roomMin = Mathf.Clamp(RoomMinSize, 2, RoomMaxSize);
        int roomMax = Mathf.Max(roomMin, RoomMaxSize);

        List<RectInt> rooms = new List<RectInt>(targetRooms);

        int attempts = targetRooms * 10;

        for (int i = 0; i < attempts && rooms.Count < targetRooms; i++)
        {
            int rw = rng.Next(roomMin, roomMax + 1);
            int rh = rng.Next(roomMin, roomMax + 1);

            if (rw > (maxX - minX + 1) || rh > (maxZ - minZ + 1))
                continue;

            int rx = rng.Next(minX, maxX - rw + 2);
            int rz = rng.Next(minZ, maxZ - rh + 2);

            RectInt candidate = new RectInt(rx, rz, rw, rh);

            // 1 tile padding avoids merges
            RectInt padded = new RectInt(candidate.xMin - 1, candidate.yMin - 1, candidate.width + 2, candidate.height + 2);

            bool overlaps = false;
            foreach (var r in rooms)
            {
                if (padded.Overlaps(r)) { overlaps = true; break; }
            }
            if (overlaps) continue;

            rooms.Add(candidate);
            CarveRect(g, candidate.xMin, candidate.yMin, candidate.width, candidate.height, minX, maxX, minZ, maxZ);
        }

      
        if (rooms.Count == 0)
        {
            int rw = Mathf.Clamp(width / 2, 4, width);
            int rh = Mathf.Clamp(depth / 2, 4, depth);
            int rx = (width - rw) / 2;
            int rz = (depth - rh) / 2;
            CarveRect(g, rx, rz, rw, rh, minX, maxX, minZ, maxZ);
            return g;
        }

        
        rooms.Sort((a, b) => a.center.x.CompareTo(b.center.x));

        for (int i = 0; i < rooms.Count - 1; i++)
        {
            Vector2Int a = Vector2Int.RoundToInt(rooms[i].center);
            Vector2Int b = Vector2Int.RoundToInt(rooms[i + 1].center);
            CarveCorridorThick(g, a, b, corridorThickness, minX, maxX, minZ, maxZ);
        }

        return g;
    }

    private void CarveCorridorThick(bool[,] g, Vector2Int a, Vector2Int b, int thickness, int minX, int maxX, int minZ, int maxZ)
    {
        int x = a.x;
        int z = a.y;


        bool horizontalFirst = rng.Next(0, 2) == 0;

        if (horizontalFirst)
        {
            while (x != b.x)
            {
                CarveThick(g, x, z, thickness, minX, maxX, minZ, maxZ);
                x += Math.Sign(b.x - x);
            }
            while (z != b.y)
            {
                CarveThick(g, x, z, thickness, minX, maxX, minZ, maxZ);
                z += Math.Sign(b.y - z);
            }
        }
        else
        {
            while (z != b.y)
            {
                CarveThick(g, x, z, thickness, minX, maxX, minZ, maxZ);
                z += Math.Sign(b.y - z);
            }
            while (x != b.x)
            {
                CarveThick(g, x, z, thickness, minX, maxX, minZ, maxZ);
                x += Math.Sign(b.x - x);
            }
        }

        CarveThick(g, x, z, thickness, minX, maxX, minZ, maxZ);
    }

    private void CarveThick(bool[,] g, int x, int z, int thickness, int minX, int maxX, int minZ, int maxZ)
    {
        int r = Mathf.Max(0, thickness);
        for (int dz = -r; dz <= r; dz++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = x + dx;
                int cz = z + dz;

                if (cx < minX || cx > maxX || cz < minZ || cz > maxZ)
                    continue;

                g[cx, cz] = true;
            }
        }
    }

    private void CarveRect(bool[,] g, int x, int z, int w, int h, int minX, int maxX, int minZ, int maxZ)
    {
        for (int dz = 0; dz < h; dz++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                int cx = x + dx;
                int cz = z + dz;
                if (cx < minX || cx > maxX || cz < minZ || cz > maxZ) continue;
                g[cx, cz] = true;
            }
        }
    }


    private void ApplyBuildingsToWalkable(bool[,] g)
    {
        if (g == null) return;

        if (buildingMask == null || buildingMask.GetLength(0) != width || buildingMask.GetLength(1) != depth)
            buildingMask = new bool[width, depth];
        else
            Array.Clear(buildingMask, 0, buildingMask.Length);

        int minX = keepBorderBlocked ? 1 : 0;
        int maxX = keepBorderBlocked ? width - 2 : width - 1;
        int minZ = keepBorderBlocked ? 1 : 0;
        int maxZ = keepBorderBlocked ? depth - 2 : depth - 1;

        int buildingMaxSize = Mathf.Max(buildingMinSize, RoomMaxSize);
        int clearance = Mathf.Max(0, buildingClearance);

        int placed = 0;

        for (int attempt = 0; attempt < buildingAttempts; attempt++)
        {
            int bw = rng.Next(buildingMinSize, buildingMaxSize + 1);
            int bh = rng.Next(buildingMinSize, buildingMaxSize + 1);

            if (bw > (maxX - minX + 1) || bh > (maxZ - minZ + 1))
                continue;

            int x0 = rng.Next(minX, maxX - bw + 2);
            int z0 = rng.Next(minZ, maxZ - bh + 2);

            if (!HasClearanceOnWalkable(g, x0, z0, bw, bh, clearance, minX, maxX, minZ, maxZ))
                continue;

            for (int z = z0; z < z0 + bh; z++)
            {
                for (int x = x0; x < x0 + bw; x++)
                {
                    g[x, z] = false;            // not walkable
                    buildingMask[x, z] = true;  // building cell (rendered later)
                }
            }

            placed++;
        }

        Debug.Log($"[FpsMapGenerator] Buildings placed: {placed}");
    }

    private bool HasClearanceOnWalkable(bool[,] g, int x0, int z0, int bw, int bh, int clearance,
                                        int minX, int maxX, int minZ, int maxZ)
    {
        int xMin = x0 - clearance;
        int zMin = z0 - clearance;
        int xMax = x0 + bw - 1 + clearance;
        int zMax = z0 + bh - 1 + clearance;

        if (xMin < minX || zMin < minZ || xMax > maxX || zMax > maxZ)
            return false;

        // Core must be fully walkable
        for (int z = z0; z < z0 + bh; z++)
            for (int x = x0; x < x0 + bw; x++)
                if (!g[x, z]) return false;

        // Ring: mostly walkable
        if (clearance > 0)
        {
            int ringTotal = 0;
            int ringWalkable = 0;

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    bool inCore = (x >= x0 && x < x0 + bw && z >= z0 && z < z0 + bh);
                    if (inCore) continue;

                    ringTotal++;
                    if (g[x, z]) ringWalkable++;
                }
            }

            float frac = (ringTotal == 0) ? 1f : (float)ringWalkable / ringTotal;
            if (frac < clearanceWalkableFraction)
                return false;
        }

        return true;
    }

    private void ForceDisconnectByBlockingRow(bool[,] g, int zRow)
    {
        if (g == null) return;
        if (zRow < 0 || zRow >= depth) return;

        for (int x = 0; x < width; x++)
            g[x, zRow] = false;
    }

    private void CullExteriorWalkable(bool[,] g)
    {
        if (g == null) return;

        for (int iter = 0; iter < exteriorCullIterations; iter++)
        {
            bool[,] next = (bool[,])g.Clone();
            bool changed = false;

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < depth; z++)
                {
                    if (!g[x, z]) continue;
                    //COunts open sides, including building footprints
                    int n =
                    (IsOpenForWallMask(g, x, z + 1) ? 1 : 0) +
                    (IsOpenForWallMask(g, x + 1, z) ? 1 : 0) +
                    (IsOpenForWallMask(g, x, z - 1) ? 1 : 0) +
                    (IsOpenForWallMask(g, x - 1, z) ? 1 : 0);

                    if (n < minWalkableNeighborsToKeep)
                    {
                        next[x, z] = false;
                        changed = true;
                    }
                }
            }

            CopyGrid(next, g);
            if (!changed) break;
        }
    }

    private bool[,] CopyGrid(bool[,] src, bool[,] dst)
    {
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                dst[x, z] = src[x, z];
        return dst;
    }

  
    private void InitializeLayoutFromWalkable(bool[,] g)
    {
        if (g == null)
        {
            Debug.LogWarning("InitializeLayoutFromWalkable: walkable grid is null");
            return;
        }

        
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                layout[x, z, 0] = TileCode.Empty;

        //Walkable tiles (skip building cells)
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (buildingMask != null && buildingMask[x, z])
                    continue;

                if (!g[x, z])
                    continue;

                bool n = IsOpenForWallMask(g, x, z + 1);
                bool e = IsOpenForWallMask(g, x + 1, z);
                bool s = IsOpenForWallMask(g, x, z - 1);
                bool w = IsOpenForWallMask(g, x - 1, z);

                bool wallN = !n;
                bool wallE = !e;
                bool wallS = !s;
                bool wallW = !w;

                int wallCount =
                    (wallN ? 1 : 0) +
                    (wallE ? 1 : 0) +
                    (wallS ? 1 : 0) +
                    (wallW ? 1 : 0);

                layout[x, z, 0] = BuildTileFromWalls(wallN, wallE, wallS, wallW, wallCount);
            }
        }

        // Building tiles from buildingMask adjecency 
        if (buildingMask != null)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < depth; z++)
                {
                    if (!buildingMask[x, z])
                        continue;

                    bool bn = IsBuilding(x, z + 1);
                    bool be = IsBuilding(x + 1, z);
                    bool bs = IsBuilding(x, z - 1);
                    bool bw = IsBuilding(x - 1, z);

                    // For buildings: wall where neighbor is NOT building
                    bool wallN = !bn;
                    bool wallE = !be;
                    bool wallS = !bs;
                    bool wallW = !bw;

                    int wallCount =
                        (wallN ? 1 : 0) +
                        (wallE ? 1 : 0) +
                        (wallS ? 1 : 0) +
                        (wallW ? 1 : 0);

                    layout[x, z, 0] = BuildTileFromWalls(wallN, wallE, wallS, wallW, wallCount);
                }
            }
        }
    }

    private bool IsBuilding(int x, int z)
    {
        if (buildingMask == null) return false;
        if (x < 0 || x >= width || z < 0 || z >= depth) return false;
        return buildingMask[x, z];
    }
    //Open includes walkable and building footprint
    private bool IsOpenForWallMask(bool[,] g, int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth)
            return false;

        if (g != null && g[x, z]) return true;
        if (buildingMask != null && buildingMask[x, z]) return true;

        return false;
    }

    private bool IsWalkable(bool[,] g, int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth)
            return false;
        return g[x, z];
    }
    //Converts wall booleans into group and rotation
    private TileCode BuildTileFromWalls(bool wallN, bool wallE, bool wallS, bool wallW, int wallCount)
    {
        if (wallCount == 0)
            return new TileCode(floorNoWallsGroupId, Direction.North);

        if (wallCount == 1)
        {
            if (wallN) return new TileCode(floorOneWallGroupId, Direction.North);
            if (wallE) return new TileCode(floorOneWallGroupId, Direction.East);
            if (wallS) return new TileCode(floorOneWallGroupId, Direction.South);
            return new TileCode(floorOneWallGroupId, Direction.West);
        }

        if (wallCount == 2)
        {
            bool opposite = (wallN && wallS) || (wallE && wallW);

            if (opposite)
            {
                if (wallN && wallS) return new TileCode(floorTwoWallsOppositeGroupId, Direction.North);
                return new TileCode(floorTwoWallsOppositeGroupId, Direction.East);
            }
            else
            {
                if (wallN && wallW) return new TileCode(floorTwoWallsCornerGroupId, Direction.North); // N+W
                if (wallN && wallE) return new TileCode(floorTwoWallsCornerGroupId, Direction.East);  // N+E
                if (wallE && wallS) return new TileCode(floorTwoWallsCornerGroupId, Direction.South); // E+S
                return new TileCode(floorTwoWallsCornerGroupId, Direction.West);  // S+W
            }
        }

        if (wallCount == 3)
        {
            if (!wallN) return new TileCode(floorThreeWallsGroupId, Direction.North);
            if (!wallE) return new TileCode(floorThreeWallsGroupId, Direction.East);
            if (!wallS) return new TileCode(floorThreeWallsGroupId, Direction.South);
            return new TileCode(floorThreeWallsGroupId, Direction.West);
        }

        return new TileCode(floorFourWallsGroupId, Direction.North);
    }

    private void EnsureCoverParent()
    {
        if (!enableCoverLayer)
            return;

        if (coverParent != null)
            return;

        Transform existing = transform.Find("CoverParent");
        if (existing != null)
        {
            coverParent = existing;
            return;
        }

        GameObject go = new GameObject("CoverParent");
        go.transform.SetParent(transform, false);
        coverParent = go.transform;
    }

    private void ClearSpawnedChildren(Transform parent)
    {
        if (parent == null) return;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(parent.GetChild(i).gameObject);
            else
                Destroy(parent.GetChild(i).gameObject);
#else
            Destroy(parent.GetChild(i).gameObject);
#endif
        }
    }

    private void AllocateLayout()
    {
        layout = new TileCode[width, depth, levels];

        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                for (int l = 0; l < levels; l++)
                    layout[x, z, l] = TileCode.Empty;
    }

    private void AllocateCoverLayout()
    {
        coverLayout = new TileCode[width, depth, levels];

        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                for (int l = 0; l < levels; l++)
                    coverLayout[x, z, l] = TileCode.Empty;
    }

    private void BuildPrefabLookup()
    {
        if (prefabLookup == null)
            prefabLookup = new Dictionary<int, GameObject>();
        else
            prefabLookup.Clear();

        if (tilePrefabs == null) return;

        foreach (var entry in tilePrefabs)
        {
            if (entry == null) continue;
            if (entry.prefab == null) continue;

            prefabLookup[entry.groupId] = entry.prefab;
        }
    }

    private void BuildCoverPrefabLookup()
    {
        if (coverPrefabLookup == null)
            coverPrefabLookup = new Dictionary<int, GameObject>();
        else
            coverPrefabLookup.Clear();

        if (coverPrefabs == null) return;

        foreach (var entry in coverPrefabs)
        {
            if (entry == null) continue;
            if (entry.prefab == null) continue;

            coverPrefabLookup[entry.groupId] = entry.prefab;
        }
    }
    public bool TryGetSpawnWorldPosition(int playerIndex, out Vector3 worldPos)
    {
        worldPos = Vector3.zero;

        if (!enableCoverLayer || coverLayout == null)
            return false;

        int targetGroup = (playerIndex == 1) ? player1SpawnGroupId : player2SpawnGroupId;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (coverLayout[x, z, spawnLevel].group == targetGroup)
                {

                    Vector3 basePos = GridToWorld(x, 0, z);



                    worldPos = basePos;
                    return true;
                }
            }
        }

        return false;
    }


    private void PlaceSpawnPointsFromWalkable(bool[,] g)
    {
        if (g == null)
        {
            Debug.LogWarning("PlaceSpawnPointsFromWalkable: walkable grid is null");
            return;
        }
        if (coverLayout == null)
        {
            Debug.LogWarning("PlaceSpawnPointsFromWalkable: coverLayout is null");
            return;
        }

        ClearSpawnMarkersInCover();

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                if (IsValidSpawnCell(g, x, z))
                    candidates.Add(new Vector2Int(x, z));

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[FpsMapGenerator] No valid spawn candidates found (walkable+floor). Check your floor group IDs / prefabs.");
            return;
        }

        Vector2Int start = candidates[rng.Next(0, candidates.Count)];

        var bfs1 = BFSFarthestValidCandidate(g, start);
        Vector2Int A = bfs1.farthest;

        var bfs2 = BFSFarthestValidCandidate(g, A);
        Vector2Int B = bfs2.farthest;

        coverLayout[A.x, A.y, spawnLevel] = new TileCode(player1SpawnGroupId, Direction.North);
        coverLayout[B.x, B.y, spawnLevel] = new TileCode(player2SpawnGroupId, Direction.North);

        Debug.Log($"[FpsMapGenerator] Spawn cells chosen: P1={A}, P2={B}, dist={bfs2.farthestDist}");
    }

    private (Vector2Int farthest, int farthestDist) BFSFarthestValidCandidate(bool[,] g, Vector2Int start)
    {
        int[,] dist = new int[width, depth];
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                dist[x, z] = -1;

        Queue<Vector2Int> q = new Queue<Vector2Int>();

        if (!IsWalkable(g, start.x, start.y))
            return (start, 0);

        dist[start.x, start.y] = 0;
        q.Enqueue(start);

        Vector2Int farthest = start;
        int farthestDist = 0;

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            int cd = dist[cur.x, cur.y];

            if (IsValidSpawnCell(g, cur.x, cur.y) && cd >= farthestDist)
            {
                farthestDist = cd;
                farthest = cur;
            }

            TryVisit(g, dist, q, cur.x, cur.y + 1, cd + 1);
            TryVisit(g, dist, q, cur.x + 1, cur.y, cd + 1);
            TryVisit(g, dist, q, cur.x, cur.y - 1, cd + 1);
            TryVisit(g, dist, q, cur.x - 1, cur.y, cd + 1);
        }

        return (farthest, farthestDist);
    }

    private void TryVisit(bool[,] g, int[,] dist, Queue<Vector2Int> q, int x, int z, int nd)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth) return;
        if (!g[x, z]) return;
        if (dist[x, z] != -1) return;

        dist[x, z] = nd;
        q.Enqueue(new Vector2Int(x, z));
    }

    private bool TryGetSpawnCell(int playerIndex, out Vector2Int cell)
    {
        cell = default;

        if (!enableCoverLayer || coverLayout == null)
            return false;

        int targetGroup = (playerIndex == 1) ? player1SpawnGroupId : player2SpawnGroupId;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (coverLayout[x, z, spawnLevel].group == targetGroup)
                {
                    cell = new Vector2Int(x, z);
                    return true;
                }
            }
        }
        return false;
    }

    private bool BFSConnectivity(bool[,] g, Vector2Int start, Vector2Int goal, out int goalDist, out int reachableCount)
    {
        goalDist = -1;
        reachableCount = 0;

        if (g == null) return false;
        if (!IsWalkable(g, start.x, start.y)) return false;
        if (!IsWalkable(g, goal.x, goal.y)) return false;

        int[,] dist = new int[width, depth];
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                dist[x, z] = -1;

        Queue<Vector2Int> q = new Queue<Vector2Int>();
        dist[start.x, start.y] = 0;
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            reachableCount++;

            int cd = dist[cur.x, cur.y];
            if (cur == goal)
                goalDist = cd;

            TryVisit(g, dist, q, cur.x, cur.y + 1, cd + 1);
            TryVisit(g, dist, q, cur.x + 1, cur.y, cd + 1);
            TryVisit(g, dist, q, cur.x, cur.y - 1, cd + 1);
            TryVisit(g, dist, q, cur.x - 1, cur.y, cd + 1);
        }

        return goalDist >= 0;
    }

    private bool ValidateConnectivityAndQuality(bool[,] g, out string reason)
    {
        reason = "";

        
        if (generationMode != GenerationMode.ProcedurallyGeneratedMap)
            return true;

        if (g == null)
        {
            reason = "walkable grid is null";
            return false;
        }

        if (!TryGetSpawnCell(1, out Vector2Int s1))
        {
            reason = "missing spawn1 marker (101) in coverLayout";
            return false;
        }
        if (!TryGetSpawnCell(2, out Vector2Int s2))
        {
            reason = "missing spawn2 marker (102) in coverLayout";
            return false;
        }

        int totalWalkable = 0;
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                if (g[x, z]) totalWalkable++;

        if (totalWalkable == 0)
        {
            reason = "no walkable cells";
            return false;
        }

        bool connected = BFSConnectivity(g, s1, s2, out int dist, out int reachableCount);
        if (!connected)
        {
            reason = $"spawns not connected. s1={s1} s2={s2}";
            return false;
        }

        float reachableFrac = (float)reachableCount / totalWalkable;
        if (reachableFrac < minReachableFraction)
        {
            reason = $"reachableFrac too low ({reachableFrac:0.00} < {minReachableFraction:0.00}). reachable={reachableCount} total={totalWalkable}";
            return false;
        }

        if (minSpawnPathLength > 0 && dist < minSpawnPathLength)
        {
            reason = $"spawn path too short (dist={dist} < {minSpawnPathLength}). s1={s1} s2={s2}";
            return false;
        }

        if (logValidationDetails)
            Debug.Log($"[FpsMapGenerator] Validation OK. s1={s1} s2={s2} dist={dist} reachableFrac={reachableFrac:0.00}");

        return true;
    }

    private void ClearSpawnMarkersInCover()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                for (int l = 0; l < levels; l++)
                {
                    int g = coverLayout[x, z, l].group;
                    if (g == player1SpawnGroupId || g == player2SpawnGroupId)
                        coverLayout[x, z, l] = TileCode.Empty;
                }
            }
        }
    }

    // ---------- MANUAL MAP ----------

    private void InitializeLayoutManually()
    {
        // Level 0 (Ground)
        string[,] L0 =
        {
            { "13W","11S","11S","11S","11S","11S","13S" },
            { "11W","10" ,"10" ,"10" ,"10" ,"10" ,"11E" },
            { "12W","5S" ,"13N","11N","15N","11" ,"11E" },
            { "12W","100","15E","10" ,"15W","100","12E" },
            { "11W","11S" ,"15S","11S","13S","5N" ,"12E" },
            { "11W","10" ,"10" ,"10" ,"10" ,"10" ,"11E" },
            { "13N","11N","11N","11N","11N","11N","13E" },
        };

        // Level 1
        string[,] L1 =
        {
            { "0" ,"0" ,"0" ,"0" ,"0" ,"0"  ,"0" },
            { "0" ,"0" ,"0" ,"0" ,"0" ,"0"  ,"0" },
            { "1E","20" ,"33W","31S","33S","100","1W" },
            { "1E","30","30","20","30","30S","1W" },
            { "1E" ,"100" ,"33","31","33E","20","1W" },
            { "0" ,"0" ,"0" ,"0" ,"0" ,"0"  ,"0" },
            { "0" ,"0" ,"0" ,"0" ,"0" ,"0"  ,"0" },
        };

        // Level 2
        string[,] L2 =
        {
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
        };

        InitializeLayoutFromMatrices(layout, L0, L1, L2);
    }

    private void InitializeCoverLayoutManually()
    {
        string[,] C0 =
        {
            { "101","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","102" },
        };

        string[,] C1 =
        {
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","2W","0","0","0" },
            { "0","0","1","0","1","0","0" },
            { "0","0","0","2W","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
        };

        string[,] C2 =
        {
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
        };

        InitializeLayoutFromMatrices(coverLayout, C0, C1, C2);
    }

    private void InitializeLayoutFromMatrices(TileCode[,,] target, string[,] level0, string[,] level1 = null, string[,] level2 = null)
    {
        if (target == null)
        {
            Debug.LogWarning("InitializeLayoutFromMatrices: target layout is null");
            return;
        }

        CopyMatrixIntoLevel(target, level0, 0);
        if (level1 != null) CopyMatrixIntoLevel(target, level1, 1);
        if (level2 != null) CopyMatrixIntoLevel(target, level2, 2);
    }

    private void CopyMatrixIntoLevel(TileCode[,,] target, string[,] matrix, int level)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                string token = (matrix[r, c] ?? "").Trim();
                var tile = ParseToken(token);
                target[c, r, level] = tile;
            }
        }
    }

    private TileCode ParseToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token == "0")
            return TileCode.Empty;

        char last = token[token.Length - 1];
        Direction dir = Direction.None;

        if (last == 'N' || last == 'E' || last == 'S' || last == 'W' ||
            last == 'n' || last == 'e' || last == 's' || last == 'w')
        {
            dir = CharToDirection(last);
            token = token.Substring(0, token.Length - 1);
        }

        if (!int.TryParse(token, out int group))
        {
            Debug.LogWarning($"ParseToken: invalid group '{token}', using empty.");
            return TileCode.Empty;
        }

        return new TileCode(group, dir);
    }

    private Direction CharToDirection(char c)
    {
        switch (char.ToUpperInvariant(c))
        {
            case 'N': return Direction.North;
            case 'E': return Direction.East;
            case 'S': return Direction.South;
            case 'W': return Direction.West;
            default: return Direction.None;
        }
    }

    // ---------- GEOMETRY ----------

    private void BuildGeometry(TileCode[,,] sourceLayout, Transform parent, Dictionary<int, GameObject> lookup, Vector3 worldOffset)
    {
        if (sourceLayout == null)
        {
            Debug.LogWarning("BuildGeometry: layout is null, nothing to build.");
            return;
        }
        if (parent == null)
        {
            Debug.LogWarning("BuildGeometry: parent is null, nothing to build under.");
            return;
        }
        if (lookup == null)
        {
            Debug.LogWarning("BuildGeometry: lookup is null.");
            return;
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                for (int level = 0; level < levels; level++)
                {
                    TileCode tile = sourceLayout[x, z, level];
                    if (tile.IsEmpty) continue;

                    if (!lookup.TryGetValue(tile.group, out GameObject prefab))
                        continue;

                    Vector3 worldPos = GridToWorld(x, level, z) + worldOffset;
                    Quaternion rot = GetRotationForDirection(tile.dir);

                    Instantiate(prefab, worldPos, rot, parent);
                }
            }
        }
    }

    private Quaternion GetRotationForDirection(Direction dir)
    {
        switch (dir)
        {
            case Direction.North: return Quaternion.Euler(0f, 0f, 0f);
            case Direction.East: return Quaternion.Euler(0f, 90f, 0f);
            case Direction.South: return Quaternion.Euler(0f, 180f, 0f);
            case Direction.West: return Quaternion.Euler(0f, 270f, 0f);
            default: return Quaternion.identity;
        }
    }

    private Vector3 GridToWorld(int x, int level, int z)
    {
        return new Vector3(x * moduleSize, level * moduleSize, z * moduleSize);
    }



    private struct DoorCandidate
    {
        public Vector2Int buildingCell;  
        public Direction outwardDir;      
        public Vector2Int outsideCell;    

        public DoorCandidate(Vector2Int b, Direction dir, Vector2Int outside)
        {
            buildingCell = b;
            outwardDir = dir;
            outsideCell = outside;
        }
    }


    private Direction Opposite(Direction d)
    {
        switch (d)
        {
            case Direction.North: return Direction.South;
            case Direction.East: return Direction.West;
            case Direction.South: return Direction.North;
            case Direction.West: return Direction.East;
            default: return Direction.None;
        }
    }

    
    // opens corridor wall facing door
    private void RebuildWalkableTileWithDoorOpening(bool[,] g, Vector2Int corridorCell, Direction sideFacingDoor)
    {
        int x = corridorCell.x;
        int z = corridorCell.y;

        if (g == null || layout == null) return;
        if (!IsWalkable(g, x, z)) return;

     
        bool n = IsWalkable(g, x, z + 1);
        bool e = IsWalkable(g, x + 1, z);
        bool s = IsWalkable(g, x, z - 1);
        bool w = IsWalkable(g, x - 1, z);

        bool wallN = !n;
        bool wallE = !e;
        bool wallS = !s;
        bool wallW = !w;

    
        switch (sideFacingDoor)
        {
            case Direction.North: wallN = false; break;
            case Direction.East: wallE = false; break;
            case Direction.South: wallS = false; break;
            case Direction.West: wallW = false; break;
        }

        int wallCount =
            (wallN ? 1 : 0) +
            (wallE ? 1 : 0) +
            (wallS ? 1 : 0) +
            (wallW ? 1 : 0);

        layout[x, z, 0] = BuildTileFromWalls(wallN, wallE, wallS, wallW, wallCount);
    }

    private void PlaceDoorsForAllBuildings(bool[,] g)
    {
        if (g == null) return;
        if (buildingMask == null) return;
        if (layout == null) return;

        if (!TryGetSpawnCell(1, out Vector2Int s1) || !TryGetSpawnCell(2, out Vector2Int s2))
        {
            Debug.LogWarning("[FpsMapGenerator] Doors not placed: missing spawns");
            return;
        }

        int[,] distToS1 = ComputeDistanceField(g, s1);
        int[,] distToS2 = ComputeDistanceField(g, s2);

        bool[,] visited = new bool[width, depth];

        int buildingsProcessed = 0;
        int doorsPlacedTotal = 0;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (!buildingMask[x, z] || visited[x, z])
                    continue;

                List<Vector2Int> component = FloodFillBuildingComponent(new Vector2Int(x, z), visited);

                List<DoorCandidate> candidates = GatherDoorCandidatesForComponent(component, g);
                if (candidates.Count == 0)
                    continue;

                DoorCandidate? doorA = PickBestDoorCandidate(candidates, distToS1, null);
                DoorCandidate? doorB = PickBestDoorCandidate(candidates, distToS2, doorA);

                int placedThisBuilding = 0;

                if (doorA.HasValue)
                {
                    PlaceDoorTile(g, doorA.Value);
                    placedThisBuilding++;
                    doorsPlacedTotal++;
                }

                if (doorsPerBuilding >= 2 && doorB.HasValue)
                {
                    PlaceDoorTile(g, doorB.Value);
                    placedThisBuilding++;
                    doorsPlacedTotal++;
                }

                if (doorsPerBuilding >= 2 && placedThisBuilding < 2)
                {
                    DoorCandidate? fallback = PickBestDoorCandidate(candidates, distToS1, doorA);
                    if (fallback.HasValue)
                    {
                        PlaceDoorTile(g, fallback.Value);
                        doorsPlacedTotal++;
                    }
                }

                buildingsProcessed++;
            }
        }

        Debug.Log($"[FpsMapGenerator] Doors placed: {doorsPlacedTotal} across {buildingsProcessed} buildings.");
    }

    // Flood-fill building region
    private List<Vector2Int> FloodFillBuildingComponent(Vector2Int start, bool[,] visited)
    {
        List<Vector2Int> component = new List<Vector2Int>();
        Queue<Vector2Int> q = new Queue<Vector2Int>();

        visited[start.x, start.y] = true;
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            component.Add(cur);

            TryEnqueueBuildingNeighbor(cur.x, cur.y + 1, visited, q);
            TryEnqueueBuildingNeighbor(cur.x + 1, cur.y, visited, q);
            TryEnqueueBuildingNeighbor(cur.x, cur.y - 1, visited, q);
            TryEnqueueBuildingNeighbor(cur.x - 1, cur.y, visited, q);
        }

        return component;
    }

    private void TryEnqueueBuildingNeighbor(int nx, int nz, bool[,] visited, Queue<Vector2Int> q)
    {
        if (nx < 0 || nx >= width || nz < 0 || nz >= depth) return;
        if (visited[nx, nz]) return;
        if (buildingMask == null || !buildingMask[nx, nz]) return;

        visited[nx, nz] = true;
        q.Enqueue(new Vector2Int(nx, nz));
    }

    // Chooses smallest distance field value
    private DoorCandidate? PickBestDoorCandidate(List<DoorCandidate> cands, int[,] dist, DoorCandidate? exclude)
    {
        int bestScore = int.MaxValue;
        DoorCandidate best = default;
        bool found = false;

        foreach (var c in cands)
        {
            if (exclude.HasValue &&
                c.buildingCell == exclude.Value.buildingCell &&
                c.outwardDir == exclude.Value.outwardDir)
                continue;

            int d = dist[c.outsideCell.x, c.outsideCell.y];
            if (d < 0) continue;

            if (d < bestScore)
            {
                bestScore = d;
                best = c;
                found = true;
            }
        }

        return found ? best : (DoorCandidate?)null;
    }

    // Places door and opens adjacent corridor wall
    private void PlaceDoorTile(bool[,] g, DoorCandidate c)
    {
        int bx = c.buildingCell.x;
        int bz = c.buildingCell.y;

        TileCode prev = layout[bx, bz, 0];
        int chosenDoorGroup = doorGroupId;

        // Corner replacement: 13 -> 151/152
        if (prev.group == floorTwoWallsCornerGroupId)
            chosenDoorGroup = ChooseCornerDoorVariant(prev, c.outwardDir);

        layout[bx, bz, 0] = new TileCode(chosenDoorGroup, c.outwardDir);

        Direction corridorSideFacingDoor = Opposite(c.outwardDir);
        RebuildWalkableTileWithDoorOpening(g, c.outsideCell, corridorSideFacingDoor);
    }

    // Selects 151/152 based on kept corner wall side
    private int ChooseCornerDoorVariant(TileCode prevCorner, Direction outwardDir)
    {
        // Corner mapping from BuildTileFromWalls
        // North: N+W, East: N+E, South: E+S, West: S+W
        Direction w1 = Direction.North;
        Direction w2 = Direction.West;

        switch (prevCorner.dir)
        {
            case Direction.North: w1 = Direction.North; w2 = Direction.West; break;
            case Direction.East: w1 = Direction.North; w2 = Direction.East; break;
            case Direction.South: w1 = Direction.East; w2 = Direction.South; break;
            case Direction.West: w1 = Direction.South; w2 = Direction.West; break;
            default: w1 = Direction.North; w2 = Direction.West; break;
        }

        // Door opens on outwardDir side, keep the other wall
        Direction keptWall;
        if (outwardDir == w1) keptWall = w2;
        else if (outwardDir == w2) keptWall = w1;
        else return doorGroupId;

        // Determine left/right wall relative to outwardDir
        Direction leftOf = Direction.None;
        Direction rightOf = Direction.None;

        switch (outwardDir)
        {
            case Direction.North: leftOf = Direction.West; rightOf = Direction.East; break;
            case Direction.East: leftOf = Direction.North; rightOf = Direction.South; break;
            case Direction.South: leftOf = Direction.East; rightOf = Direction.West; break;
            case Direction.West: leftOf = Direction.South; rightOf = Direction.North; break;
        }

        if (keptWall == leftOf) return doorCornerLeftWallGroupId;
        if (keptWall == rightOf) return doorCornerRightWallGroupId;
        return doorGroupId;
    }

    private List<DoorCandidate> GatherDoorCandidatesForComponent(List<Vector2Int> component, bool[,] g)
    {
        List<DoorCandidate> candidates = new List<DoorCandidate>(64);

        foreach (var cell in component)
        {
            int x = cell.x;
            int z = cell.y;

            TryAddDoorCandidate(candidates, g, x, z, x, z + 1, Direction.North);
            TryAddDoorCandidate(candidates, g, x, z, x + 1, z, Direction.East);
            TryAddDoorCandidate(candidates, g, x, z, x, z - 1, Direction.South);
            TryAddDoorCandidate(candidates, g, x, z, x - 1, z, Direction.West);
        }

        return candidates;
    }

    private void TryAddDoorCandidate(List<DoorCandidate> candidates, bool[,] g, int bx, int bz, int ox, int oz, Direction outward)
    {
        if (ox < 0 || ox >= width || oz < 0 || oz >= depth) return;
        if (!g[ox, oz]) return;

        candidates.Add(new DoorCandidate(new Vector2Int(bx, bz), outward, new Vector2Int(ox, oz)));
    }

    private int[,] ComputeDistanceField(bool[,] g, Vector2Int start)
    {
        int[,] dist = new int[width, depth];
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                dist[x, z] = -1;

        if (!IsWalkable(g, start.x, start.y))
            return dist;

        Queue<Vector2Int> q = new Queue<Vector2Int>();
        dist[start.x, start.y] = 0;
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            int cd = dist[cur.x, cur.y];

            TryPushDistance(g, dist, q, cur.x, cur.y + 1, cd + 1);
            TryPushDistance(g, dist, q, cur.x + 1, cur.y, cd + 1);
            TryPushDistance(g, dist, q, cur.x, cur.y - 1, cd + 1);
            TryPushDistance(g, dist, q, cur.x - 1, cur.y, cd + 1);
        }

        return dist;
    }

    private void TryPushDistance(bool[,] g, int[,] dist, Queue<Vector2Int> q, int x, int z, int nd)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth) return;
        if (!g[x, z]) return;
        if (dist[x, z] != -1) return;

        dist[x, z] = nd;
        q.Enqueue(new Vector2Int(x, z));
    }

}
