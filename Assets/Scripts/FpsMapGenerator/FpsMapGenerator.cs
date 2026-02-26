using System;
using System.Collections.Generic;
using UnityEngine;

public partial class FpsMapGenerator : MonoBehaviour
{
    public System.Action OnMapRegenerated;
    //Prefabs Face north by default
    public enum Direction
    {
        None,
        North,
        East,
        South,
        West,

        NorthEast,
        SouthEast,
        SouthWest,
        NorthWest
    }

    public enum GenerationMode
    {
        ManuallyMadeMap1, 
        ProcedurallyGeneratedMap 
    }

    [System.Serializable]
    public class TilePrefab
    {
        public int groupId;          
        public GameObject prefab;
    }

    [System.Serializable]
    public struct TileCode
    {
        public int group;        
        public Direction dir;    

        public TileCode(int group, Direction dir = Direction.None)
        {
            this.group = group;
            this.dir = dir;
        }

        public bool IsEmpty => group == 0;
        public static TileCode Empty => new TileCode(0, Direction.None);
    }

    //----------Windows----------

    [Header("Building Windows")]
    public bool enableBuildingWindows = true;
    public int windowWallGroupId = 115;
    [Range(0, 10)] public int maxWindowsPerGroundFloor = 3;
    [Range(0, 10)] public int maxWindowsPerUpperFloor = 3;

    //----------Outdoor Cubes----------
    [Header("Outdoor Cube Cover (multi-cell)")]
    public int outdoorCubeGroupId = 100;
    [Range(2, 12)] public int maxCubeRunLength = 6;
    [Range(2, 6)] public int minCubeRunLength = 2;
    [Range(0, 3)] public int cubeFootprintBuffer = 1; 

    //----------Grid----------
    [Header("Grid Settings")]
    public int width = 24;
    public int depth = 24;
    public int levels = 3;          // 0 = ground, 1 = mid, 2 = top
    public float moduleSize = 10f;  // tile size in world units

    //----------Roof----------
    [Header("Building Roof")]
    public bool enableBuildingRoof = true;
    public int roofGroupId = 10;

    //----------Mode----------
    [Header("Generation Mode")]
    public GenerationMode generationMode = GenerationMode.ManuallyMadeMap1;
    public bool keepBorderBlocked = true;

    //----------BorderSmoothing----------
    [Header("Border Smoothing")]
     int borderSmoothCornerGroupId = 19; 
    [Range(0, 8)] public int borderSmoothingSpawnExcludeRadius = 3;

    //----------Rooms----------
    [Range(0, 50)] public int extraRooms = 8;
    [Range(1, 6)] public int RoomMaxSize = 4;
    public int RoomMinSize = 2;
    [Range(0, 2)]    public int corridorThickness = 2;


    //----------Exterior Cull----------
    //[Header("Procedural: Exterior Cull")]
    [Range(0, 4)] int minWalkableNeighborsToKeep = 1;
    [Range(0, 10)] int exteriorCullIterations = 1;


    //----------Buildings----------
    [Header("Procedural: buildings")]
    public bool enableBuildings = true;
    public int buildingAttempts = 60;
    public int buildingMinSize = 3;
    [Range(0, 4)] public int buildingClearance = 1;
    [Range(0f, 1)] public float clearanceWalkableFraction = 0.8f;
    private bool[,] buildingMask;
    private bool[,] buildingHasStairsMask;

    //----------Verticality----------
    [Header("Building Verticality")]
    public bool enableBuildingVerticality = true;
    [Range(1, 10)] public int buildingUpperLevel = 1;
    public int buildingUpperFloorGroupId = 10;
    public int stairGroupId = 5;             

    [Range(1, 999)] public int mediumBuildingMinCells = 18; 
    [Range(1, 999)] public int largeBuildingMinCells = 35;  

    [Range(0, 6)] public int stairMinDoorDistance = 2; 
    [Range(1, 12)] public int minStairSeparation = 6;  
    [Range(0, 2)] public int stairHoleRadius = 0;

    //----------Debug----------
    [Header("DEBUG: Force Validation Failure")]
    public bool forceConnectivityFail = false;
    [Range(0, 999)] public int forceBlockRowZ = 8; // Row index to block

    //----------Validation----------
    [Header("Validation")]
    [Range(1, 100)] public int maxGenerationAttempts = 60;
    [Range(0f, 1f)] public float minReachableFraction = 0.95f; // represent percentage of cells connected to spawn 1
    [Range(0, 20)] public int minSpawnPathLength = 12;
    public bool logValidationDetails = true;

    //----------WallBitmasking----------
    [Header("Procedural: Bitmasking Walls")]
    int floorNoWallsGroupId = 10;
    int floorOneWallGroupId = 11; //N facing
    int floorTwoWallsOppositeGroupId = 12; //N+S facing walls at default
    int floorTwoWallsCornerGroupId = 13; //N+W facing by default
    int floorThreeWallsGroupId = 14;
    int floorFourWallsGroupId = 16;

    //----------Prefabs----------
    [Header("Prefabs")]
    public TilePrefab[] tilePrefabs;
    public Transform mapParent;

