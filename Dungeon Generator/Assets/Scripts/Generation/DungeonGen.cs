using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NaughtyAttributes;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Generation
{
    public class DungeonGenerator : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private RectInt dungeonBounds = new(0, 0, 100, 100);
        [SerializeField] private int minRoomSize = 12;
        [SerializeField] private int maxDepth = 5;
        [SerializeField, Range(0, 100)] private int deletedRoomsPercentage = 10;

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
        [SerializeField] private int seed;
        [SerializeField] private bool useRandomSeed = true;
        [SerializeField] private float stepDelay = 0.5f;
        [SerializeField] private float floorStepDelay = 0.05f;

        [Header("Settings")]
        [SerializeField] private bool generateOnStart = true;

        private readonly List<Room> _rooms = new();
        private readonly List<Door> _doors = new();
        private GameObject _dungeonRoot;

        private readonly List<Room> _displayRooms = new();
        private readonly List<Door> _displayDoors = new();
        private readonly List<(Room roomA, Room roomB, RectInt doorBounds)> _displayCandidates = new();

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
        private void GenerateDungeon()
        {
            StopAllCoroutines();
            StartCoroutine(GenerateDungeonCoroutine());
        }
        
        //Dungeon Gen Coroutine
        //Pretty self-explanatory, the coroutine starts by clearing the previous dungeon, checks and creates a root obj
        //and then fires all other methods one after another to generate a new dungeon.
        private IEnumerator GenerateDungeonCoroutine()
        {
            _rooms.Clear();
            _doors.Clear();
            _displayRooms.Clear();
            _displayDoors.Clear();
            _displayCandidates.Clear();
            DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();

            if (_dungeonRoot) DestroyImmediate(_dungeonRoot);
            _dungeonRoot = new GameObject("-- Dungeon Layout --");
            
            //I dont like warnings
            var agent = player ? player.GetComponent<NavMeshAgent>() : null;
            if (agent) agent.enabled = false;

            if (useRandomSeed) seed = Random.Range(0, int.MaxValue);
            Random.InitState(seed);

            yield return StartCoroutine(GenerateRooms());

            var candidates = FindAdjacentPairs();
            yield return StartCoroutine(ShowCandidates(candidates));
            yield return StartCoroutine(BuildSpanningTree(candidates));

            Debug.Log($"Connectivity: {(IsFullyConnected() ? "pass" : "fail")}");

            yield return StartCoroutine(RemoveRooms());

            var tilemap = BuildTilemap();
            yield return StartCoroutine(SpawnWalls(tilemap));
            yield return StartCoroutine(SpawnFloor(tilemap));

            BakeNavMesh();
            
            if (agent) agent.enabled = true;
            PlacePlayer();

            RefreshDebug();

            Debug.Log($"Genned {_rooms.Count} rooms | {_doors.Count} doors | Seed: {seed}");
        }

        //Generation steps
        private IEnumerator GenerateRooms()
        {
            Split(dungeonBounds, 0);
            foreach (var room in _rooms)
            {
                _displayRooms.Add(room);
                RefreshDebug();
                yield return new WaitForSeconds(stepDelay);
            }
        }
        
        private IEnumerator ShowCandidates(List<(Room, Room, RectInt)> candidates)
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
        
        //RandomSpanningTree
        //Creates random spanning tree using Union-Find. And what Union-Find does is it shuffles the candidate list,
        //checks if each room pair is already connected and if not, it merges them and creates a door. This ensures
        //that every room is fully reachable.
        private IEnumerator BuildSpanningTree(List<(Room roomA, Room roomB, RectInt doorBounds)> candidates)
        {
            for (var i = candidates.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            var roomIndex = new Dictionary<Room, int>();
            for (var i = 0; i < _rooms.Count; i++) roomIndex[_rooms[i]] = i;

            var parent = new int[_rooms.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;

            foreach (var (roomA, roomB, doorBounds) in candidates)
            {
                if (!Union(parent, roomIndex[roomA], roomIndex[roomB])) continue;
                var door = new Door(roomA, roomB, doorBounds, doorWidth, doorHeight);
                roomA.Doors.Add(door);
                roomB.Doors.Add(door);
                _doors.Add(door);
                _displayDoors.Add(door);
                RefreshDebug();
                yield return new WaitForSeconds(stepDelay);
            }
        }
        //RemoveRooms
        //Used to delete a portion of the smallest rooms. I even exposed the deletion percentage for fun and to check
        //if my function runs without errors.
        //Essentially it stars by sorting all rooms ascending by size. Then it tries to remove the first (smallest)
        //room and checks by using Depth First Search if AFTER the deletion, all rooms in the dungeon are reachable.
        private IEnumerator RemoveRooms()
        {
            var removeCount = Mathf.CeilToInt(_rooms.Count * (deletedRoomsPercentage / 100f));
            var sorted = new List<Room>(_rooms);
            sorted.Sort((a, b) => (a.Bounds.width * a.Bounds.height).CompareTo(b.Bounds.width * b.Bounds.height));

            var removed = 0;
            foreach (var room in sorted)
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

        //NavMesh
        [Button]
        private void BakeNavMesh()
        {
            if (!navMeshSurface) return;
            navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            navMeshSurface.BuildNavMesh();
        }
        
        //PlayerSpawn
        //Tries to spawn the player in a random room location, if it fails it puts the player in the room center
        private void PlacePlayer()
        {
            if (!player || _rooms.Count == 0) return;

            var agent = player.GetComponent<NavMeshAgent>();

            var spawnRoom = _rooms[Random.Range(0, _rooms.Count)];
            var roomCenter = new Vector3(spawnRoom.Center.x, 0, spawnRoom.Center.y);

            if (NavMesh.SamplePosition(roomCenter, out var hit, cellSize * 2f, NavMesh.AllAreas))
            {
                if (agent)
                    agent.Warp(hit.position);
                else
                    player.position = hit.position;
            }
            else
            {
                player.position = roomCenter;
            }
        }

        //Tilemap
        //Converts the nice looking room/door graph into a 2D integer grid, where 0 = empty, 1 = wall and 2 = door.
        //Walls are put at the perimeter of the room, leaving the middle empty. Doors overwrite any valid wall cell.
        private int[,] BuildTilemap()
        {
            var rows = dungeonBounds.height / cellSize;
            var cols = dungeonBounds.width / cellSize;
            var tilemap = new int[rows, cols];

            foreach (var room in _rooms)
            {
                var minCol = (room.Bounds.x - dungeonBounds.x) / cellSize;
                var maxCol = (room.Bounds.x + room.Bounds.width - dungeonBounds.x) / cellSize - 1;
                var minRow = (room.Bounds.y - dungeonBounds.y) / cellSize;
                var maxRow = (room.Bounds.y + room.Bounds.height - dungeonBounds.y) / cellSize - 1;

                for (var col = minCol; col <= maxCol; col++)
                {
                    tilemap[minRow, col] = 1;
                    tilemap[maxRow, col] = 1;
                }
                for (var row = minRow; row <= maxRow; row++)
                {
                    tilemap[row, minCol] = 1;
                    tilemap[row, maxCol] = 1;
                }
            }

            foreach (var door in _doors)
            {
                var col = (door.Bounds.x - dungeonBounds.x) / cellSize;
                var row = (door.Bounds.y - dungeonBounds.y) / cellSize;
                if (row >= 0 && row < rows && col >= 0 && col < cols)
                    tilemap[row, col] = 2;
            }

            return tilemap;
        }

        //Spawning
        //All spawning methods work the same, so here is what they do.
        //The methods iterate every non-zero(0) cell and checks their neighbours on the following logic:
        //If a cell has neighbours on its PERPENDICULAR axis -> corner
        //If not -> wall
        //If cell is 2 -> door
        //Floors use Breadth First Search from the center of the first room, and from there goes trough all 0 cells and
        //puts floors on them.
        private IEnumerator SpawnWalls(int[,] tilemap)
        {
            var rows = tilemap.GetLength(0);
            var cols = tilemap.GetLength(1);

            var wallParent = new GameObject("Walls");
            wallParent.transform.SetParent(_dungeonRoot.transform);

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    if (tilemap[row, col] == 0) continue;

                    var hasLeft = col > 0 && tilemap[row, col - 1] >= 1;
                    var hasRight = col < cols - 1 && tilemap[row, col + 1] >= 1;
                    var hasDown = row > 0 && tilemap[row - 1, col] >= 1;
                    var hasUp = row < rows - 1 && tilemap[row + 1, col] >= 1;

                    var pos = CellToWorld(row, col);

                    if (tilemap[row, col] == 2)
                    {
                        if (!doorPrefab) continue;
                        var rot = (hasLeft || hasRight) ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
                        Instantiate(doorPrefab, pos, rot, wallParent.transform);
                    }
                    else
                    {
                        var isCorner = (hasLeft || hasRight) && (hasDown || hasUp);
                        if (isCorner)
                        {
                            if (columnPrefab)
                                Instantiate(columnPrefab, pos, Quaternion.identity, wallParent.transform);
                        }
                        else
                        {
                            if (!wallPrefab) continue;
                            var rot = (hasLeft || hasRight) ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
                            Instantiate(wallPrefab, pos, rot, wallParent.transform);
                        }
                    }

                    RefreshDebug();
                    yield return new WaitForSeconds(stepDelay * 0.05f);
                }
            }
        }

        private IEnumerator SpawnFloor(int[,] tilemap)
        {
            if (!floorPrefab) yield break;

            var rows = tilemap.GetLength(0);
            var cols = tilemap.GetLength(1);

            var startRoom = _rooms[0];
            var startCol = Mathf.FloorToInt((startRoom.Center.x - dungeonBounds.x) / cellSize);
            var startRow = Mathf.FloorToInt((startRoom.Center.y - dungeonBounds.y) / cellSize);

            if (tilemap[startRow, startCol] == 1) yield break;

            var floorParent = new GameObject("Floors");
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

                var boxCol = tile.AddComponent<BoxCollider>();
                boxCol.size = new Vector3(cellSize, 0.1f, cellSize);
                boxCol.center = Vector3.zero;

                foreach (var dir in dirs)
                {
                    var next = current + dir;
                    if (next.x < 0 || next.x >= cols || next.y < 0 || next.y >= rows) continue;
                    if (visited.Contains(next)) continue;
                    if (tilemap[next.y, next.x] == 1) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }

                RefreshDebug();
                yield return new WaitForSeconds(floorStepDelay);
            }
        }

        private Vector3 CellToWorld(int row, int col) => new(
            dungeonBounds.x + col * cellSize + cellSize * 0.5f,
            0,
            dungeonBounds.y + row * cellSize + cellSize * 0.5f
        );

        //Graph helpers
        private List<(Room, Room, RectInt)> FindAdjacentPairs()
        {
            var candidates = new List<(Room, Room, RectInt)>();

            for (var i = 0; i < _rooms.Count; i++)
            {
                for (var j = i + 1; j < _rooms.Count; j++)
                {
                    var rA = _rooms[i];
                    var rB = _rooms[j];
                    var wall = AlgorithmsUtils.Intersect(rA.Bounds, rB.Bounds);

                    var verticalWall   = wall.width  == cellSize && wall.height >= cellSize * 5;
                    var horizontalWall = wall.height == cellSize && wall.width  >= cellSize * 5;

                    if (!verticalWall && !horizontalWall) continue;

                    RectInt doorBounds;
                    if (verticalWall)
                    {
                        var doorY = SnapToGrid(Random.Range(wall.y + cellSize * 2, wall.y + wall.height - cellSize * 2));
                        doorBounds = new RectInt(wall.x, doorY, cellSize, cellSize);
                    }
                    else
                    {
                        var doorX = SnapToGrid(Random.Range(wall.x + cellSize * 2, wall.x + wall.width - cellSize * 2));
                        doorBounds = new RectInt(doorX, wall.y, cellSize, cellSize);
                    }
                    candidates.Add((rA, rB, doorBounds));
                }
            }
            return candidates;
        }
        
        //Depth First Search check.
        //Works by checking door connections.
        private bool IsFullyConnected()
        {
            if (_rooms.Count == 0) return true;

            var visited = new HashSet<Room>();
            var stack = new Stack<Room>();
            stack.Push(_rooms[0]);
            visited.Add(_rooms[0]);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var neighbour in current.Doors.Select(door => door.GetOtherRoom(current))
                             .Where(neighbour => _rooms.Contains(neighbour) && !visited.Contains(neighbour)))
                {
                    visited.Add(neighbour);
                    stack.Push(neighbour);
                }
            }

            return visited.Count == _rooms.Count;
        }
        
        //Find function
        //Recursive function to find the root (parent) of the object.
        private static int Find(int[] parent, int x) =>
            parent[x] == x ? x : parent[x] = Find(parent, parent[x]);
        
        //Union function
        //Uses Find to find the roots of two rooms. If the rooms are already in the same room, then it returns false.
        private static bool Union(int[] parent, int x, int y)
        {
            int px = Find(parent, x), py = Find(parent, y);
            if (px == py) return false;
            parent[px] = py;
            return true;
        }

        // (BSP) Binary Space Partitioning
        // Essentially, what it does is it is used to split big rooms into smaller ones; maxDepth defines the amount of
        // "cuts" a room can undergo, so lower mD == bigger rooms and higher mD == smaller rooms.
        private List<Room> Split(RectInt bounds, int depth)
        {
            var tooSmall = bounds.width  < minRoomSize * 2 + cellSize ||
                           bounds.height < minRoomSize * 2 + cellSize;

            if (depth >= maxDepth || tooSmall)
            {
                var leaf = new Room(bounds);
                _rooms.Add(leaf);
                return new List<Room> { leaf };
            }

            var splitVertically = bounds.width > bounds.height || (bounds.height <= bounds.width && Random.value > 0.5f);

            RectInt boundsA, boundsB;

            if (splitVertically)
            {
                var splitX = SnapToGrid(Random.Range(bounds.x + minRoomSize, bounds.x + bounds.width - minRoomSize));
                boundsA = new RectInt(bounds.x, bounds.y, splitX - bounds.x + cellSize, bounds.height);
                boundsB = new RectInt(splitX, bounds.y, bounds.x + bounds.width - splitX, bounds.height);
            }
            else
            {
                var splitY = SnapToGrid(Random.Range(bounds.y + minRoomSize, bounds.y + bounds.height - minRoomSize));
                boundsA = new RectInt(bounds.x, bounds.y, bounds.width, splitY - bounds.y + cellSize);
                boundsB = new RectInt(bounds.x, splitY, bounds.width, bounds.y + bounds.height - splitY);
            }

            var all = new List<Room>(Split(boundsA, depth + 1));
            all.AddRange(Split(boundsB, depth + 1));
            return all;
        }
        
        //Makes sure everything is aligned by rounding any integer to the nearest multiple of cellSize.
        private int SnapToGrid(int value) => Mathf.RoundToInt((float)value / cellSize) * cellSize;

        //Debug view
        //Colours that show during generation.
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

        //Public accessors
        public List<Room> GetRooms() => _rooms;
        public List<Door> GetDoors() => _doors;
        public RectInt GetDungeonBounds() => dungeonBounds;
    }
}