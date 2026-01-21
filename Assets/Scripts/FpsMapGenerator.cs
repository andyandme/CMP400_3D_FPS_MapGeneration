using System.Collections.Generic;
using UnityEngine;


public class FpsMapGenerator : MonoBehaviour
{
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
    ManualMatrix, //Premade Maps 
    ProceduralWalkable //Procedurally generated maps (just the floorplan)
    
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
    
    [Header("Grid Settings")]
    public int width = 16;
    public int depth = 16;
    public int levels = 3;          // 0 = ground, 1 = mid, 2 = top
    public float moduleSize = 10f;  // tile size in world units

    [Header("Generation Mode")]
    public GenerationMode generationMode = GenerationMode.ManualMatrix;

    public int proceduralFloorGroupId = 10;
    public bool keepBorderBlocked = true;

    [Range(0, 50)]
    public int extraRooms = 8;

    [Range(1,6)]
    public int RoomMaxSize = 4;

    [Range(0, 2)]
    public int corridorThickness = 1;

    public bool drawWalkableDebug = true;



    [Header("Prefabs")]
    public TilePrefab[] tilePrefabs;
    public Transform mapParent;

    [Header("Cover Layer")]

    public bool enableCoverLayer = true;
    public TilePrefab[] coverPrefabs; // a seperate layer for the cover to be implemented
    public Transform coverParent;
    public Vector3 coverWorldOffset = Vector3.zero;


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

        if (generationMode == GenerationMode.ManualMatrix)
        {
            InitializeLayoutManually();

        }
        else
        {
            walkable = GenerateWalkablePlan();
            InitializeLayoutFromWalkable(walkable);
        }

        if (enableCoverLayer)
        {
            InitializeCoverLayoutManually();
        }

            

        BuildGeometry(layout, mapParent != null ? mapParent : transform, prefabLookup, Vector3.zero);

        if (enableCoverLayer)
        {
            BuildGeometry(coverLayout, coverParent, coverPrefabLookup, coverWorldOffset);
        }

        Debug.Log($"[FpsMapGenerator] Regenerated. Seed={lastUsedSeed} (useRandomSeed={useRandomSeed})");
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

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (g != null && g[x, z])
                {
                    layout[x, z, 0] = new TileCode(proceduralFloorGroupId, Direction.North);

                }
            
            }
        
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawWalkableDebug)
        {
            return;
        }
        if (generationMode != GenerationMode.ProceduralWalkable)
        {
            return;
        }

        if (walkable == null)
        { 
            return;
        }

        Gizmos.matrix = Matrix4x4.identity;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {

                if (!walkable[x, z]) continue;

                Vector3 p = GridToWorld(x, 0, z) + new Vector3(moduleSize * 0.5f, 0.2f, moduleSize * 0.5f);
                Gizmos.DrawWireCube(p, new Vector3(moduleSize * 0.9f, 0.2f, moduleSize * 0.9f));


            }
        
        }

    
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

            if (entry == null)
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

        //-----------------------Guide not finished below this-----------------------
        //20 = Ceiling / Roof(Ceiling with no walls or Ground)
        //21.1 – 21.4 = Ceiling with one wall(same Notation as 1.1 – 1.4)
        //22.1 – 22.6 = Ceiling with two walls(same Notation as 2.1 – 2.6)
        //23.1 – 23.4 = Ceiling with three walls(same notation as 3.1 – 3.4)

        //30 = Ceiling and Ground(No walls)
        //31.1 – 31.4 = Ceiling and Ground with one wall(same Notation as 1.1 – 1.4)
        //32.1 – 32.6 = Ceiling and Ground with two walls(same Notation as 2.1 – 2.6)
        //33.1 – 33.4 = Ceiling and Ground with three walls(same Notation as 3.1 – 3.4)
        //35.1 – 35.1 = Ceiling and Ground with Door(Same Notation as 15.1 – 15.4)



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
            { "0","0","0","0","0","0","0" },//Closest to camera spawn (South Side)
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },//Furthest from camera spawn (NorthSide)

        };
        string[,] C1 =
        {
            { "0","0","0","0","0","0","0" },
            { "0","0","0","0","0","0","0" },
            { "0","0","0","2","0","0","0" },
            { "0","0","1","0","1","0","0" },
            { "0","0","0","2","0","0","0" },
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

     
        //width = w;
        //depth = h;
        //levels = 3;

        //AllocateLayout(); // clears layout to empty with updated sizes

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