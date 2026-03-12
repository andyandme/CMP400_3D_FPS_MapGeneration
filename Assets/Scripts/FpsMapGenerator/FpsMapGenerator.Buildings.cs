using System;
using System.Collections.Generic;
using UnityEngine;

public partial class FpsMapGenerator : MonoBehaviour
{
    private void ApplyBuildingsToWalkable(bool[,] g) //Places building footprints by marking parts of the walkable grid as buildings
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
                    g[x, z] = false;            
                    buildingMask[x, z] = true;  
                }
            }

            placed++;
        }

        Debug.Log($"[FpsMapGenerator] Buildings placed: {placed}");
    }

    private bool HasClearanceOnWalkable(bool[,] g, int x0, int z0, int bw, int bh, int clearance, int minX, int maxX, int minZ, int maxZ) //Checks if building can fit without breaking spacing rules
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


    private bool IsBuildingCell(int x, int z)//Returns true if this gris cell is inside a building footprint
    {
        if (buildingMask == null) return false;
        if (x < 0 || x >= width || z < 0 || z >= depth) return false;
        return buildingMask[x, z];
    }

    private bool IsExteriorSideOfBuilding(int x, int z, Direction side) // Checks if a side of a building cell faces outside the building (Wall directions are correct)
    {
        int nx = x + DirDx(side);
        int nz = z + DirDz(side);
        return !IsBuildingCell(nx, nz);
    }

    private bool TryGetKeptCornerWall(int x, int z, Direction outwardDir, out Direction keptWall) // Choose which wall to keep in a corner door
    {
        keptWall = Direction.None;

        if (!IsExteriorSideOfBuilding(x, z, outwardDir))
            return false;

        List<Direction> exterior = new List<Direction>(4);
        if (IsExteriorSideOfBuilding(x, z, Direction.North)) exterior.Add(Direction.North);
        if (IsExteriorSideOfBuilding(x, z, Direction.East)) exterior.Add(Direction.East);
        if (IsExteriorSideOfBuilding(x, z, Direction.South)) exterior.Add(Direction.South);
        if (IsExteriorSideOfBuilding(x, z, Direction.West)) exterior.Add(Direction.West);


        if (exterior.Count < 2)
            return false;

  
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

    private void PlaceBuildingRoofSlab(List<Vector2Int> comp) // Places roof tiles above a building footprint
    {
        if (!enableBuildingRoof) return;

        int roofLevel = buildingUpperLevel + 1;
        if (roofLevel <= 0 || roofLevel >= levels) return;

        int group = (roofGroupId != 0) ? roofGroupId : buildingUpperFloorGroupId;
        if (group == 0) return;

        for (int i = 0; i < comp.Count; i++)
        {
            var c = comp[i];
            layout[c.x, c.y, roofLevel] = new TileCode(group, Direction.North);
        }
    }


    private int GetCornerDoorVariantFromKeptWall(Direction outwardDir, Direction keptWall)//Chooses the correct door tile (prefab) for corner tiles
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


    private Direction Opposite(Direction d) //Returns the opposite Direction (e.g. IF north then return south)
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


    private void PlaceDoorsForAllBuildings(bool[,] g) //Adds doors to each building
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


    private List<Vector2Int> FloodFillBuildingComponent(Vector2Int start, bool[,] visited) //Finds all connected building cells so it is all treated as one building
    {
        List<Vector2Int> component = new List<Vector2Int>();
        Queue<Vector2Int> q = new Queue<Vector2Int>();

        visited[start.x, start.y] = true;
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            component.Add(cur);

            TryEnqueueBuildingNeighbour(cur.x, cur.y + 1, visited, q);
            TryEnqueueBuildingNeighbour(cur.x + 1, cur.y, visited, q);
            TryEnqueueBuildingNeighbour(cur.x, cur.y - 1, visited, q);
            TryEnqueueBuildingNeighbour(cur.x - 1, cur.y, visited, q);
        }

        return component;
    }

    private void TryEnqueueBuildingNeighbour(int nx, int nz, bool[,] visited, Queue<Vector2Int> q) //adds a nearby building cell to the flood fill search
    {
        if (nx < 0 || nx >= width || nz < 0 || nz >= depth) return;
        if (visited[nx, nz]) return;
        if (buildingMask == null || !buildingMask[nx, nz]) return;

        visited[nx, nz] = true;
        q.Enqueue(new Vector2Int(nx, nz));
    }

    private DoorCandidate? PickBestDoorCandidate(List<DoorCandidate> cands, int[,] dist, DoorCandidate? exclude) //Picks the best door spot based on distance to spawn
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



    private void PlaceDoorTile(bool[,] g, DoorCandidate c) //Places the actual Door Tile
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

        //Direction corridorSideFacingDoor = Opposite(c.outwardDir);
    }


    private List<DoorCandidate> GatherDoorCandidatesForComponent(List<Vector2Int> component, bool[,] g) //Collects all the possible Door position around the building
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

    //private void GetComponentBounds(List<Vector2Int> comp, out int minX, out int maxX, out int minZ, out int maxZ) //Finds the bounding box size of a building footprint
    //{
    //    minX = int.MaxValue; maxX = int.MinValue;
    //    minZ = int.MaxValue; maxZ = int.MinValue;

    //    for (int i = 0; i < comp.Count; i++)
    //    {
    //        int x = comp[i].x;
    //        int z = comp[i].y;
    //        if (x < minX) minX = x;
    //        if (x > maxX) maxX = x;
    //        if (z < minZ) minZ = z;
    //        if (z > maxZ) maxZ = z;
    //    }
    //}

    private int GetStairCountForBuilding(List<Vector2Int> comp) // Decides if it needs 0 or 2 staircases
    {
        if (comp == null) return 0;

        int cellCount = comp.Count;

        if (cellCount < mediumBuildingMinCells)
        { 
            return 0;
        }
        return 2;
    }

    private void TryAddDoorCandidate(List<DoorCandidate> candidates, bool[,] g, int bx, int bz, int ox, int oz, Direction outward) //Adds a door canditate is the outside cell is walkable
    {
        if (ox < 0 || ox >= width || oz < 0 || oz >= depth) return;
        if (!g[ox, oz]) return;

        candidates.Add(new DoorCandidate(new Vector2Int(bx, bz), outward, new Vector2Int(ox, oz)));
    }

    private void PlaceBuildingVerticality(bool[,] g) //Create the upper floor, creates stair holes, and places the stairs
    {
        if (!enableBuildings) return;
        if (!enableBuildingVerticality) return;
        if (buildingMask == null || layout == null) return;
        if (buildingUpperLevel <= 0 || buildingUpperLevel >= levels) return;

        if (buildingHasStairsMask == null || buildingHasStairsMask.GetLength(0) != width || buildingHasStairsMask.GetLength(1) != depth)
            buildingHasStairsMask = new bool[width, depth];
        else
            System.Array.Clear(buildingHasStairsMask, 0, buildingHasStairsMask.Length);

        bool[,] visited = new bool[width, depth];

        for (int sx = 0; sx < width; sx++)
            for (int sz = 0; sz < depth; sz++)
            {
                if (!buildingMask[sx, sz] || visited[sx, sz]) continue;

                List<Vector2Int> comp = FloodFillBuildingComponent(new Vector2Int(sx, sz), visited);
                if (comp == null || comp.Count == 0) continue;

                int stairsToPlace = GetStairCountForBuilding(comp);

                if (stairsToPlace <= 0)
                {
                    PlaceUpperFloorSlab(comp, null);
                    continue;
                }


                List<Vector2Int> doors = new List<Vector2Int>();
                for (int i = 0; i < comp.Count; i++)
                {
                    var c = comp[i];
                    if (IsDoorGroup(layout[c.x, c.y, 0].group))
                        doors.Add(c);
                }


                if (doors.Count > 0)
                    stairsToPlace = Mathf.Min(stairsToPlace, doors.Count);

                List<Vector2Int> candidates = new List<Vector2Int>(comp.Count);
                for (int i = 0; i < comp.Count; i++)
                {
                    var c = comp[i];

     
                    if (IsDoorGroup(layout[c.x, c.y, 0].group)) continue;

                    if (!IsBuildingCell(c.x, c.y + 1)) continue;
                    if (!IsBuildingCell(c.x + 1, c.y)) continue;
                    if (!IsBuildingCell(c.x, c.y - 1)) continue;
                    if (!IsBuildingCell(c.x - 1, c.y)) continue;

                    if (doors.Count > 0 && stairMinDoorDistance > 0)
                    {
                        int best = int.MaxValue;
                        for (int d = 0; d < doors.Count; d++)
                        {
                            int md = Mathf.Abs(doors[d].x - c.x) + Mathf.Abs(doors[d].y - c.y);
                            if (md < best) best = md;
                        }
                        if (best < stairMinDoorDistance) continue;
                    }

                    candidates.Add(c);
                }

                if (candidates.Count == 0)
                {
                    for (int i = 0; i < comp.Count; i++)
                    {
                        var c = comp[i];
                        if (IsDoorGroup(layout[c.x, c.y, 0].group)) continue;

                        int nb =
                            (IsBuildingCell(c.x, c.y + 1) ? 1 : 0) +
                            (IsBuildingCell(c.x + 1, c.y) ? 1 : 0) +
                            (IsBuildingCell(c.x, c.y - 1) ? 1 : 0) +
                            (IsBuildingCell(c.x - 1, c.y) ? 1 : 0);

                        if (nb < 3) continue;
                        candidates.Add(c);
                    }
                }

                if (candidates.Count == 0)
                {

                    PlaceUpperFloorSlab(comp, null);
                    continue;
                }

                List<StairAssignment> stairsPicked = PickStairsFacingDistinctDoorsDeterministic(candidates, doors, stairsToPlace);

                if (stairsPicked == null || stairsPicked.Count == 0)
                {

                    PlaceUpperFloorSlab(comp, null);
                    continue;
                }

                for (int i = 0; i < comp.Count; i++)
                {
                    var c = comp[i];
                    buildingHasStairsMask[c.x, c.y] = true;
                }

                for (int i = 0; i < stairsPicked.Count; i++)
                {
                    var s = stairsPicked[i];
                    Direction stairDir = ChooseStairDirectionTowardDoor(s.stairCell, s.doorCell);
                    layout[s.stairCell.x, s.stairCell.y, 0] = new TileCode(stairGroupId, stairDir);
                }

                List<Vector2Int> stairCells = new List<Vector2Int>(stairsPicked.Count);
                for (int i = 0; i < stairsPicked.Count; i++)
                    stairCells.Add(stairsPicked[i].stairCell);

                HashSet<long> holeSet = BuildHoleSetFromStairs(stairCells);

                PlaceUpperFloorSlab(comp, stairCells);

                BuildUpperFloorPerimeterWalls(comp, holeSet);

                PlaceBuildingRoofSlab(comp);
            }
    }


    private struct StairAssignment 
    {
        public Vector2Int stairCell;
        public Vector2Int doorCell;

        public StairAssignment(Vector2Int stair, Vector2Int door)
        {
            stairCell = stair;
            doorCell = door;
        }
    }

    private List<StairAssignment> PickStairsFacingDistinctDoorsDeterministic(List<Vector2Int> candidates, List<Vector2Int> doors, int count) //Chooses stair locations so they are spaced apart and each one faces a different door
    {
        List<StairAssignment> result = new List<StairAssignment>(count);
        if (count <= 0) return result;
        if (candidates == null || candidates.Count == 0) return result;

        if (doors == null || doors.Count == 0)
        {
            List<Vector2Int> stairs = PickStairCellsDeterministic(candidates, count);
            for (int i = 0; i < stairs.Count; i++)
                result.Add(new StairAssignment(stairs[i], default));
            return result;
        }

        List<Vector2Int> orderedDoors = new List<Vector2Int>(doors);
        orderedDoors.Sort((a, b) =>
        {
            int ha = StableHash(lastUsedSeed, a.x, a.y, 7001);
            int hb = StableHash(lastUsedSeed, b.x, b.y, 7001);
            return ha.CompareTo(hb);
        });

        int target = Mathf.Min(count, orderedDoors.Count);

        int minSep = Mathf.Max(2, minStairSeparation); 

        for (int di = 0; di < orderedDoors.Count && result.Count < target; di++)
        {
            Vector2Int door = orderedDoors[di];

            Vector2Int best = default;
            bool found = false;
            int bestScore = int.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2Int c = candidates[i];

                bool tooClose = false;
                for (int j = 0; j < result.Count; j++)
                {
                    int md = Mathf.Abs(result[j].stairCell.x - c.x) + Mathf.Abs(result[j].stairCell.y - c.y);
                    if (md < minSep) { tooClose = true; break; }
                }
                if (tooClose) continue;

                int dist = Mathf.Abs(door.x - c.x) + Mathf.Abs(door.y - c.y);

                int tie = Mathf.Abs(StableHash(lastUsedSeed, c.x, c.y, 7002 + di)) % 997;

                int score = dist * 1000 + tie;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                    found = true;
                }
            }

            if (!found) continue;

            candidates.Remove(best);

            result.Add(new StairAssignment(best, door));
        }

        if (result.Count < target)
        {
            int relaxedMinSep = 2; 
            for (int di = 0; di < orderedDoors.Count && result.Count < target; di++)
            {
                Vector2Int door = orderedDoors[di];

                bool doorUsed = false;
                for (int k = 0; k < result.Count; k++)
                    if (result[k].doorCell == door) { doorUsed = true; break; }
                if (doorUsed) continue;

                Vector2Int best = default;
                bool found = false;
                int bestScore = int.MaxValue;

                for (int i = 0; i < candidates.Count; i++)
                {
                    Vector2Int c = candidates[i];

                    bool tooClose = false;
                    for (int j = 0; j < result.Count; j++)
                    {
                        int md = Mathf.Abs(result[j].stairCell.x - c.x) + Mathf.Abs(result[j].stairCell.y - c.y);
                        if (md < relaxedMinSep) { tooClose = true; break; }
                    }
                    if (tooClose) continue;

                    int dist = Mathf.Abs(door.x - c.x) + Mathf.Abs(door.y - c.y);
                    int tie = Mathf.Abs(StableHash(lastUsedSeed, c.x, c.y, 7102 + di)) % 997;
                    int score = dist * 1000 + tie;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                        found = true;
                    }
                }

                if (!found) continue;

                candidates.Remove(best);
                result.Add(new StairAssignment(best, door));
            }
        }

        return result;
    }

    private Direction ChooseStairDirectionTowardDoor(Vector2Int stair, Vector2Int door) //Rotates the stairs so they point towards their assigned door
    {
        int dx = door.x - stair.x;
        int dz = door.y - stair.y;

        if (Mathf.Abs(dx) > Mathf.Abs(dz))
            return (dx >= 0) ? Direction.East : Direction.West;
        else
            return (dz >= 0) ? Direction.North : Direction.South;
    }


    private List<Vector2Int> PickStairCellsDeterministic(List<Vector2Int> candidates, int count) //Picks stair cells in a repeatable way so that the same seed will give the same result
    {
        List<Vector2Int> result = new List<Vector2Int>(count);
        if (candidates == null || candidates.Count == 0 || count <= 0) return result;

        candidates.Sort((a, b) =>
        {
            int sa = StairCellScore(a);
            int sb = StairCellScore(b);
            if (sa != sb) return sb.CompareTo(sa);

            int ha = StableHash(lastUsedSeed, a.x, a.y, 9101);
            int hb = StableHash(lastUsedSeed, b.x, b.y, 9101);
            return ha.CompareTo(hb);
        });

        for (int i = 0; i < candidates.Count && result.Count < count; i++)
        {
            var c = candidates[i];

            bool tooClose = false;
            for (int j = 0; j < result.Count; j++)
            {
                int md = Mathf.Abs(result[j].x - c.x) + Mathf.Abs(result[j].y - c.y);
                if (md < minStairSeparation) { tooClose = true; break; }
            }
            if (tooClose) continue;

            result.Add(c);
        }

        if (result.Count < count)
        {
            for (int i = 0; i < candidates.Count && result.Count < count; i++)
            {
                var c = candidates[i];
                if (result.Contains(c)) continue;
                result.Add(c);
            }
        }

        return result;
    }

    private int StairCellScore(Vector2Int c) //Gives a stair spot a score based on how inside the building it is.
    {
        int nb =
            (IsBuildingCell(c.x, c.y + 1) ? 1 : 0) +
            (IsBuildingCell(c.x + 1, c.y) ? 1 : 0) +
            (IsBuildingCell(c.x, c.y - 1) ? 1 : 0) +
            (IsBuildingCell(c.x - 1, c.y) ? 1 : 0);

        return nb;
    }


    private void PlaceUpperFloorSlab(List<Vector2Int> comp, List<Vector2Int> stairCells) //Places the ceiling tiles for the Building leaving holes for stairs
    {
        if (buildingUpperFloorGroupId == 0) return;

        HashSet<long> holeSet = new HashSet<long>();
        if (stairCells != null)
        {
            for (int i = 0; i < stairCells.Count; i++)
            {
                var s = stairCells[i];
                AddHoleArea(holeSet, s.x, s.y, stairHoleRadius);
            }
        }

        for (int i = 0; i < comp.Count; i++)
        {
            var c = comp[i];

            if (holeSet.Count > 0)
            {
                long key = ((long)c.x << 32) ^ (uint)c.y;
                if (holeSet.Contains(key)) continue;
            }

            layout[c.x, c.y, buildingUpperLevel] = new TileCode(buildingUpperFloorGroupId, Direction.North);
        }
    }

    private void AddHoleArea(HashSet<long> holeSet, int x, int z, int r)
    {
        if (holeSet == null) return;
        r = Mathf.Max(0, r);

        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx;
                int nz = z + dz;
                if (nx < 0 || nx >= width || nz < 0 || nz >= depth) continue;

                long key = ((long)nx << 32) ^ (uint)nz;
                holeSet.Add(key);
            }
    }

    private void PlaceBuildingWindowsGroundFloor(bool[,] g)
    {
        if (!enableBuildingWindows) return;
        if (windowWallGroupId == 0) return;
        if (maxWindowsPerGroundFloor <= 0) return;

        if (g == null) return;
        if (buildingMask == null) return;
        if (layout == null) return;

        bool[,] visited = new bool[width, depth];

        int buildingsProcessed = 0;
        int windowsPlacedTotal = 0;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (!buildingMask[x, z] || visited[x, z]) continue;

                List<Vector2Int> comp = FloodFillBuildingComponent(new Vector2Int(x, z), visited);
                if (comp == null || comp.Count == 0) continue;

                List<WindowCandidate> cands = GatherGroundFloorWindowCandidates(comp, g);

                if (cands.Count == 0)
                {
                    buildingsProcessed++;
                    continue;
                }

                int budget = ComputeWindowBudgetRuleSetA(comp, g, maxWindowsPerGroundFloor);

                budget = Mathf.Min(budget, cands.Count);

                int placedThisBuilding = PlaceWindowsDistributed(cands, budget, 0);

                windowsPlacedTotal += placedThisBuilding;
                buildingsProcessed++;
            }
        }

        Debug.Log($"[FpsMapGenerator] Windows placed (ground floor): {windowsPlacedTotal} across {buildingsProcessed} buildings.");
    }

    private List<WindowCandidate> GatherGroundFloorWindowCandidates(List<Vector2Int> comp, bool[,] g)
{
    List<WindowCandidate> result = new List<WindowCandidate>(64);

    for (int i = 0; i < comp.Count; i++)
    {
        var c = comp[i];
        int x = c.x;
        int z = c.y;

        if (IsDoorGroup(layout[x, z, 0].group)) continue;

        int extCount = 0;
        Direction extDir = Direction.None;

        if (!IsBuildingCell(x, z + 1)) { extCount++; extDir = Direction.North; }
        if (!IsBuildingCell(x + 1, z)) { extCount++; extDir = Direction.East; }
        if (!IsBuildingCell(x, z - 1)) { extCount++; extDir = Direction.South; }
        if (!IsBuildingCell(x - 1, z)) { extCount++; extDir = Direction.West; }

        if (extCount != 1) continue;

        int ox = x + DirDx(extDir);
        int oz = z + DirDz(extDir);

        if (ox < 0 || ox >= width || oz < 0 || oz >= depth) continue;
        if (!g[ox, oz]) continue;
        if (buildingMask[ox, oz]) continue;

        TileCode t = layout[x, z, 0];
        if (t.group != floorOneWallGroupId) continue;


        if (t.dir != extDir) continue;

        result.Add(new WindowCandidate(new Vector2Int(x, z), extDir));
    }

    result.Sort((a, b) =>
    {
        int ha = StableHash(lastUsedSeed, a.cell.x, a.cell.y, 6611);
        int hb = StableHash(lastUsedSeed, b.cell.x, b.cell.y, 6611);
        return ha.CompareTo(hb);
    });

    return result;
}



    private struct WindowCandidate
    {
        public Vector2Int cell;
        public Direction outwardDir;

        public WindowCandidate(Vector2Int c, Direction d)
        {
            cell = c;
            outwardDir = d;
        }
    }

    private void PlaceBuildingWindowsUpperFloor(bool[,] g)
    {
        if (!enableBuildingWindows) return;
        if (windowWallGroupId == 0) return;
        if (maxWindowsPerUpperFloor <= 0) return;

        if (g == null) return;
        if (buildingMask == null) return;
        if (layout == null) return;

        if (buildingUpperLevel <= 0 || buildingUpperLevel >= levels) return;

        if (!TryGetSpawnCell(1, out Vector2Int s1) || !TryGetSpawnCell(2, out Vector2Int s2))
        {
            Debug.LogWarning("[FpsMapGenerator] Windows not placed: missing spawns");
            return;
        }

        bool[,] visited = new bool[width, depth];

        int buildingsProcessed = 0;
        int windowsPlacedTotal = 0;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (!buildingMask[x, z] || visited[x, z]) continue;

                List<Vector2Int> comp = FloodFillBuildingComponent(new Vector2Int(x, z), visited);
                if (comp == null || comp.Count == 0) continue;

                List<WindowCandidate> cands = GatherUpperFloorWindowCandidates(comp, g, s1, s2);

                if (cands.Count == 0)
                {
                    buildingsProcessed++;
                    continue;
                }

                int budget = ComputeWindowBudgetRuleSetA(comp, g, maxWindowsPerUpperFloor);

                budget = Mathf.Min(budget, cands.Count);

                int placedThisBuilding = PlaceWindowsDistributed(cands, budget, buildingUpperLevel);

                windowsPlacedTotal += placedThisBuilding;
                buildingsProcessed++;
            }
        }

        Debug.Log($"[FpsMapGenerator] Windows placed (upper floor): {windowsPlacedTotal} across {buildingsProcessed} buildings.");
    }

    private List<WindowCandidate> GatherUpperFloorWindowCandidates(List<Vector2Int> comp, bool[,] g, Vector2Int s1, Vector2Int s2)
    {
        List<WindowCandidate> result = new List<WindowCandidate>(64);

        for (int i = 0; i < comp.Count; i++)
        {
            var c = comp[i];
            int x = c.x;
            int z = c.y;


            if (layout[x, z, buildingUpperLevel].group == 0) continue;


            int extCount = 0;
            Direction extDir = Direction.None;

            if (!IsBuildingCell(x, z + 1)) { extCount++; extDir = Direction.North; }
            if (!IsBuildingCell(x + 1, z)) { extCount++; extDir = Direction.East; }
            if (!IsBuildingCell(x, z - 1)) { extCount++; extDir = Direction.South; }
            if (!IsBuildingCell(x - 1, z)) { extCount++; extDir = Direction.West; }

            if (extCount != 1) continue;

            int ox = x + DirDx(extDir);
            int oz = z + DirDz(extDir);

            if (ox < 0 || ox >= width || oz < 0 || oz >= depth) continue;
            if (!g[ox, oz]) continue;             
            if (buildingMask[ox, oz]) continue;        


            if (FacesPointCardinal(extDir, x, z, s1.x, s1.y)) continue;
            if (FacesPointCardinal(extDir, x, z, s2.x, s2.y)) continue;

            TileCode upper = layout[x, z, buildingUpperLevel];
            if (upper.group != floorOneWallGroupId) continue;

            if (upper.dir != extDir) continue;

            result.Add(new WindowCandidate(new Vector2Int(x, z), extDir));
        }

        result.Sort((a, b) =>
        {
            int ha = StableHash(lastUsedSeed, a.cell.x, a.cell.y, 7711);
            int hb = StableHash(lastUsedSeed, b.cell.x, b.cell.y, 7711);
            return ha.CompareTo(hb);
        });

        return result;
    }


    private void ComputeBuildingFacadeExposure(bool[,] g, List<Vector2Int> comp,
        out int exposureN, out int exposureE, out int exposureS, out int exposureW)
    {
        exposureN = exposureE = exposureS = exposureW = 0;
        if (g == null || comp == null) return;

        for (int i = 0; i < comp.Count; i++)
        {
            int x = comp[i].x;
            int z = comp[i].y;


            // North
            if (!IsBuildingCell(x, z + 1))
            {
                int ox = x;
                int oz = z + 1;
                if (ox >= 0 && ox < width && oz >= 0 && oz < depth && g[ox, oz]) exposureN++;
            }
            // East
            if (!IsBuildingCell(x + 1, z))
            {
                int ox = x + 1;
                int oz = z;
                if (ox >= 0 && ox < width && oz >= 0 && oz < depth && g[ox, oz]) exposureE++;
            }
            // South
            if (!IsBuildingCell(x, z - 1))
            {
                int ox = x;
                int oz = z - 1;
                if (ox >= 0 && ox < width && oz >= 0 && oz < depth && g[ox, oz]) exposureS++;
            }
            // West
            if (!IsBuildingCell(x - 1, z))
            {
                int ox = x - 1;
                int oz = z;
                if (ox >= 0 && ox < width && oz >= 0 && oz < depth && g[ox, oz]) exposureW++;
            }
        }
    }


    private void ComputeBuildingSignature(List<Vector2Int> comp, out int cx, out int cz, out int minX, out int minZ)
    {
        cx = cz = 0;
        minX = int.MaxValue;
        minZ = int.MaxValue;

        if (comp == null || comp.Count == 0) return;

        for (int i = 0; i < comp.Count; i++)
        {
            int x = comp[i].x;
            int z = comp[i].y;

            cx += x;
            cz += z;

            if (x < minX) minX = x;
            if (z < minZ) minZ = z;
        }

        cx /= comp.Count;
        cz /= comp.Count;
    }

    private int ComputeWindowBudgetRuleSetA(List<Vector2Int> comp, bool[,] g, int maxForThisFloor)
    {
        if (comp == null || comp.Count == 0) return 0;
        if (g == null) return 0;
        if (maxForThisFloor <= 0) return 0;

        ComputeBuildingFacadeExposure(g, comp, out int eN, out int eE, out int eS, out int eW);
        int E = eN + eE + eS + eW;
        int A = comp.Count;

        // Base budget
        int budget = 1;

        if (A >= mediumBuildingMinCells || E >= 10) budget = 2;
        if (A >= largeBuildingMinCells || E >= 16) budget = 3;

        ComputeBuildingSignature(comp, out int cx, out int cz, out int minX, out int minZ);

        int h = StableHash(lastUsedSeed, cx, cz, A ^ (minX * 31 + minZ * 17));

        int nudge = (Mathf.Abs(h) % 3) - 1; // -1,0,+1

        budget = Mathf.Clamp(budget + nudge, 1, 3);

        budget = Mathf.Clamp(budget, 1, Mathf.Min(3, maxForThisFloor));

        return budget;
    }

    private int PlaceWindowsDistributed(List<WindowCandidate> cands, int target, int level)
    {
        if (cands == null || cands.Count == 0 || target <= 0) return 0;
        if (layout == null) return 0;
        if (level < 0 || level >= levels) return 0;

        List<WindowCandidate> north = new List<WindowCandidate>();
        List<WindowCandidate> east = new List<WindowCandidate>();
        List<WindowCandidate> south = new List<WindowCandidate>();
        List<WindowCandidate> west = new List<WindowCandidate>();

        for (int i = 0; i < cands.Count; i++)
        {
            switch (cands[i].outwardDir)
            {
                case Direction.North: north.Add(cands[i]); break;
                case Direction.East: east.Add(cands[i]); break;
                case Direction.South: south.Add(cands[i]); break;
                case Direction.West: west.Add(cands[i]); break;
            }
        }

        Direction[] dirOrder = BuildDirectionOrderForBuilding(cands);

        int placed = 0;


        for (int i = 0; i < dirOrder.Length && placed < target; i++)
        {
            if (TryPlaceWindowFromBucket(dirOrder[i], north, east, south, west, level))
                placed++;
        }

        int guard = 64;
        while (placed < target && guard-- > 0)
        {
            bool any = false;
            for (int i = 0; i < dirOrder.Length && placed < target; i++)
            {
                if (TryPlaceWindowFromBucket(dirOrder[i], north, east, south, west, level))
                {
                    placed++;
                    any = true;
                }
            }
            if (!any) break;
        }

        return placed;
    }

    private bool TryPlaceWindowFromBucket(
    Direction d,
    List<WindowCandidate> north,
    List<WindowCandidate> east,
    List<WindowCandidate> south,
    List<WindowCandidate> west,
    int level)
    {
        List<WindowCandidate> bucket = null;

        switch (d)
        {
            case Direction.North: bucket = north; break;
            case Direction.East: bucket = east; break;
            case Direction.South: bucket = south; break;
            case Direction.West: bucket = west; break;
        }

        if (bucket == null || bucket.Count == 0) return false;

        WindowCandidate c = bucket[0];
        bucket.RemoveAt(0);

        layout[c.cell.x, c.cell.y, level] = new TileCode(windowWallGroupId, c.outwardDir);

        return true;
    }

    private Direction[] BuildDirectionOrderForBuilding(List<WindowCandidate> cands)
    {

        int cx = 0, cz = 0;
        for (int i = 0; i < cands.Count; i++)
        {
            cx += cands[i].cell.x;
            cz += cands[i].cell.y;
        }
        cx /= Mathf.Max(1, cands.Count);
        cz /= Mathf.Max(1, cands.Count);

        int h = Mathf.Abs(StableHash(lastUsedSeed, cx, cz, 8801));
        int rot = h % 4;

        Direction[] baseOrder = { Direction.North, Direction.East, Direction.South, Direction.West };
        Direction[] order = new Direction[4];
        for (int i = 0; i < 4; i++)
            order[i] = baseOrder[(i + rot) % 4];

        return order;
    }

    private bool FacesPointCardinal(Direction outward, int fromX, int fromZ, int targetX, int targetZ)
    {
        int dx = targetX - fromX;
        int dz = targetZ - fromZ;

        // If target is exactly on the cell, ignore.
        if (dx == 0 && dz == 0) return false;

        // Determine the dominant cardinal direction from cell -> target.
        if (Mathf.Abs(dx) >= Mathf.Abs(dz))
        {
            // East/West dominant
            Direction dir = (dx >= 0) ? Direction.East : Direction.West;
            return dir == outward;
        }
        else
        {
            // North/South dominant
            Direction dir = (dz >= 0) ? Direction.North : Direction.South;
            return dir == outward;
        }
    }
    private void BuildUpperFloorPerimeterWalls(List<Vector2Int> comp, HashSet<long> holeSet)
    {
        if (layout == null) return;
        if (buildingUpperLevel <= 0 || buildingUpperLevel >= levels) return;

        for (int i = 0; i < comp.Count; i++)
        {
            var c = comp[i];
            int x = c.x;
            int z = c.y;

            if (holeSet != null)
            {
                long key = ((long)x << 32) ^ (uint)z;
                if (holeSet.Contains(key))
                {
                    layout[x, z, buildingUpperLevel] = TileCode.Empty;
                    continue;
                }
            }

    
            bool wallN = !IsBuildingCell(x, z + 1);
            bool wallE = !IsBuildingCell(x + 1, z);
            bool wallS = !IsBuildingCell(x, z - 1);
            bool wallW = !IsBuildingCell(x - 1, z);

            int wallCount =
                (wallN ? 1 : 0) +
                (wallE ? 1 : 0) +
                (wallS ? 1 : 0) +
                (wallW ? 1 : 0);

            layout[x, z, buildingUpperLevel] = BuildTileFromWalls(wallN, wallE, wallS, wallW, wallCount);
        }
    }

    private HashSet<long> BuildHoleSetFromStairs(List<Vector2Int> stairs)
    {
        if (stairs == null || stairs.Count == 0) return null;

        HashSet<long> holeSet = new HashSet<long>();
        for (int i = 0; i < stairs.Count; i++)
        {
            var s = stairs[i];
            AddHoleArea(holeSet, s.x, s.y, stairHoleRadius);
        }
        return holeSet;
    }
}
