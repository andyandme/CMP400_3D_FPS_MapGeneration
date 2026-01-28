using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;


public class FpsMapGenerator : MonoBehaviour
{

    public System.Action OnMapRegenerated;
    //Deafult Direction for prefabs is north so North basically means None but kept for clarity
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
    ManuallyMadeMap1, //Premade Maps 
    ProcedurallyGeneratedMap //Procedurally generated maps (just the floorplan)
    
    }

    [System.Serializable]
    public class TilePrefab
    {
        public int groupId;          // code for the prefabs
        public GameObject prefab;    
    }

    [System.Serializable]
    public struct TileCode
    {
        public int group;        // tile group id
        public Direction dir;    // orientation

        public TileCode(int group, Direction dir = Direction.None)
        {
            this.group = group;
            this.dir = dir;
        }

        public bool IsEmpty => group == 0;

        public static TileCode Empty => new TileCode(0, Direction.None);
    }


    // Settings
    //-Hopefully fully editable for auto generated maps
    [Header("Procedural: Exterior Cull")]
    [Range(0, 4)] public int minWalkableNeighborsToKeep = 2;  
    [Range(0, 10)] public int exteriorCullIterations = 2;     

    [Header("Grid Settings")]
    public int width = 16;
    public int depth = 16;
    public int levels = 3;          // 0 = ground, 1 = mid, 2 = top
    public float moduleSize = 10f;  // tile size in world units

    [Header("Generation Mode")]
    public GenerationMode generationMode = GenerationMode.ManuallyMadeMap1;

    public int proceduralFloorGroupId = 10;
    public bool keepBorderBlocked = true;

    [Range(0, 50)]
    public int extraRooms = 8;

    [Range(1,6)]
    public int RoomMaxSize = 4;

    [Range(0, 2)]
    public int corridorThickness = 1;

    public bool drawWalkableDebug = true;


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
        //AllocateLayout();
        //InitializeLayoutManually();
        //BuildGeometry();
    }

    private void Update()
    {
        if (regenWithRkey && Application.isPlaying && Input.GetKeyDown(KeyCode.R))
        {
            Regenerate();
        }
    }

    [ContextMenu("Regenerate")]
    public void Regenerate()
    {
        // Seed selection
        if (useRandomSeed)
        {
            seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        lastUsedSeed = seed;
        rng = new System.Random(lastUsedSeed);

       
        BuildPrefabLookup();
        BuildCoverPrefabLookup();
        EnsureCoverParent();


        //clear old children
        ClearSpawnedChildren(mapParent != null ? mapParent : transform);

        if (enableCoverLayer)
        {
            ClearSpawnedChildren(coverParent != null ? coverParent : transform);
        }

        //Allocate map Layout and cover 

        
        AllocateLayout();
       

        if (enableCoverLayer)
        {
            AllocateCoverLayout();
        }

        if (generationMode == GenerationMode.ManuallyMadeMap1)
        {
            InitializeLayoutManually();

        }
        else
        {
            walkable = GenerateWalkablePlan();
            CullExteriorWalkable(walkable);
            InitializeLayoutFromWalkable(walkable);
        }

        if (enableCoverLayer)
        {
            if (generationMode == GenerationMode.ManuallyMadeMap1)
            {

                InitializeCoverLayoutManually();
            }

            if (generationMode == GenerationMode.ProcedurallyGeneratedMap && placeSpawnsForProcedural)
            {
                PlaceSpawnPointsFromWalkable(walkable);
            }

        }
        else
            if (generationMode == GenerationMode.ProcedurallyGeneratedMap && placeSpawnsForProcedural)
        {
            Debug.LogWarning("Spawn placement has been requested but enable coverlayer is false");
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

                    int n =
                        (IsWalkable(g, x, z + 1) ? 1 : 0) +
                        (IsWalkable(g, x + 1, z) ? 1 : 0) +
                        (IsWalkable(g, x, z - 1) ? 1 : 0) +
                        (IsWalkable(g, x - 1, z) ? 1 : 0);

                    
                    if (n < minWalkableNeighborsToKeep)
                    {
                        next[x, z] = false;
                        changed = true;
                    }
                }
            }

            // Apply
            g = CopyGrid(next, g);

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
        {
            for (int z = 0; z < depth; z++)
            {
                if (IsValidSpawnCell(g, x, z))
                    candidates.Add(new Vector2Int(x, z));
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[FpsMapGenerator] No valid spawn candidates found (walkable+floor). Check your floor group IDs / prefabs.");
            return;
        }

        // Choose a start candidate, BFS to farthest VALID candidate A, then BFS from A to farthest VALID candidate B
        Vector2Int start = candidates[rng.Next(0, candidates.Count)];

        var bfs1 = BFSFarthestValidCandidate(g, start);
        Vector2Int A = bfs1.farthest;

        var bfs2 = BFSFarthestValidCandidate(g, A);
        Vector2Int B = bfs2.farthest;

        // Write markers
        coverLayout[A.x, A.y, spawnLevel] = new TileCode(player1SpawnGroupId, Direction.North);
        coverLayout[B.x, B.y, spawnLevel] = new TileCode(player2SpawnGroupId, Direction.North);

        Debug.Log($"[FpsMapGenerator] Spawn cells chosen (floor-valid): P1={A}, P2={B}, dist={bfs2.farthestDist}");

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
                   // basePos += new Vector3(moduleSize * 0.5f, 0f, moduleSize * 0.5f);

                    worldPos = basePos;
                    return true;
                }
            }
        }

        return false;
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
                    {
                        coverLayout[x, z, l] = TileCode.Empty;
                    }
                }
            }
        }
    }




    private void TryVisit(bool[,] g, int[,] dist, Queue<Vector2Int> q, int x, int z, int nd)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth) return;
        if (!g[x, z]) return;
        if (dist[x, z] != -1) return;

        dist[x, z] = nd;
        q.Enqueue(new Vector2Int(x, z));
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
            Debug.LogWarning("Grid too small for keepBorderBlocked, DIsable keepBorderBlocked or increase width/depth ");
            return g;
        
        }

        int startX = rng.Next(minX, maxX + 1);
        int endX = rng.Next(minX + 1);

        int x = startX;
        int z = minZ;

        List<Vector2Int> pathCells = new List<Vector2Int>(width * depth);


        while (z <= maxZ)
        {
           CarveThick(g, x, z, corridorThickness, minX, maxX, minZ, maxZ);
            pathCells.Add(new Vector2Int(x, z));
            
            if (z == maxZ) break;

            int dx = System.Math.Sign(endX - x);
            int moveRoll = rng.Next(0, 100);

            if (moveRoll < 60)
            {
                z++;
            }
            else if (moveRoll < 85 && dx != 0)
            {
                x = Mathf.Clamp(x + dx, minX, maxX);

            }
            else
            { 
            int dir = rng.Next(0,2) == 0 ? -1 : 1;
            x = Mathf.Clamp(x + dir, minX, maxX);
            }
        }

        for (int i = 0; i < extraRooms && pathCells.Count > 0; i++)
        {
            Vector2Int anchor = pathCells[rng.Next(0, pathCells.Count)];
            int rw = rng.Next(2, RoomMaxSize + 1);
            int rh = rng.Next(2, RoomMaxSize + 1);

            int rx = Mathf.Clamp(anchor.x - rng.Next(0, rw), minX, maxX);
            int rz = Mathf.Clamp(anchor.y - rng.Next(0,rh), minZ, maxZ);

            CarveRect(g, rx, rz, rw, rh, minX, maxX, minZ, maxZ);
        
        }

        return g;
     
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
                if (cx < minX || cx > maxX || cz < minZ || cz > maxZ)continue;
                {
                    
                    g[cx, cz] = true;
                    
                }
            
            }
        
        }
    
    }

    private void InitializeLayoutFromWalkable(bool[,] g)
    {
        if (g == null)
        {
            Debug.LogWarning("INitializeLayoutFromWalkable: walkable grid is null");
        }
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {

                    layout[x, z, 0] = TileCode.Empty;

                
            
            }
        
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (!g[x, z])
                    continue;


                bool n = IsWalkable(g, x, z + 1);
                bool e = IsWalkable(g, x + 1, z);
                bool s = IsWalkable(g, x, z - 1);
                bool w = IsWalkable(g, x - 1, z);

                bool wallN = !n;
                bool wallE = !e;
                bool wallS = !s;
                bool wallW = !w;


                int wallCount =
                    (wallN ?  1: 0) + 
                    (wallE ? 1 : 0) +
                    (wallS ? 1 : 0) +
                    (wallW ? 1 : 0);

                TileCode tile = BuildTileFromWalls(wallN, wallE, wallS, wallW, wallCount);
                layout[x, z, 0] = tile;

            }
        }
    }

    private bool IsWalkable(bool[,] g, int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth)
            return false;
        return g[x, z];
    
    }

    private TileCode BuildTileFromWalls(bool wallN, bool wallE, bool wallS, bool wallW, int wallCount)
    {
        // 0 walls
        if (wallCount == 0)
        {
            return new TileCode(floorNoWallsGroupId, Direction.North);
        }

        // 1 wall
        if (wallCount == 1)
        {
            // One wall prefab default: wall on North at Direction.North
            if (wallN) return new TileCode(floorOneWallGroupId, Direction.North);
            if (wallE) return new TileCode(floorOneWallGroupId, Direction.East);
            if (wallS) return new TileCode(floorOneWallGroupId, Direction.South);
            /*wallW*/
            return new TileCode(floorOneWallGroupId, Direction.West);
        }
        // 2 walls
        if (wallCount == 2)
        {
            bool opposite = (wallN && wallS) || (wallE && wallW);

            if (opposite)
            {
                // Opposite walls prefab default: walls on North + South
                // If walls are E+W, rotate 90 degrees (Direction.East).
                if (wallN && wallS) return new TileCode(floorTwoWallsOppositeGroupId, Direction.North);
                /*wallE && wallW*/  return new TileCode(floorTwoWallsOppositeGroupId, Direction.East);
            }
            else
            {
                // Corner-walls prefab default: walls on North + West
                // Rotate to match the two adjacent wall sides:
                // NW = North, NE = East, ES = South, SW = West
                if (wallN && wallW) return new TileCode(floorTwoWallsCornerGroupId, Direction.North); // N+W
                if (wallN && wallE) return new TileCode(floorTwoWallsCornerGroupId, Direction.East);  // N+E
                if (wallE && wallS) return new TileCode(floorTwoWallsCornerGroupId, Direction.South); // E+S
                /*wallS && wallW*/  return new TileCode(floorTwoWallsCornerGroupId, Direction.West);  // S+W
            }
        }

        // 3 walls
        if (wallCount == 3)
        {
            // Three-walls prefab default: opening on North at Direction.North (walls E+S+W)
            if (!wallN) return new TileCode(floorThreeWallsGroupId, Direction.North); // open to North
            if (!wallE) return new TileCode(floorThreeWallsGroupId, Direction.East);  // open to East
            if (!wallS) return new TileCode(floorThreeWallsGroupId, Direction.South); // open to South
            /*open West*/return new TileCode(floorThreeWallsGroupId, Direction.West);
        }

        // 4 walls
        return new TileCode(floorFourWallsGroupId, Direction.North);
    }


    private void EnsureCoverParent()
    {
        if (!enableCoverLayer)
        { 
            return;
        }
        if (coverParent != null)
        {
            return;
        }

        Transform existing = transform.Find("CoverParent");

        if (existing != null )
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
        if (parent == null)
        {
            return;
        }

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

        // Default everything to empty
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                for (int l = 0; l < levels; l++)
                {
                    layout[x, z, l] = TileCode.Empty;
                }
            }
        }
    }

    private void AllocateCoverLayout()
    {
        coverLayout = new TileCode[width, depth, levels];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                for (int l = 0; l < levels; l++)
                {
                    coverLayout[x, z, l] = TileCode.Empty;
                }
            }
        }
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
        {
            coverPrefabLookup = new Dictionary<int, GameObject>();
        }
        else 
        {
        coverPrefabLookup.Clear();
        }

        if (coverPrefabs == null)
        { 
            return; 
        }

        foreach (var entry in coverPrefabs)
        {
            if (entry == null)
            { 
                continue;
            }

            if (entry.prefab == null)
            {
                continue; 
            }
            coverPrefabLookup[entry.groupId] = entry.prefab;
        }
    }

  
    private void InitializeLayoutManually()
    {
        //Prefab Placement Guide
        //Direction:
        //Direction Refers to which way the prefab is pointing at default it will be north facing. Each prefab will say which part of it is northfacing.
        //0 = Nothing (just an open 10x10x10 area, No need for direction on this one) 
        //1 = One Wall (The one wall is northfacing)
        //2.1 = Two Walls At opposite Sides (One of these walls is northfacing)
        //2.2 = Two walls to make a right angle (Right most wall when staring from its centre is Northfacing)
        //3 = Three walls (The empty Gap where there is no wall is Northfacing)
        //5 = Stairs (Where the Stairs starts (lowest step) is Northfacing)

        //10   = Ground (Just a ground tile)
        //11   = Ground with one wall(11.1 = wall is on the North side, 11.2 = South, 11.3 = East, 11.4 = West)
        //12.1 = Ground with two walls at opposite sides (One of these walls is northfacing)
        //12.2 = Ground with two walls to make a right angle (Right most wall when staring from its centre is Northfacing)
        //13   = Ground with three walls (The empty Gap where there is no wall is Northfacing)
        //15   = Ground with Door (The side with the door is Northfacing)





        // Level 0 (Ground)
        string[,] L0 =
        {
            { "13W","11S","11S","11S","11S","11S","13S" }, //Closest to camera spawn (South Side)
            { "11W","10" ,"10" ,"10" ,"10" ,"10" ,"11E" },
            { "12W","5S" ,"13N","11N","15N","11" ,"11E" },
            { "12W","100","15E","10" ,"15W","100","12E" },
            { "11W","11S" ,"15S","11S","13S","5N" ,"12E" },
            { "11W","10" ,"10" ,"10" ,"10" ,"10" ,"11E" },
            { "13N","11N","11N","11N","11N","11N","13E" }, //Furthest from camera spawn (NorthSide)
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

        // Level 2 (Left blanck for now)
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
            { "101","0","0","0","0","0","0" },//Closest to camera spawn (South Side)
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","102" },//Furthest from camera spawn (NorthSide)

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
            Debug.LogWarning("InitializeLayoutFromMatrices: target Layout id null");
            return;
        }

        // auto-size grid from L0
        int h = level0.GetLength(0);
        int w = level0.GetLength(1);


        // copy each matrix into layout
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
                // matrix is [row, col] = [z, x]
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

        // Split number + optional letter e.g. "11W", "30"
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
                    {
                        continue;
                    }



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
}