    //----------Doors----------
    [Header("Doors")]
    int doorGroupId = 15;
    int doorsPerBuilding = 2;
    int doorCornerLeftWallGroupId = 151;
    int doorCornerRightWallGroupId = 152;

    //----------CoverRules----------
    [Header("Cover Placement Rules")]
    public int[] outdoorCoverGroupIds;
    public int[] indoorCoverGroupIds;

    //----------CoverSettings----------
    [Header("Cover Placement Settings")]
    [Range(3, 30)] public int minSightlineRun = 8;      
    [Range(2, 12)] public int runCoverSpacing = 4;       
    [Range(0, 6)] public int coverSpawnExcludeRadius = 3;
    [Range(0, 4)] public int coverDoorExcludeRadius = 1;
    [Range(0, 200)] public int maxOutdoorCovers = 60;

    //----------IndoorCover----------
    [Header("Indoor Cover")]
    [Range(0, 6)] public int indoorCoverMinDoorDistance = 2;
    [Range(0, 8)] public int maxCoverPerBuilding = 3;

    //----------CoverLayer----------
    [Header("Cover Layer")]
    public bool enableCoverLayer = true;
    public TilePrefab[] coverPrefabs; // a seperate layer for the cover to be implemented
    public Transform coverParent;
    public Vector3 coverWorldOffset = Vector3.zero;

    //----------SpawnMarkers----------
    [Header("Cover Layer Spawn Points")]
     int player1SpawnGroupId = 101;
     int player2SpawnGroupId = 102;
     int spawnLevel = 0;
     bool placeSpawnsForProcedural = true;

    //----------Seed----------
    [Header("Seed")]
    public bool useRandomSeed = false;
    public int seed = 12345;

    //----------Regenerate----------
    [Header("Regenerate")]
    public bool generateOnStart = true;
    public bool regenWithRkey = true;
    private int lastUsedSeed;


    //----------RunTimeData----------
    private TileCode[,,] layout;  
    private TileCode[,,] coverLayout;
    private bool[,] walkable;

    private Dictionary<int, GameObject> prefabLookup;
    private Dictionary<int, GameObject> coverPrefabLookup;

    private System.Random rng;




    private void Awake() //Sets up Prefabs and coverParent 
    {
        BuildPrefabLookup();
        BuildCoverPrefabLookup();
        EnsureCoverParent();
    }

    private void Start() //Generate map on startup
    {
        if (generateOnStart)
        {
            Regenerate();
        }
    }


    private void Update() // If 'R' is pressed Generate map
    {
        if (regenWithRkey && Application.isPlaying && Input.GetKeyDown(KeyCode.R))
        {
            Regenerate();
        }
    }

    public void Regenerate() // Builds a new map and cover, Will keep retrying until it passes validation then adds the spawn tiles
    {
        int attempts = 0;
        bool accepted = false;
        string lastRejectReason = "";

        int baseSeed = seed;

        for (attempts = 1; attempts <= maxGenerationAttempts; attempts++)
        {
            int attemptSeed;

            if (useRandomSeed)
            {
                attemptSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                seed = attemptSeed;
            }
            else
            {
                attemptSeed = baseSeed + (attempts - 1);
            }
            
            lastUsedSeed = attemptSeed;
            rng = new System.Random(lastUsedSeed);

            AllocateLayout();
            if (enableCoverLayer) AllocateCoverLayout();


            if (generationMode == GenerationMode.ManuallyMadeMap1)
            {
                InitializeLayoutManually();
                if (enableCoverLayer)
                    InitializeCoverLayoutManually();

                accepted = true;
                break;
            }
            if (generationMode == GenerationMode.ProcedurallyGeneratedMap)
            {
                walkable = GenerateWalkablePlan();

               
                if (enableBuildings)
                {
                    ApplyBuildingsToWalkable(walkable);
                }   
                else
                {
                    buildingMask = null;
                }
                  

                CullExteriorWalkable(walkable);

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
                {
                    PlaceDoorsForAllBuildings(walkable);
                }


                if (enableBuildings && enableBuildingVerticality)
                {
                    PlaceBuildingVerticality(walkable);
                }

                if (enableBuildings && enableBuildingWindows)
                {
                    PlaceBuildingWindowsGroundFloor(walkable);

                    if (enableBuildingVerticality)
                    {
                        PlaceBuildingWindowsUpperFloor(walkable);
                    }
                }

                SmoothOuterBorderCorners(walkable);

                if (enableCoverLayer)
                {
                    PlaceCoverFromAnalysis(walkable);
                }

                accepted = ValidateConnectivityAndQuality(walkable, out lastRejectReason);

                if (accepted)
                    break;

                if (logValidationDetails)
                    Debug.LogWarning($"[FpsMapGenerator] Reject attempt {attempts}/{maxGenerationAttempts} Seed={lastUsedSeed} Reason: {lastRejectReason}");


            }

        }

        if (!accepted)
        {
            Debug.LogError($"[FpsMapGenerator] FAILED to generate a valid map after {maxGenerationAttempts} atempts. Last reason for rejection: {lastRejectReason} ");
        }




        BuildPrefabLookup();
        BuildCoverPrefabLookup();
        EnsureCoverParent();

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
}