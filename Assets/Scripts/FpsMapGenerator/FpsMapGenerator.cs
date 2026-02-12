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

    [Header("Grid Settings")]
    public int width = 24;
    public int depth = 24;
    public int levels = 3;          // 0 = ground, 1 = mid, 2 = top
    public float moduleSize = 10f;  // tile size in world units


    [Header("Generation Mode")]
    public GenerationMode generationMode = GenerationMode.ManuallyMadeMap1;

    public bool keepBorderBlocked = true;

    [Header("Border Smoothing")]
    public int borderSmoothCornerGroupId = 19;
    [Range(0, 8)] public int borderSmoothingSpawnExcludeRadius = 3;


    [Range(0, 50)]
    public int extraRooms = 8;

    [Range(1, 6)]
    public int RoomMaxSize = 4;

    public int RoomMinSize = 2;

    [Range(0, 2)]
    public int corridorThickness = 2;



    [Header("Procedural: Exterior Cull")]
    [Range(0, 4)] public int minWalkableNeighborsToKeep = 1;
    [Range(0, 10)] public int exteriorCullIterations = 1;



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
                    ApplyBuildingsToWalkable(walkable);

                if (forceConnectivityFail)
                    ForceDisconnectByBlockingRow(walkable, forceBlockRowZ);


                InitializeLayoutFromWalkable(walkable);

                if (enableCoverLayer && placeSpawnsForProcedural)
                    PlaceSpawnPointsFromWalkable(walkable);

   

                if (enableBuildings)
                    PlaceDoorsForAllBuildings(walkable);


                SmoothOuterBorderCorners(walkable);


            }


            if (enableBuildings)
                PlaceDoorsForAllBuildings(walkable);

            SmoothOuterBorderCorners(walkable);

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
}