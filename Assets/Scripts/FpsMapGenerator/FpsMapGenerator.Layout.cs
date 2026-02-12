using System;
using System.Collections.Generic;
using UnityEngine;

public partial class FpsMapGenerator : MonoBehaviour
{



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
            CarveCorridorWiggle(g, a, b, corridorThickness, minX, maxX, minZ, maxZ);
        }

        return g;


    }

   

    private int WalkableDegree(bool[,] g, int x, int z)
    {
        int deg = 0;
        if (IsWalkableCell(g, x, z + 1)) deg++;
        if (IsWalkableCell(g, x + 1, z)) deg++;
        if (IsWalkableCell(g, x, z - 1)) deg++;
        if (IsWalkableCell(g, x - 1, z)) deg++;
        return deg;
    }

    private bool IsWalkableCell(bool[,] g, int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= depth) return false;
        if (g == null) return false;
        if (!g[x, z]) return false;
        if (buildingMask != null && buildingMask[x, z]) return false;
        return true;
    }

    private int MeasureDeadEndChainLength(bool[,] g, Vector2Int tip)
    {
        // Follow the corridor until we hit a junction (deg != 2) or end (deg == 1 again).
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Vector2Int cur = tip;
        Vector2Int prev = new Vector2Int(int.MinValue, int.MinValue);

        int length = 0;

        while (true)
        {
            if (!visited.Add(cur))
                break;

            length++;

            int deg = WalkableDegree(g, cur.x, cur.y);
            if (deg != 2 && cur != tip)
                break;

            // Step to the next cell (the walkable neighbour that isn’t prev)
            if (!TryGetNextChainStep(g, cur, prev, out Vector2Int next))
                break;

            prev = cur;
            cur = next;
        }

        return length;
    }

    private bool TryGetNextChainStep(bool[,] g, Vector2Int cur, Vector2Int prev, out Vector2Int next)
    {
        // Check 4 neighbours, pick the one that is walkable and not prev.
        Vector2Int n = new Vector2Int(cur.x, cur.y + 1);
        Vector2Int e = new Vector2Int(cur.x + 1, cur.y);
        Vector2Int s = new Vector2Int(cur.x, cur.y - 1);
        Vector2Int w = new Vector2Int(cur.x - 1, cur.y);

        if (IsWalkableCell(g, n.x, n.y) && n != prev) { next = n; return true; }
        if (IsWalkableCell(g, e.x, e.y) && e != prev) { next = e; return true; }
        if (IsWalkableCell(g, s.x, s.y) && s != prev) { next = s; return true; }
        if (IsWalkableCell(g, w.x, w.y) && w != prev) { next = w; return true; }

        next = default;
        return false;
    }


    private void TryEnqueueDigNode(
        bool[,] g,
        int nx, int nz,
        Vector2Int from,
        bool[,] visited,
        Vector2Int[,] cameFrom,
        Queue<Vector2Int> q,
        Func<int, int, bool> inBounds)
    {
        if (!inBounds(nx, nz)) return;
        if (visited[nx, nz]) return;

        // Do not dig through buildings
        if (buildingMask != null && buildingMask[nx, nz]) return;

        visited[nx, nz] = true;
        cameFrom[nx, nz] = from;
        q.Enqueue(new Vector2Int(nx, nz));
    }

 

    private void CarveCorridorWiggle(bool[,] g, Vector2Int a, Vector2Int b, int thickness, int minX, int maxX, int minZ, int maxZ)
    {
        int x = a.x;
        int z = a.y;

        int guard = width * depth * 4;
        int lastDx = 0;
        int lastDz = 0;

        while ((x != b.x || z != b.y) && guard-- > 0)
        {
            CarveThick(g, x, z, thickness, minX, maxX, minZ, maxZ);

            int dx = Math.Sign(b.x - x);
            int dz = Math.Sign(b.y - z);

            bool canX = dx != 0;
            bool canZ = dz != 0;

            if (!canX && !canZ) break;

            // Prefer direction that reduces distance but occasionally turn to avoid long straight bends
            bool turn = rng.NextDouble() < 0.55;

            int stepX = 0;
            int stepZ = 0;

            if (canX && canZ)
            {
                if (turn)
                {
                    // alternate axis to introduce bends
                    if (lastDx != 0) { stepX = 0; stepZ = dz; }
                    else { stepX = dx; stepZ = 0; }
                }
                else
                {
                    // go toward dominant distance axis
                    if (Math.Abs(b.x - x) >= Math.Abs(b.y - z)) { stepX = dx; stepZ = 0; }
                    else { stepX = 0; stepZ = dz; }
                }
            }
            else if (canX) { stepX = dx; stepZ = 0; }
            else { stepX = 0; stepZ = dz; }

            lastDx = stepX;
            lastDz = stepZ;

            x += stepX;
            z += stepZ;
        }

        CarveThick(g, b.x, b.y, thickness, minX, maxX, minZ, maxZ);
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

    private void SmoothOuterBorderCorners(bool[,] g)
    {


        if (layout == null || g == null) return;
        if (borderSmoothCornerGroupId == 0) return;

        bool hasS1 = TryGetSpawnCell(1, out Vector2Int s1);
        bool hasS2 = TryGetSpawnCell(2, out Vector2Int s2);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {


                if (buildingMask != null && buildingMask[x, z])
                    continue;

                TileCode t = layout[x, z, 0];
                if (t.group != floorTwoWallsCornerGroupId)
                    continue;

                if (hasS1 && (Mathf.Abs(x - s1.x) + Mathf.Abs(z - s1.y)) <= borderSmoothingSpawnExcludeRadius)
                    continue;

                if (hasS2 && (Mathf.Abs(x - s2.x) + Mathf.Abs(z - s2.y)) <= borderSmoothingSpawnExcludeRadius)
                    continue;


                GetCornerWallDirs(t.dir, out Direction wallA, out Direction wallB);


                if (!IsVoidOnSide(g, x, z, wallA)) continue;
                if (!IsVoidOnSide(g, x, z, wallB)) continue;


                layout[x, z, 0] = new TileCode(borderSmoothCornerGroupId, t.dir);
            }
        }
    }


    private void GetCornerWallDirs(Direction dir, out Direction wallA, out Direction wallB)
    {

        switch (dir)
        {
            case Direction.North: wallA = Direction.North; wallB = Direction.West; break;
            case Direction.East: wallA = Direction.North; wallB = Direction.East; break;
            case Direction.South: wallA = Direction.East; wallB = Direction.South; break;
            case Direction.West: wallA = Direction.South; wallB = Direction.West; break;
            default: wallA = Direction.North; wallB = Direction.West; break;
        }
    }



    private bool IsVoidOnSide(bool[,] g, int x, int z, Direction side)
    {
        int nx = x + DirDx(side);
        int nz = z + DirDz(side);


        if (nx < 0 || nx >= width || nz < 0 || nz >= depth)
            return true;


        if (buildingMask != null && buildingMask[nx, nz])
            return false;

        return !g[nx, nz];
    }

    private int DirDx(Direction d)
    {
        switch (d)
        {
            case Direction.East: return 1;
            case Direction.West: return -1;
            default: return 0;
        }
    }

    private int DirDz(Direction d)
    {
        switch (d)
        {
            case Direction.North: return 1;
            case Direction.South: return -1;
            default: return 0;
        }
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
}