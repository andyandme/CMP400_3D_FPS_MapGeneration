using System;
using System.Collections.Generic;
using UnityEngine;

public partial class FpsMapGenerator : MonoBehaviour
{
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


        for (int z = z0; z < z0 + bh; z++)
            for (int x = x0; x < x0 + bw; x++)
                if (!g[x, z]) return false;


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

    private bool IsBuilding(int x, int z)
    {
        if (buildingMask == null) return false;
        if (x < 0 || x >= width || z < 0 || z >= depth) return false;
        return buildingMask[x, z];
    }
    


    private bool IsBuildingCell(int x, int z)
    {
        if (buildingMask == null) return false;
        if (x < 0 || x >= width || z < 0 || z >= depth) return false;
        return buildingMask[x, z];
    }

    private bool IsExteriorSideOfBuilding(int x, int z, Direction side)
    {
        int nx = x + DirDx(side);
        int nz = z + DirDz(side);
        return !IsBuildingCell(nx, nz);
    }

    private bool TryGetKeptCornerWall(int x, int z, Direction outwardDir, out Direction keptWall)
    {
        keptWall = Direction.None;

        // Door must face outward
        if (!IsExteriorSideOfBuilding(x, z, outwardDir))
            return false;

        // Collect all exterior sides
        List<Direction> exterior = new List<Direction>(4);
        if (IsExteriorSideOfBuilding(x, z, Direction.North)) exterior.Add(Direction.North);
        if (IsExteriorSideOfBuilding(x, z, Direction.East)) exterior.Add(Direction.East);
        if (IsExteriorSideOfBuilding(x, z, Direction.South)) exterior.Add(Direction.South);
        if (IsExteriorSideOfBuilding(x, z, Direction.West)) exterior.Add(Direction.West);

        //Corner needs at least 2 exterior sides
        if (exterior.Count < 2)
            return false;

        // Keep the other exterior side (not the opening)
        for (int i = 0; i < exterior.Count; i++)
        {
            if (exterior[i] != outwardDir)
            {
                keptWall = exterior[i];
                return true;
            }
        }

        return false;
    }

    private int GetCornerDoorVariantFromKeptWall(Direction outwardDir, Direction keptWall)
    {
        Direction leftOf = Direction.None;
        Direction rightOf = Direction.None;

        switch (outwardDir)
        {
            case Direction.North: leftOf = Direction.West; rightOf = Direction.East; break;
            case Direction.East: leftOf = Direction.North; rightOf = Direction.South; break;
            case Direction.South: leftOf = Direction.East; rightOf = Direction.West; break;
            case Direction.West: leftOf = Direction.South; rightOf = Direction.North; break;
        }

        if (keptWall == leftOf) return doorCornerLeftWallGroupId;   // 151
        if (keptWall == rightOf) return doorCornerRightWallGroupId;  // 152
        return doorGroupId; // fallback
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


        bool n = IsOpenForWallMask(g, x, z + 1);
        bool e = IsOpenForWallMask(g, x + 1, z);
        bool s = IsOpenForWallMask(g, x, z - 1);
        bool w = IsOpenForWallMask(g, x - 1, z);

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

    // Flood fill building region
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



    private void PlaceDoorTile(bool[,] g, DoorCandidate c)
    {
        int bx = c.buildingCell.x;
        int bz = c.buildingCell.y;

        TileCode prev = layout[bx, bz, 0];
        int chosenDoorGroup = doorGroupId;

       
        if (TryGetKeptCornerWall(bx, bz, c.outwardDir, out Direction keptWall))
        {
            chosenDoorGroup = GetCornerDoorVariantFromKeptWall(c.outwardDir, keptWall);
        }

        layout[bx, bz, 0] = new TileCode(chosenDoorGroup, c.outwardDir);

        Direction corridorSideFacingDoor = Opposite(c.outwardDir);
        RebuildWalkableTileWithDoorOpening(g, c.outsideCell, corridorSideFacingDoor);
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
}
