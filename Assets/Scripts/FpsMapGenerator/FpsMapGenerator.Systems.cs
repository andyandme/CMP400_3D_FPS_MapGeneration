using System;
using System.Collections.Generic;
using UnityEngine;

public partial class FpsMapGenerator : MonoBehaviour
{


    [Header("Validation: FPS Map Heuristics")]

    // Dead ends
    [Range(0f, 1f)] public float maxDeadEndFraction = 0.18f;   // % of walkable cells can be dead ends
    [Range(1, 20)] public int maxDeadEndChainLength = 10;     // longest allowed chain from tip to junction

    
    [Range(1, 20)] public int maxStraightRun = 14;            // max consecutive walkable cells in a straight line

 
    [Range(0,10)] public int maxBridgesOnSpawnPath = 3;     

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

        //Walkable tiles(skip building cells)
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
        if (g == null || coverLayout == null) return;

        ClearSpawnMarkersInCover();

        List<Vector2Int> candidates = new List<Vector2Int>(width * depth);
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                if (IsValidSpawnCell(g, x, z))
                    candidates.Add(new Vector2Int(x, z));

        if (candidates.Count < 2)
        {
            Debug.LogWarning("[FpsMapGenerator] Not enough spawn candidates.");
            return;
        }

        Vector2Int bestA = candidates[0];
        Vector2Int bestB = candidates[1];
        float bestScore = float.NegativeInfinity;

        //path distance dominates. euclid breaks ties toward opposite sides
        const float PATH_W = 10f;
        const float EUCLID_W = 4f;

