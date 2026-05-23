using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private RectInt dungeonBounds = new(0, 0, 100, 100);
    [SerializeField] private int minRoomSize = 12;
    [SerializeField] private int maxDepth = 5;

    [Header("Scale")]
    [SerializeField] private int cellSize = 4;
    [SerializeField] private int wallHeight = 4;
    [SerializeField] private int doorWidth = 4;
    [SerializeField] private int doorHeight = 4;

    [Header("Prefabs")]
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject columnPrefab;
    [SerializeField] private GameObject doorPrefab;
    [SerializeField] private GameObject floorPrefab;

    [Header("Navigation")]
    [SerializeField] private NavMeshSurface navMeshSurface;
    [SerializeField] private Transform player;

    [Header("Generation")]
    [SerializeField] private int seed = 0;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private float stepDelay = 0.5f;

    [Header("Settings")]
    [SerializeField] private bool generateOnStart = true;

    private List<Room> _rooms = new();
    private List<Door> _doors = new();
    private GameObject _dungeonRoot;

    private List<Room> _displayRooms = new();
    private List<Door> _displayDoors = new();
    private List<(Room roomA, Room roomB, RectInt doorBounds)> _displayCandidates = new();

    public int CellSize => cellSize;
    public int WallHeight => wallHeight;
    public int DoorWidth => doorWidth;
    public int DoorHeight => doorHeight;
    public int Seed => seed;

    private void Start()
    {
        if (generateOnStart) GenerateDungeon();
    }

    [Button]
    public void GenerateDungeon()
    {
        StopAllCoroutines();
        StartCoroutine(GenerateDungeonCoroutine());
    }

    private IEnumerator GenerateDungeonCoroutine()
    {
        _rooms.Clear();
        _doors.Clear();
        _displayRooms.Clear();
        _displayDoors.Clear();
        _displayCandidates.Clear();
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();

        if (_dungeonRoot != null) DestroyImmediate(_dungeonRoot);
        _dungeonRoot = new GameObject("- Dungeon Root -");

        if (useRandomSeed) seed = Random.Range(0, int.MaxValue);
        Random.InitState(seed);

        yield return StartCoroutine(StepGenerateRooms());

        var candidates = FindAdjacentPairs();
        yield return StartCoroutine(StepShowCandidates(candidates));
        yield return StartCoroutine(StepBuildSpanningTree(candidates));

        Debug.Log($"Connectivity: {(IsFullyConnected() ? "PASSED" : "FAILED")}");

        yield return StartCoroutine(StepRemoveSmallRooms());

        int[,] tilemap = BuildTilemap();
        SpawnWalls(tilemap);
        SpawnFloor(tilemap);

        BakeNavMesh();
        PlacePlayer();

        RefreshDebug();

        Debug.Log($"Done — {_rooms.Count} rooms | {_doors.Count} doors | Seed: {seed}");
    }

    // --- Generation steps ---
    private IEnumerator StepGenerateRooms()
    {
        Split(dungeonBounds, 0);
        foreach (var room in _rooms)
        {
            _displayRooms.Add(room);
            RefreshDebug();
            yield return new WaitForSeconds(stepDelay);
        }
    }

    private IEnumerator StepShowCandidates(List<(Room, Room, RectInt)> candidates)
    {
        foreach (var candidate in candidates)
        {
            _displayCandidates.Add(candidate);
            RefreshDebug();
            yield return new WaitForSeconds(stepDelay * 0.2f);
        }
        yield return new WaitForSeconds(stepDelay);
        _displayCandidates.Clear();
    }

    private IEnumerator StepBuildSpanningTree(List<(Room roomA, Room roomB, RectInt doorBounds)> candidates)
    {
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var roomIndex = new Dictionary<Room, int>();
        for (int i = 0; i < _rooms.Count; i++) roomIndex[_rooms[i]] = i;

        int[] parent = new int[_rooms.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        foreach (var (roomA, roomB, doorBounds) in candidates)
        {
            if (Union(parent, roomIndex[roomA], roomIndex[roomB]))
            {
                Door door = new Door(roomA, roomB, doorBounds, doorWidth, doorHeight);
                roomA.Doors.Add(door);
                roomB.Doors.Add(door);
                _doors.Add(door);
                _displayDoors.Add(door);
                RefreshDebug();
                yield return new WaitForSeconds(stepDelay);
            }
        }
    }

    private IEnumerator StepRemoveSmallRooms()
    {
        int removeCount = Mathf.CeilToInt(_rooms.Count * 0.1f);
        var sorted = new List<Room>(_rooms);
        sorted.Sort((a, b) => (a.Bounds.width * a.Bounds.height).CompareTo(b.Bounds.width * b.Bounds.height));

        int removed = 0;
        foreach (Room room in sorted)
        {
            if (removed >= removeCount) break;

            var backedUpDoors = new List<Door>(room.Doors);

            _rooms.Remove(room);
            _displayRooms.Remove(room);
            foreach (var door in backedUpDoors)
            {
                door.GetOtherRoom(room).Doors.Remove(door);
                _doors.Remove(door);
                _displayDoors.Remove(door);
            }
            room.Doors.Clear();

            RefreshDebug();
            yield return new WaitForSeconds(stepDelay);

            if (IsFullyConnected())
            {
                removed++;
            }
            else
            {
                _rooms.Add(room);
                _displayRooms.Add(room);
                foreach (var door in backedUpDoors)
                {
                    door.GetOtherRoom(room).Doors.Add(door);
                    _doors.Add(door);
                    _displayDoors.Add(door);
                }
                room.Doors.AddRange(backedUpDoors);
                RefreshDebug();
            }
        }
    }

    // --- NavMesh & Player ---
    [Button]
    private void BakeNavMesh()
    {
        if (navMeshSurface == null) return;
        navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        navMeshSurface.BuildNavMesh();
    }

    private void PlacePlayer()
    {
        if (player == null || _rooms.Count == 0) return;

        Room spawnRoom = _rooms[Random.Range(0, _rooms.Count)];
        Vector3 roomCenter = new Vector3(spawnRoom.Center.x, 0, spawnRoom.Center.y);

        if (NavMesh.SamplePosition(roomCenter, out NavMeshHit hit, cellSize * 2f, NavMesh.AllAreas))
            player.position = hit.position;
        else
            player.position = roomCenter;
    }

    // --- Tilemap ---
    private int[,] BuildTilemap()
    {
        int rows = dungeonBounds.height / cellSize;
        int cols = dungeonBounds.width / cellSize;
        int[,] tilemap = new int[rows, cols];

        foreach (var room in _rooms)
        {
            int minCol = (room.Bounds.x - dungeonBounds.x) / cellSize;
            int maxCol = (room.Bounds.x + room.Bounds.width - dungeonBounds.x) / cellSize - 1;
            int minRow = (room.Bounds.y - dungeonBounds.y) / cellSize;
            int maxRow = (room.Bounds.y + room.Bounds.height - dungeonBounds.y) / cellSize - 1;

            for (int col = minCol; col <= maxCol; col++)
            {
                tilemap[minRow, col] = 1;
                tilemap[maxRow, col] = 1;
            }
            for (int row = minRow; row <= maxRow; row++)
            {
                tilemap[row, minCol] = 1;
                tilemap[row, maxCol] = 1;
            }
        }

        foreach (var door in _doors)
        {
            int col = (door.Bounds.x - dungeonBounds.x) / cellSize;
            int row = (door.Bounds.y - dungeonBounds.y) / cellSize;
            if (row >= 0 && row < rows && col >= 0 && col < cols)
                tilemap[row, col] = 2;
        }

        return tilemap;
    }

    // --- Spawning ---
    private void SpawnWalls(int[,] tilemap)
    {
        int rows = tilemap.GetLength(0);
        int cols = tilemap.GetLength(1);

        GameObject wallParent = new GameObject("Walls");
        wallParent.transform.SetParent(_dungeonRoot.transform);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                if (tilemap[row, col] == 0) continue;

                bool hasLeft  = col > 0        && tilemap[row, col - 1] >= 1;
                bool hasRight = col < cols - 1  && tilemap[row, col + 1] >= 1;
                bool hasDown  = row > 0        && tilemap[row - 1, col] >= 1;
                bool hasUp    = row < rows - 1  && tilemap[row + 1, col] >= 1;

                Vector3 pos = CellToWorld(row, col);

                if (tilemap[row, col] == 2)
                {
                    if (doorPrefab == null) continue;
                    Quaternion rot = (hasLeft || hasRight) ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
                    Instantiate(doorPrefab, pos, rot, wallParent.transform);
                }
                else
                {
                    bool isCorner = (hasLeft || hasRight) && (hasDown || hasUp);

                    if (isCorner)
                    {
                        if (columnPrefab != null)
                            Instantiate(columnPrefab, pos, Quaternion.identity, wallParent.transform);
                    }
                    else
                    {
                        if (wallPrefab == null) continue;
                        Quaternion rot = (hasLeft || hasRight) ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
                        Instantiate(wallPrefab, pos, rot, wallParent.transform);
                    }
                }
            }
        }
    }

    private void SpawnFloor(int[,] tilemap)
    {
        if (floorPrefab == null) return;

        int rows = tilemap.GetLength(0);
        int cols = tilemap.GetLength(1);

        Room startRoom = _rooms[0];
        int startCol = Mathf.FloorToInt((startRoom.Center.x - dungeonBounds.x) / cellSize);
        int startRow = Mathf.FloorToInt((startRoom.Center.y - dungeonBounds.y) / cellSize);

        if (tilemap[startRow, startCol] == 1) return;

        GameObject floorParent = new GameObject("Floor");
        floorParent.transform.SetParent(_dungeonRoot.transform);

        var visited = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        var startPos = new Vector2Int(startCol, startRow);
        visited.Add(startPos);
        queue.Enqueue(startPos);

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            var tile = Instantiate(floorPrefab, CellToWorld(current.y, current.x),
                Quaternion.identity, floorParent.transform);
            tile.name = $"Floor_{current.x}_{current.y}";

            // BoxCollider lets the NavMesh baker sample floor geometry
            // without needing CPU read access on the mesh asset
            var col = tile.AddComponent<BoxCollider>();
            col.size   = new Vector3(cellSize, 0.1f, cellSize);
            col.center = Vector3.zero;

            foreach (var dir in dirs)
            {
                var next = current + dir;
                if (next.x < 0 || next.x >= cols || next.y < 0 || next.y >= rows) continue;
                if (visited.Contains(next)) continue;
                if (tilemap[next.y, next.x] == 1) continue;
                visited.Add(next);
                queue.Enqueue(next);
            }
        }
    }

    private Vector3 CellToWorld(int row, int col) => new Vector3(
        dungeonBounds.x + col * cellSize + cellSize * 0.5f,
        0,
        dungeonBounds.y + row * cellSize + cellSize * 0.5f
    );

    // --- Graph helpers ---
    private List<(Room, Room, RectInt)> FindAdjacentPairs()
    {
        var candidates = new List<(Room, Room, RectInt)>();

        for (int i = 0; i < _rooms.Count; i++)
        {
            for (int j = i + 1; j < _rooms.Count; j++)
            {
                Room rA = _rooms[i];
                Room rB = _rooms[j];
                RectInt wall = AlgorithmsUtils.Intersect(rA.Bounds, rB.Bounds);

                bool verticalWall   = wall.width  == cellSize && wall.height >= cellSize * 5;
                bool horizontalWall = wall.height == cellSize && wall.width  >= cellSize * 5;

                if (!verticalWall && !horizontalWall) continue;

                RectInt doorBounds;
                if (verticalWall)
                {
                    int doorY = SnapToGrid(Random.Range(wall.y + cellSize * 2, wall.y + wall.height - cellSize * 2));
                    doorBounds = new RectInt(wall.x, doorY, cellSize, cellSize);
                }
                else
                {
                    int doorX = SnapToGrid(Random.Range(wall.x + cellSize * 2, wall.x + wall.width - cellSize * 2));
                    doorBounds = new RectInt(doorX, wall.y, cellSize, cellSize);
                }

                candidates.Add((rA, rB, doorBounds));
            }
        }

        return candidates;
    }

    private bool IsFullyConnected()
    {
        if (_rooms.Count == 0) return true;

        var visited = new HashSet<Room>();
        var stack = new Stack<Room>();
        stack.Push(_rooms[0]);
        visited.Add(_rooms[0]);

        while (stack.Count > 0)
        {
            Room current = stack.Pop();
            foreach (var door in current.Doors)
            {
                Room neighbour = door.GetOtherRoom(current);
                if (_rooms.Contains(neighbour) && !visited.Contains(neighbour))
                {
                    visited.Add(neighbour);
                    stack.Push(neighbour);
                }
            }
        }

        return visited.Count == _rooms.Count;
    }

    private int Find(int[] parent, int x) =>
        parent[x] == x ? x : parent[x] = Find(parent, parent[x]);

    private bool Union(int[] parent, int x, int y)
    {
        int px = Find(parent, x), py = Find(parent, y);
        if (px == py) return false;
        parent[px] = py;
        return true;
    }

    // --- BSP ---
    private List<Room> Split(RectInt bounds, int depth)
    {
        bool tooSmall = bounds.width  < minRoomSize * 2 + cellSize ||
                        bounds.height < minRoomSize * 2 + cellSize;

        if (depth >= maxDepth || tooSmall)
        {
            Room leaf = new Room(bounds);
            _rooms.Add(leaf);
            return new List<Room> { leaf };
        }

        bool splitVertically = bounds.width > bounds.height || (bounds.height <= bounds.width && Random.value > 0.5f);

        RectInt boundsA, boundsB;

        if (splitVertically)
        {
            int splitX = SnapToGrid(Random.Range(bounds.x + minRoomSize, bounds.x + bounds.width - minRoomSize));
            boundsA = new RectInt(bounds.x, bounds.y, splitX - bounds.x + cellSize, bounds.height);
            boundsB = new RectInt(splitX, bounds.y, bounds.x + bounds.width - splitX, bounds.height);
        }
        else
        {
            int splitY = SnapToGrid(Random.Range(bounds.y + minRoomSize, bounds.y + bounds.height - minRoomSize));
            boundsA = new RectInt(bounds.x, bounds.y, bounds.width, splitY - bounds.y + cellSize);
            boundsB = new RectInt(bounds.x, splitY, bounds.width, bounds.y + bounds.height - splitY);
        }

        var all = new List<Room>(Split(boundsA, depth + 1));
        all.AddRange(Split(boundsB, depth + 1));
        return all;
    }

    private int SnapToGrid(int value) => Mathf.RoundToInt((float)value / cellSize) * cellSize;

    // --- Debug ---
    private void RefreshDebug()
    {
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();
        DebugDrawingBatcher.GetInstance().BatchCall(() =>
        {
            foreach (var room in _displayRooms)
            {
                DebugExtension.DebugBounds(
                    new Bounds(
                        new Vector3(room.Center.x, 0, room.Center.y),
                        new Vector3(room.Bounds.width, 0.05f, room.Bounds.height)
                    ), Color.purple);
            }

            foreach (var (rA, rB, doorBounds) in _displayCandidates)
            {
                DebugExtension.DebugBounds(
                    new Bounds(
                        new Vector3(doorBounds.center.x, 0, doorBounds.center.y),
                        new Vector3(doorBounds.width, 0.05f, doorBounds.height)
                    ), Color.white);
            }

            foreach (var door in _displayDoors)
            {
                DebugExtension.DebugBounds(
                    new Bounds(
                        new Vector3(door.Bounds.center.x, 0, door.Bounds.center.y),
                        new Vector3(door.Bounds.width, 0.05f, door.Bounds.height)
                    ), Color.hotPink);

                Gizmos.color = Color.red;
                Gizmos.DrawLine(
                    new Vector3(door.RoomA.Center.x, 0.05f, door.RoomA.Center.y),
                    new Vector3(door.RoomB.Center.x, 0.05f, door.RoomB.Center.y));
            }
        });
    }

    // --- Public accessors ---
    public List<Room> GetRooms() => _rooms;
    public List<Door> GetDoors() => _doors;
    public RectInt GetDungeonBounds() => dungeonBounds;
}