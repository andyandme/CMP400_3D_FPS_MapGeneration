using System.Collections.Generic;
using UnityEngine;

public partial class FpsMapGenerator : MonoBehaviour
{
    [SerializeField] private string roundSpawnAName = "SpawnA";
    [SerializeField] private string roundSpawnBName = "SpawnB";

    private Transform generatedSpawnA;
    private Transform generatedSpawnB;

    public Transform GeneratedSpawnA => generatedSpawnA;
    public Transform GeneratedSpawnB => generatedSpawnB;


    private bool IsDoorGroup(int g)
    {
        return g == doorGroupId || g == doorCornerLeftWallGroupId || g == doorCornerRightWallGroupId;
    }

    private bool IsSpawnMarker(int g)
    {
        return g == player1SpawnGroupId || g == player2SpawnGroupId;
    }

    private bool IsInBounds(int x, int z)
    {
        return x >= 0 && x < width && z >= 0 && z < depth;
    }

    private bool IsOpenCell(bool[,] g, int x, int z)
    {
        if (!IsInBounds(x, z)) return false;
        if (g != null && g[x, z]) return true;
        if (buildingMask != null && buildingMask[x, z]) return true;
        return false;
    }

    private int OpenDegree(bool[,] g, int x, int z)
    {
        int d = 0;
        if (IsOpenCell(g, x, z + 1)) d++;
        if (IsOpenCell(g, x + 1, z)) d++;
        if (IsOpenCell(g, x, z - 1)) d++;
        if (IsOpenCell(g, x - 1, z)) d++;
        return d;
    }

    private bool WithinManhattan(Vector2Int a, int x, int z, int r)
    {
        return (Mathf.Abs(a.x - x) + Mathf.Abs(a.y - z)) <= r;
    }


    private bool IsCoverCellFree(int x, int z, int level)
    {
        if (coverLayout == null) return false;
        return coverLayout[x, z, level].group == 0;
    }

    private bool IsCoverCellFree(int x, int z)
    {
        return IsCoverCellFree(x, z, spawnLevel);
    }

    private void ClearNonSpawnCover()
    {
        if (coverLayout == null) return;

        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
            {
                int g0 = coverLayout[x, z, spawnLevel].group;
                if (!IsSpawnMarker(g0))
                    coverLayout[x, z, spawnLevel] = TileCode.Empty;
            }
    }

    private bool IsDoorFrontCell(int x, int z)
    {
        if (layout == null) return false;
        if (!IsInBounds(x, z)) return false;

        Direction[] dirs = { Direction.North, Direction.East, Direction.South, Direction.West };

        for (int i = 0; i < dirs.Length; i++)
        {
            Direction d = dirs[i];

            int bx = x - DirDx(d);
            int bz = z - DirDz(d);
            if (!IsInBounds(bx, bz)) continue;

            TileCode t = layout[bx, bz, 0];
            if (!IsDoorGroup(t.group)) continue;

            if (t.dir == d) return true;
        }

        return false;
    }

    private bool IsBlockedByDoorRule(int x, int z)
    {
        if (!IsInBounds(x, z)) return true;
        if (layout != null && IsDoorGroup(layout[x, z, 0].group)) return true;
        if (IsDoorFrontCell(x, z)) return true;
        return false;
    }

    private bool IsNearDoorOrDoorFront(int x, int z, int r)
    {
        if (layout == null) return false;

        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx;
                int nz = z + dz;
                if (!IsInBounds(nx, nz)) continue;

                if (IsDoorGroup(layout[nx, nz, 0].group)) return true;
                if (IsDoorFrontCell(nx, nz)) return true;
            }