        for (int i = 0; i < candidates.Count; i++)
        {
            Vector2Int a = candidates[i];
            int[,] dist = ComputeDistanceField(g, a);

            for (int j = i + 1; j < candidates.Count; j++)
            {
                Vector2Int b = candidates[j];

                int d = dist[b.x, b.y];
                if (d < 0) continue;

                int dx = a.x - b.x;
                int dz = a.y - b.y;
                float euclidSqr = (dx * dx) + (dz * dz);

                float score = (d * PATH_W) + (euclidSqr * EUCLID_W);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestA = a;
                    bestB = b;
                }
            }
        }

        coverLayout[bestA.x, bestA.y, spawnLevel] = new TileCode(player1SpawnGroupId, Direction.North);
        coverLayout[bestB.x, bestB.y, spawnLevel] = new TileCode(player2SpawnGroupId, Direction.North);

        Debug.Log($"[FpsMapGenerator] Spawn cells chosen: P1={bestA}, P2={bestB}, score={bestScore:0.0}");
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
    //-
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



        // Dead-end analysis
        AnalyzeDeadEnds(g, out int deadEndCells, out int maxChainLen);
        float deadEndFrac = (totalWalkable == 0) ? 0f : (float)deadEndCells / totalWalkable;

        if (deadEndFrac > maxDeadEndFraction)
        {
            reason = $"too many dead ends ({deadEndFrac:0.00} > {maxDeadEndFraction:0.00}). deadEndCells={deadEndCells} total={totalWalkable}";
            return false;
        }

        if (maxChainLen > maxDeadEndChainLength)
        {
            reason = $"dead-end chain too long (maxChainLen={maxChainLen} > {maxDeadEndChainLength})";
            return false;
        }

      
        int longestStraight = ComputeMaxStraightRun(g);
        if (longestStraight > maxStraightRun)
        {
            reason = $"straight run too long (maxStraightRunFound={longestStraight} > {maxStraightRun})";
            return false;
        }

      
        int bridgesOnPath = CountBridgesOnShortestPath(g, s1, s2);
        if (bridgesOnPath > maxBridgesOnSpawnPath)
        {
            reason = $"too many bridge edges on spawn shortest path (bridgesOnPath={bridgesOnPath} > {maxBridgesOnSpawnPath})";
            return false;
        }

        if (logValidationDetails)
            Debug.Log($"[FpsMapGenerator] Validation OK. s1={s1} s2={s2} dist={dist} reachableFrac={reachableFrac:0.00} deadEndFrac={deadEndFrac:0.00} maxChainLen={maxChainLen} longestStraight={longestStraight} bridgesOnPath={bridgesOnPath}");

        return true;
    }

    private int Degree(bool[,] g, int x, int z)
    {
        int d = 0;
        if (IsWalkable(g, x, z + 1)) d++;
        if (IsWalkable(g, x + 1, z)) d++;
        if (IsWalkable(g, x, z - 1)) d++;
        if (IsWalkable(g, x - 1, z)) d++;
        return d;
    }

   
    private void AnalyzeDeadEnds(bool[,] g, out int deadEndCells, out int maxChainLen)
    {
        deadEndCells = 0;
        maxChainLen = 0;

        if (g == null) return;

        bool[,] chainVisited = new bool[width, depth];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (!g[x, z]) continue;

                int deg = Degree(g, x, z);
                if (deg == 1) deadEndCells++;

               
                if (deg != 1) continue;
                if (chainVisited[x, z]) continue;

                int chainLen = 0;
                int cx = x, cz = z;
                int px = int.MinValue, pz = int.MinValue;

                while (true)
                {
                    if (cx < 0 || cx >= width || cz < 0 || cz >= depth) break;
                    if (!g[cx, cz]) break;

                    chainVisited[cx, cz] = true;
                    chainLen++;

                    int cdeg = Degree(g, cx, cz);
                    if (cdeg >= 3) break;          
                    if (cdeg == 0) break;         
                    if (cdeg == 1 && chainLen > 1) break; 

                    
                    bool moved = false;

                    // N
                    if (!moved && IsWalkable(g, cx, cz + 1) && !(cx == px && (cz + 1) == pz))
                    { px = cx; pz = cz; cz = cz + 1; moved = true; }
                    // E
                    if (!moved && IsWalkable(g, cx + 1, cz) && !((cx + 1) == px && cz == pz))
                    { px = cx; pz = cz; cx = cx + 1; moved = true; }
                    // S
                    if (!moved && IsWalkable(g, cx, cz - 1) && !(cx == px && (cz - 1) == pz))
                    { px = cx; pz = cz; cz = cz - 1; moved = true; }
                    // W
                    if (!moved && IsWalkable(g, cx - 1, cz) && !((cx - 1) == px && cz == pz))
                    { px = cx; pz = cz; cx = cx - 1; moved = true; }

                    if (!moved) break;
                }

                if (chainLen > maxChainLen)
                    maxChainLen = chainLen;
            }
        }
    }

    // Longest consecutive walkable run in straight lines (N/S and E/W).
    private int ComputeMaxStraightRun(bool[,] g)
    {
        if (g == null) return 0;

        int best = 0;

        // Horizontal runs (x increasing)
        for (int z = 0; z < depth; z++)
        {
            int run = 0;
            for (int x = 0; x < width; x++)
            {
                if (g[x, z]) { run++; if (run > best) best = run; }
                else run = 0;
            }
        }

        // Vertical runs (z increasing)
        for (int x = 0; x < width; x++)
        {
            int run = 0;
            for (int z = 0; z < depth; z++)
            {
                if (g[x, z]) { run++; if (run > best) best = run; }
                else run = 0;
            }
        }

        return best;
    }

    
    private int CountBridgesOnShortestPath(bool[,] g, Vector2Int s1, Vector2Int s2)
    {
        if (g == null) return 0;
        if (!IsWalkable(g, s1.x, s1.y) || !IsWalkable(g, s2.x, s2.y)) return 0;

        Vector2Int[,] parent = new Vector2Int[width, depth];
        bool[,] seen = new bool[width, depth];
        Queue<Vector2Int> q = new Queue<Vector2Int>();

        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                parent[x, z] = new Vector2Int(int.MinValue, int.MinValue);

        seen[s1.x, s1.y] = true;
        parent[s1.x, s1.y] = s1;
        q.Enqueue(s1);

        bool found = false;
        while (q.Count > 0 && !found)
        {
            var cur = q.Dequeue();

            if (cur == s2) { found = true; break; }

            TryBfsStep(g, cur, new Vector2Int(cur.x, cur.y + 1), seen, parent, q);
            TryBfsStep(g, cur, new Vector2Int(cur.x + 1, cur.y), seen, parent, q);
            TryBfsStep(g, cur, new Vector2Int(cur.x, cur.y - 1), seen, parent, q);
            TryBfsStep(g, cur, new Vector2Int(cur.x - 1, cur.y), seen, parent, q);
        }

        if (!found) return int.MaxValue; 
    
        List<(Vector2Int a, Vector2Int b)> pathEdges = new List<(Vector2Int, Vector2Int)>();
        Vector2Int at = s2;
        while (at != s1)
        {
            Vector2Int p = parent[at.x, at.y];
            if (p.x == int.MinValue) break;
            pathEdges.Add((p, at));
            at = p;
        }

    
        HashSet<long> bridgeSet = ComputeBridgeEdgeSet(g);

        
        int bridgesOnPath = 0;
        for (int i = 0; i < pathEdges.Count; i++)
        {
            long key = EdgeKey(pathEdges[i].a, pathEdges[i].b);
            if (bridgeSet.Contains(key)) bridgesOnPath++;
        }

        return bridgesOnPath;
    }

    private void TryBfsStep(bool[,] g, Vector2Int from, Vector2Int to, bool[,] seen, Vector2Int[,] parent, Queue<Vector2Int> q)
    {
        int x = to.x, z = to.y;
        if (x < 0 || x >= width || z < 0 || z >= depth) return;
        if (seen[x, z]) return;
        if (!g[x, z]) return;

        seen[x, z] = true;
        parent[x, z] = from;
        q.Enqueue(to);
    }

    private HashSet<long> ComputeBridgeEdgeSet(bool[,] g)
    {
        int n = width * depth;

        int[] disc = new int[n];
        int[] low = new int[n];
        int[] parent = new int[n];
        for (int i = 0; i < n; i++) { disc[i] = -1; low[i] = -1; parent[i] = -1; }

        HashSet<long> bridges = new HashSet<long>();
        int time = 0;

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!g[x, z]) continue;
                int u = NodeId(x, z);
                if (disc[u] != -1) continue;

                BridgeDfs(u, ref time, disc, low, parent, bridges, g);
            }
        }

        return bridges;
    }

    private void BridgeDfs(int u, ref int time, int[] disc, int[] low, int[] parent, HashSet<long> bridges, bool[,] g)
    {
        disc[u] = low[u] = time++;
        var (ux, uz) = FromNodeId(u);

        Span<(int dx, int dz)> dirs = stackalloc (int, int)[]
        {
        (0, 1), (1, 0), (0, -1), (-1, 0)
    };

        for (int i = 0; i < dirs.Length; i++)
        {
            int vx = ux + dirs[i].dx;
            int vz = uz + dirs[i].dz;
            if (vx < 0 || vx >= width || vz < 0 || vz >= depth) continue;
            if (!g[vx, vz]) continue;

            int v = NodeId(vx, vz);

            if (disc[v] == -1)
            {
                parent[v] = u;
                BridgeDfs(v, ref time, disc, low, parent, bridges, g);

                low[u] = Math.Min(low[u], low[v]);

                // Bridge condition
                if (low[v] > disc[u])
                    bridges.Add(EdgeKey(ux, uz, vx, vz));
            }
            else if (v != parent[u])
            {
                low[u] = Math.Min(low[u], disc[v]);
            }
        }
    }

    private int NodeId(int x, int z) => x + z * width;

    private (int x, int z) FromNodeId(int id)
    {
        int z = id / width;
        int x = id - z * width;
        return (x, z);
    }

    // EdgeKey stored undirected: normalize endpoints.
    private long EdgeKey(Vector2Int a, Vector2Int b) => EdgeKey(a.x, a.y, b.x, b.y);

    private long EdgeKey(int ax, int az, int bx, int bz)
    {
        int a = NodeId(ax, az);
        int b = NodeId(bx, bz);
        if (a > b) { int t = a; a = b; b = t; }
        return ((long)a << 32) ^ (uint)b;
    }
    //-
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