        return false;
    }

    private bool TouchesAnyCover8(int x, int z)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;

                int nx = x + dx;
                int nz = z + dz;
                if (!IsInBounds(nx, nz)) continue;

                int ng = coverLayout[nx, nz, spawnLevel].group;
                if (ng != 0) return true;
            }

        return false;
    }

    private bool TouchesCube8(int x, int z)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;

                int nx = x + dx;
                int nz = z + dz;
                if (!IsInBounds(nx, nz)) continue;

                int ng = coverLayout[nx, nz, spawnLevel].group;
                if (ng == outdoorCubeGroupId) return true;
            }

        return false;
    }

    private int StableHash(int a, int b, int c, int d = 0)
    {
        unchecked
        {
            uint h = 17u;
            h = h * 31u + (uint)a;
            h = h * 31u + (uint)b;
            h = h * 31u + (uint)c;
            h = h * 31u + (uint)d;

            h ^= (h >> 16);
            h *= 0x7feb352du;
            h ^= (h >> 15);
            h *= 0x846ca68bu;
            h ^= (h >> 16);

            return (int)h;
        }
    }

    private bool IsBlockedForPath(int x, int z)
    {
        int cg = coverLayout[x, z, spawnLevel].group;
        if (IsSpawnMarker(cg)) return false;
        return cg != 0;
    }

    private bool SpawnsStillConnectedWithCover(bool[,] g, Vector2Int s1, Vector2Int s2)
    {
        if (g == null) return true;
        if (s1 == default || s2 == default) return true;

        if (!IsInBounds(s1.x, s1.y) || !IsInBounds(s2.x, s2.y)) return true;
        if (!g[s1.x, s1.y] || !g[s2.x, s2.y]) return true;

        if (IsBlockedForPath(s1.x, s1.y)) return false;
        if (IsBlockedForPath(s2.x, s2.y)) return false;

        bool[,] visited = new bool[width, depth];
        Queue<Vector2Int> q = new Queue<Vector2Int>();

        visited[s1.x, s1.y] = true;
        q.Enqueue(s1);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == s2) return true;

            TryEnqueue(cur.x, cur.y + 1);
            TryEnqueue(cur.x + 1, cur.y);
            TryEnqueue(cur.x, cur.y - 1);
            TryEnqueue(cur.x - 1, cur.y);
        }

        return false;

        void TryEnqueue(int nx, int nz)
        {
            if (!IsInBounds(nx, nz)) return;
            if (visited[nx, nz]) return;
            if (!g[nx, nz]) return;
            if (IsBlockedForPath(nx, nz)) return;

            visited[nx, nz] = true;
            q.Enqueue(new Vector2Int(nx, nz));
        }
    }


    private int MeasureOpenRunLengthOutsideBuildings(bool[,] g, int x, int z, int dx, int dz)
    {
        if (!IsOpenCell(g, x, z)) return 0;
        if (buildingMask != null && buildingMask[x, z]) return 0;

        int len = 1;

        int fx = x + dx, fz = z + dz;
        while (fx >= 0 && fx < width && fz >= 0 && fz < depth)
        {
            if (!IsOpenCell(g, fx, fz)) break;
            if (buildingMask != null && buildingMask[fx, fz]) break;
            len++;
            fx += dx; fz += dz;
        }

        int bx = x - dx, bz = z - dz;
        while (bx >= 0 && bx < width && bz >= 0 && bz < depth)
        {
            if (!IsOpenCell(g, bx, bz)) break;
            if (buildingMask != null && buildingMask[bx, bz]) break;
            len++;
            bx -= dx; bz -= dz;
        }

        return len;
    }

    private int MeasurePerpWalkableWidth(bool[,] g, int x, int z, int dx, int dz)
    {
        int pdx = -dz;
        int pdz = dx;

        if (!IsInBounds(x, z) || g == null || !g[x, z]) return 0;
        if (buildingMask != null && buildingMask[x, z]) return 0;

        int widthCount = 1;

        int fx = x + pdx, fz = z + pdz;
        while (IsInBounds(fx, fz) && g[fx, fz] && (buildingMask == null || !buildingMask[fx, fz]))
        {
            widthCount++;
            fx += pdx; fz += pdz;
        }

        int bx = x - pdx, bz = z - pdz;
        while (IsInBounds(bx, bz) && g[bx, bz] && (buildingMask == null || !buildingMask[bx, bz]))
        {
            widthCount++;
            bx -= pdx; bz -= pdz;
        }

        return widthCount;
    }

    private int MaxChainLenForWidth(int perpWidth)
    {
        if (perpWidth <= 2) return 1;
        if (perpWidth == 3) return 2;
        return 3;
    }

    private bool TryGetWallMaskFromTile(TileCode t, out bool wallN, out bool wallE, out bool wallS, out bool wallW)
    {
        wallN = wallE = wallS = wallW = false;
        int g = t.group;

        if (g == floorNoWallsGroupId) return true;

        if (g == floorOneWallGroupId)
        {
            wallN = (t.dir == Direction.North);
            wallE = (t.dir == Direction.East);
            wallS = (t.dir == Direction.South);
            wallW = (t.dir == Direction.West);
            return true;
        }

        if (g == floorTwoWallsOppositeGroupId)
        {
            if (t.dir == Direction.North) { wallN = true; wallS = true; }
            else { wallE = true; wallW = true; }
            return true;
        }

        if (g == floorTwoWallsCornerGroupId)
        {
            switch (t.dir)
            {
                case Direction.North: wallN = true; wallW = true; break;
                case Direction.East: wallN = true; wallE = true; break;
                case Direction.South: wallE = true; wallS = true; break;
                case Direction.West: wallS = true; wallW = true; break;
            }
            return true;
        }

        if (g == floorThreeWallsGroupId)
        {
            wallN = wallE = wallS = wallW = true;
            if (t.dir == Direction.North) wallN = false;
            else if (t.dir == Direction.East) wallE = false;
            else if (t.dir == Direction.South) wallS = false;
            else if (t.dir == Direction.West) wallW = false;
            return true;
        }

        if (g == floorFourWallsGroupId)
        {
            wallN = wallE = wallS = wallW = true;
            return true;
        }

        return false;
    }

    private bool IsWallConstrainedLaneCell(int x, int z)
    {
        if (layout == null) return false;
        if (!IsInBounds(x, z)) return false;
        if (buildingMask != null && buildingMask[x, z]) return false;

        TileCode t = layout[x, z, 0];

        if (!TryGetWallMaskFromTile(t, out bool n, out bool e, out bool s, out bool w))
            return false;

        return (e && w) || (n && s);
    }


    private bool IsAdjacentToWallConstrainedLaneCell(int x, int z)
    {
        return IsWallConstrainedLaneCell(x + 1, z) ||
               IsWallConstrainedLaneCell(x - 1, z) ||
               IsWallConstrainedLaneCell(x, z + 1) ||
               IsWallConstrainedLaneCell(x, z - 1);
    }

    private bool HasOutdoorWalkableBuffer(bool[,] g, int cx, int cz, int r)
    {
        if (r <= 0) return true;

        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx;
                int z = cz + dz;

                if (!IsInBounds(x, z)) return false;
                if (g == null || !g[x, z]) return false;
                if (buildingMask != null && buildingMask[x, z]) return false;
            }

        return true;
    }

    public void PlaceCoverFromAnalysis(bool[,] g)
    {
        if (!enableCoverLayer || coverLayout == null) return;
        if (generationMode != GenerationMode.ProcedurallyGeneratedMap) return;

        ClearNonSpawnCover();

        TryGetSpawnCell(1, out Vector2Int s1);
        TryGetSpawnCell(2, out Vector2Int s2);

        PlaceOutdoorSightlineBreakers(g, s1, s2);
        PlaceIndoorCover(g);

        RefreshGeneratedRoundSpawns();
    }

    private void RefreshGeneratedRoundSpawns()
    {
        if (!TryGetSpawnCell(1, out Vector2Int s1))
        {
            Debug.LogWarning("[FpsMapGenerator] Could not find player 1 spawn marker (101) for round spawn anchor.");
            return;
        }

        if (!TryGetSpawnCell(2, out Vector2Int s2))
        {
            Debug.LogWarning("[FpsMapGenerator] Could not find player 2 spawn marker (102) for round spawn anchor.");
            return;
        }

        if (generatedSpawnA == null)
        {
            Transform existing = transform.Find(roundSpawnAName);
            if (existing != null)
            {
                generatedSpawnA = existing;
            }
            else
            {
                GameObject go = new GameObject(roundSpawnAName);
                go.transform.SetParent(transform, false);
                generatedSpawnA = go.transform;
            }
        }

        if (generatedSpawnB == null)
        {
            Transform existing = transform.Find(roundSpawnBName);
            if (existing != null)
            {
                generatedSpawnB = existing;
            }
            else
            {
                GameObject go = new GameObject(roundSpawnBName);
                go.transform.SetParent(transform, false);
                generatedSpawnB = go.transform;
            }
        }

        Vector3 worldA = GetRoundSpawnWorldPosition(s1, spawnLevel);
        Vector3 worldB = GetRoundSpawnWorldPosition(s2, spawnLevel);

        generatedSpawnA.position = worldA;
        generatedSpawnB.position = worldB;

        generatedSpawnA.rotation = Quaternion.identity;
        generatedSpawnB.rotation = Quaternion.identity;

        Debug.Log($"[FpsMapGenerator] Round spawns refreshed. A cell={s1} pos={worldA} | B cell={s2} pos={worldB}");
    }

    private Vector3 GetRoundSpawnWorldPosition(Vector2Int cell, int level)
    {
        float worldX = cell.x * moduleSize;
        float worldY = (level * moduleSize) + 1f;
        float worldZ = cell.y * moduleSize;

        return new Vector3(worldX, worldY, worldZ);
    }

    private void PlaceOutdoorSightlineBreakers(bool[,] g, Vector2Int s1, Vector2Int s2)
    {
        if (outdoorCoverGroupIds == null || outdoorCoverGroupIds.Length == 0) return;

        int placed = 0;

        // Horizontal runs
        for (int z = 0; z < depth && placed < maxOutdoorCovers; z++)
        {
            int x = 0;
            while (x < width && placed < maxOutdoorCovers)
            {
                while (x < width && (!IsOpenCell(g, x, z) || (buildingMask != null && buildingMask[x, z]))) x++;
                int start = x;
                while (x < width && (IsOpenCell(g, x, z) && (buildingMask == null || !buildingMask[x, z]))) x++;
                int end = x - 1;

                int len = end - start + 1;
                if (len >= minSightlineRun)
                {
                    int count = Mathf.Clamp(len / Mathf.Max(1, runCoverSpacing), 1, 4);

                    for (int i = 0; i < count && placed < maxOutdoorCovers; i++)
                    {
                        float t = (i + 1f) / (count + 1f);
                        int cx = start + Mathf.RoundToInt(t * (len - 1));

                        TryPlaceRunCover(g, cx, z, isHorizontalRun: true, s1, s2, ref placed);
                    }
                }
            }
        }

        // Vertical runs
        for (int x = 0; x < width && placed < maxOutdoorCovers; x++)
        {
            int z = 0;
            while (z < depth && placed < maxOutdoorCovers)
            {
                while (z < depth && (!IsOpenCell(g, x, z) || (buildingMask != null && buildingMask[x, z]))) z++;
                int start = z;
                while (z < depth && (IsOpenCell(g, x, z) && (buildingMask == null || !buildingMask[x, z]))) z++;
                int end = z - 1;

                int len = end - start + 1;
                if (len >= minSightlineRun)
                {
                    int count = Mathf.Clamp(len / Mathf.Max(1, runCoverSpacing), 1, 4);

                    for (int i = 0; i < count && placed < maxOutdoorCovers; i++)
                    {
                        float t = (i + 1f) / (count + 1f);
                        int cz = start + Mathf.RoundToInt(t * (len - 1));

                        TryPlaceRunCover(g, x, cz, isHorizontalRun: false, s1, s2, ref placed);
                    }
                }
            }
        }
    }


    private void TryPlaceRunCover(bool[,] g, int x, int z, bool isHorizontalRun, Vector2Int s1, Vector2Int s2, ref int placed)
    {
        if (!IsOpenCell(g, x, z)) return;
        if (buildingMask != null && buildingMask[x, z]) return;
        if (!IsCoverCellFree(x, z)) return;

        if (IsBlockedByDoorRule(x, z)) return;

        if (coverSpawnExcludeRadius > 0)
        {
            if (WithinManhattan(s1, x, z, coverSpawnExcludeRadius)) return;
            if (WithinManhattan(s2, x, z, coverSpawnExcludeRadius)) return;
        }

        if (coverDoorExcludeRadius > 0 && IsNearDoorOrDoorFront(x, z, coverDoorExcludeRadius)) return;

        bool hasBypass =
            isHorizontalRun
                ? (IsOpenCell(g, x, z + 1) || IsOpenCell(g, x, z - 1))
                : (IsOpenCell(g, x + 1, z) || IsOpenCell(g, x - 1, z));

        if (!hasBypass) return;

        int runX = MeasureOpenRunLengthOutsideBuildings(g, x, z, 1, 0);
        int runZ = MeasureOpenRunLengthOutsideBuildings(g, x, z, 0, 1);

        bool longX = runX >= minSightlineRun;
        bool longZ = runZ >= minSightlineRun;


        if (TryPlaceOutdoorCubeLine(g, x, z, runX, runZ, isHorizontalRun, s1, s2, out int cubesPlaced))
        {
            placed += cubesPlaced;
            return;
        }


        if (TouchesCube8(x, z)) return;

        int deg = OpenDegree(g, x, z);
        if (deg >= 4) return;

        int h = StableHash(lastUsedSeed, x, z, isHorizontalRun ? 1 : 2);
        int idx = Mathf.Abs(h) % outdoorCoverGroupIds.Length;
        int group = outdoorCoverGroupIds[idx];

        Direction dir;
        if (longX && longZ) dir = Direction.NorthEast;
        else if (longX) dir = Direction.East;
        else if (longZ) dir = Direction.North;
        else dir = isHorizontalRun ? Direction.East : Direction.North;

        coverLayout[x, z, spawnLevel] = new TileCode(group, dir);

        if (!SpawnsStillConnectedWithCover(g, s1, s2))
        {
            coverLayout[x, z, spawnLevel] = TileCode.Empty;
            return;
        }

        placed++;
    }


    private bool TryPlaceOutdoorCubeLine(
        bool[,] g,
        int x, int z,
        int runX, int runZ,
        bool isHorizontalRun,
        Vector2Int s1, Vector2Int s2,
        out int cubesPlaced)
    {
        cubesPlaced = 0;
        if (coverLayout == null || g == null) return false;

        bool longX = runX >= minSightlineRun;
        bool longZ = runZ >= minSightlineRun;
        if (!longX && !longZ) return false;

        if (!IsInBounds(x, z)) return false;
        if (!g[x, z]) return false;
        if (buildingMask != null && buildingMask[x, z]) return false;
        if (!IsCoverCellFree(x, z)) return false;

        if (IsBlockedByDoorRule(x, z)) return false;
        if (TouchesAnyCover8(x, z)) return false;

        bool alongX;
        if (longX && longZ)
            alongX = (Mathf.Abs(StableHash(lastUsedSeed, x, z, 91001)) % 2) == 0;
        else
            alongX = longX;

        int dx = alongX ? 1 : 0;
        int dz = alongX ? 0 : 1;

        int localWidth = MeasurePerpWalkableWidth(g, x, z, dx, dz);
        int preferredLen = MaxChainLenForWidth(localWidth);

        int minL = Mathf.Clamp(minCubeRunLength, 1, 3);
        int maxL = Mathf.Clamp(maxCubeRunLength, 1, 3);
        preferredLen = Mathf.Clamp(preferredLen, minL, maxL);

        int[] lensOrder = preferredLen == 2 ? new[] { 2, 3, 1 } :
                          preferredLen == 3 ? new[] { 3, 2, 1 } :
                                              new[] { 1, 2, 3 };

        int[] shifts = { 0, -1, +1, -2, +2 };

        for (int li = 0; li < lensOrder.Length; li++)
        {
            int len = lensOrder[li];
            if (len < minL || len > maxL) continue;

            GetCenteredAnchor(x, z, dx, dz, len, out int baseAx, out int baseAz);

            int startShift = Mathf.Abs(StableHash(lastUsedSeed, x, z, 92000 + len)) % shifts.Length;

            for (int k = 0; k < shifts.Length; k++)
            {
                int shift = shifts[(startShift + k) % shifts.Length];
                int ax = baseAx - dx * shift;
                int az = baseAz - dz * shift;

                if (!CanPlaceCubeChainSmart(g, ax, az, dx, dz, len, s1, s2))
                    continue;

                ApplyCubeChain(ax, az, dx, dz, len);

                if (!SpawnsStillConnectedWithCover(g, s1, s2))
                {
                    RevertCubeChain(ax, az, dx, dz, len);
                    continue;
                }

                cubesPlaced = len;
                return true;
            }
        }

        return false;
    }

    private void GetCenteredAnchor(int x, int z, int dx, int dz, int len, out int ax, out int az)
    {
        int half = (len - 1) / 2;
        ax = x - dx * half;
        az = z - dz * half;
    }

    private bool CanPlaceCubeChainSmart(bool[,] g, int ax, int az, int dx, int dz, int len, Vector2Int s1, Vector2Int s2)
    {
        int minPerpWidth = int.MaxValue;

        for (int i = 0; i < len; i++)
        {
            int x = ax + dx * i;
            int z = az + dz * i;

            if (!IsInBounds(x, z)) return false;

            if (g == null || !g[x, z]) return false;
            if (buildingMask != null && buildingMask[x, z]) return false;

            if (IsWallConstrainedLaneCell(x, z)) return false;
            if (IsAdjacentToWallConstrainedLaneCell(x, z)) return false;

            if (!IsCoverCellFree(x, z)) return false;

            if (!HasOutdoorWalkableBuffer(g, x, z, cubeFootprintBuffer)) return false;

            if (IsBlockedByDoorRule(x, z)) return false;

            if (coverSpawnExcludeRadius > 0)
            {
                if (WithinManhattan(s1, x, z, coverSpawnExcludeRadius)) return false;
                if (WithinManhattan(s2, x, z, coverSpawnExcludeRadius)) return false;
            }

            if (coverDoorExcludeRadius > 0 && IsNearDoorOrDoorFront(x, z, coverDoorExcludeRadius)) return false;

            if (TouchesAnyCover8(x, z)) return false;

            int pw = MeasurePerpWalkableWidth(g, x, z, dx, dz);
            if (pw <= 0) return false;
            if (pw < minPerpWidth) minPerpWidth = pw;
        }

        int maxAllowedByWidth = MaxChainLenForWidth(minPerpWidth);
        if (len > maxAllowedByWidth) return false;

        if (!HasContinuousBypassLane(g, ax, az, dx, dz, len)) return false;

        return true;
    }

    private bool HasContinuousBypassLane(bool[,] g, int ax, int az, int dx, int dz, int len)
    {
        int pdx = -dz;
        int pdz = dx;

        bool sideAOk = true;
        bool sideBOk = true;

        for (int i = 0; i < len; i++)
        {
            int x = ax + dx * i;
            int z = az + dz * i;

            int ax1 = x + pdx, az1 = z + pdz;
            int bx1 = x - pdx, bz1 = z - pdz;

            if (sideAOk && !IsOutdoorBypassCell(g, ax1, az1)) sideAOk = false;
            if (sideBOk && !IsOutdoorBypassCell(g, bx1, bz1)) sideBOk = false;

            if (!sideAOk && !sideBOk) return false;
        }

        return true;
    }

    private bool IsOutdoorBypassCell(bool[,] g, int x, int z)
    {
        if (!IsInBounds(x, z)) return false;
        if (g == null || !g[x, z]) return false;
        if (buildingMask != null && buildingMask[x, z]) return false;

        int cg = coverLayout[x, z, spawnLevel].group;
        if (cg != 0 && !IsSpawnMarker(cg)) return false;

        if (IsBlockedByDoorRule(x, z)) return false;

        return true;
    }

    private void ApplyCubeChain(int ax, int az, int dx, int dz, int len)
    {
        for (int i = 0; i < len; i++)
        {
            int x = ax + dx * i;
            int z = az + dz * i;
            coverLayout[x, z, spawnLevel] = new TileCode(outdoorCubeGroupId, Direction.North);
        }
    }

    private void RevertCubeChain(int ax, int az, int dx, int dz, int len)
    {
        for (int i = 0; i < len; i++)
        {
            int x = ax + dx * i;
            int z = az + dz * i;
            coverLayout[x, z, spawnLevel] = TileCode.Empty;
        }
    }


    private void PlaceIndoorCover(bool[,] g)
    {
        if (indoorCoverGroupIds == null || indoorCoverGroupIds.Length == 0) return;
        if (buildingMask == null) return;
        if (layout == null) return;
        if (coverLayout == null) return;

        bool placeUpper = enableBuildingVerticality && buildingUpperLevel > 0 && buildingUpperLevel < levels;

        bool[,] visited = new bool[width, depth];

        for (int sx = 0; sx < width; sx++)
            for (int sz = 0; sz < depth; sz++)
            {
                if (!buildingMask[sx, sz] || visited[sx, sz]) continue;

                List<Vector2Int> comp = FloodFillBuildingComponent(new Vector2Int(sx, sz), visited);
                if (comp == null || comp.Count == 0) continue;

                List<Vector2Int> doors = new List<Vector2Int>();
                for (int i = 0; i < comp.Count; i++)
                {
                    var c = comp[i];
                    int tg = layout[c.x, c.y, 0].group;
                    if (IsDoorGroup(tg)) doors.Add(c);
                }

                List<Vector2Int> candidates = new List<Vector2Int>(comp.Count);

                for (int i = 0; i < comp.Count; i++)
                {
                    var c = comp[i];
                    int tg = layout[c.x, c.y, 0].group;

                    if (IsDoorGroup(tg)) continue;
                    if (tg == stairGroupId) continue;

                    if (!IsCoverCellFree(c.x, c.y, spawnLevel)) continue;

                    if (doors.Count > 0 && indoorCoverMinDoorDistance > 0)
                    {
                        int best = int.MaxValue;
                        for (int d = 0; d < doors.Count; d++)
                        {
                            int md = Mathf.Abs(doors[d].x - c.x) + Mathf.Abs(doors[d].y - c.y);
                            if (md < best) best = md;
                        }
                        if (best < indoorCoverMinDoorDistance) continue;
                    }

                    candidates.Add(c);
                }

                if (candidates.Count == 0) continue;

                int target = Mathf.Clamp(comp.Count / 10, 1, maxCoverPerBuilding);

                int wallAdjCap = Mathf.Min(1, target);
                wallAdjCap = Mathf.Min(wallAdjCap, Mathf.Max(0, target / 2));

                candidates.Sort((a, b) =>
                {
                    bool aWallAdj = IsAdjacentToBuildingWall4(a.x, a.y);
                    bool bWallAdj = IsAdjacentToBuildingWall4(b.x, b.y);

                    // Prefer interior first
                    if (aWallAdj != bWallAdj)
                        return aWallAdj ? 1 : -1;

                    // Then prefer farther from doors
                    int da = DoorDistanceScore(a, doors);
                    int db = DoorDistanceScore(b, doors);
                    if (da != db) return db.CompareTo(da);

                    // Stable tie-break
                    int ha = StableHash(lastUsedSeed, a.x, a.y, 3001);
                    int hb = StableHash(lastUsedSeed, b.x, b.y, 3001);
                    return ha.CompareTo(hb);
                });

                List<Vector2Int> chosen = new List<Vector2Int>(target);
                int wallAdjChosen = 0;

                for (int i = 0; i < candidates.Count && chosen.Count < target; i++)
                {
                    var c = candidates[i];

                    bool wallAdj = IsAdjacentToBuildingWall4(c.x, c.y);
                    if (wallAdj && wallAdjChosen >= wallAdjCap)
                        continue;

                    bool tooClose = false;
                    for (int j = 0; j < chosen.Count; j++)
                    {
                        int md = Mathf.Abs(chosen[j].x - c.x) + Mathf.Abs(chosen[j].y - c.y);
                        if (md <= 2) { tooClose = true; break; }
                    }
                    if (tooClose) continue;

                    int h = StableHash(lastUsedSeed, c.x, c.y, 3002);
                    int idx = Mathf.Abs(h) % indoorCoverGroupIds.Length;
                    int group = indoorCoverGroupIds[idx];

                    coverLayout[c.x, c.y, spawnLevel] = new TileCode(group, Direction.North);

                    if (placeUpper && buildingHasStairsMask != null && buildingHasStairsMask[c.x, c.y])
                    {
                        bool hasUpperFloorHere = layout[c.x, c.y, buildingUpperLevel].group != 0;
                        bool upperIsStair = layout[c.x, c.y, buildingUpperLevel].group == stairGroupId;

                        if (hasUpperFloorHere &&
                            !upperIsStair &&
                            IsCoverCellFree(c.x, c.y, buildingUpperLevel))
                        {
                            coverLayout[c.x, c.y, buildingUpperLevel] = new TileCode(group, Direction.North);
                        }
                    }

                    chosen.Add(c);
                    if (wallAdj) wallAdjChosen++;
                }

                if (chosen.Count == 0 && candidates.Count > 0)
                {
                    var c = candidates[0];

                    int h = StableHash(lastUsedSeed, c.x, c.y, 3003);
                    int idx = Mathf.Abs(h) % indoorCoverGroupIds.Length;
                    int group = indoorCoverGroupIds[idx];

                    if (IsCoverCellFree(c.x, c.y, spawnLevel))
                        coverLayout[c.x, c.y, spawnLevel] = new TileCode(group, Direction.North);
                }
            }
    }
    private bool IsAdjacentToBuildingWall4(int x, int z)
    {
        if (buildingMask == null) return false;
        if (x < 0 || x >= width || z < 0 || z >= depth) return false;

        if (!buildingMask[x, z]) return false;

        if (z + 1 >= depth || !buildingMask[x, z + 1]) return true; // North
        if (x + 1 >= width || !buildingMask[x + 1, z]) return true; // East
        if (z - 1 < 0 || !buildingMask[x, z - 1]) return true;      // South
        if (x - 1 < 0 || !buildingMask[x - 1, z]) return true;      // West

        return false;
    }

    private int DoorDistanceScore(Vector2Int c, List<Vector2Int> doors)
    {
        if (doors == null || doors.Count == 0) return 999;
        int best = int.MaxValue;

        for (int i = 0; i < doors.Count; i++)
        {
            int d = Mathf.Abs(doors[i].x - c.x) + Mathf.Abs(doors[i].y - c.y);
            if (d < best) best = d;
        }

        return best;
    }


}