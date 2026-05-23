using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private RectInt dungeonBounds = new RectInt(0, 0, 100, 100);
    [SerializeField] private int minRoomSize = 16;
    [SerializeField] private int maxDepth = 4;

    [Header("Scale")]
    [SerializeField] private int cellSize = 4;
    [SerializeField] private int wallHeight = 4;
    [SerializeField] private int doorWidth = 4;
    [SerializeField] private int doorHeight = 4;

    [Header("Settings")]
    [SerializeField] private bool generateOnStart = true;

    private List<Room> _rooms = new List<Room>();
    private List<Door> _doors = new List<Door>();

    public int CellSize => cellSize;
    public int WallHeight => wallHeight;
    public int DoorWidth => doorWidth;
    public int DoorHeight => doorHeight;

    private void Start()
    {
        if (generateOnStart) GenerateDungeon();
    }

    [Button]
    public void GenerateDungeon()
    {
        _rooms.Clear();
        _doors.Clear();
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();

        Split(dungeonBounds, 0);

        DrawDebug();

        Debug.Log($"Dungeon — {_rooms.Count} rooms | {_doors.Count} doors");
    }

    private List<Room> Split(RectInt bounds, int depth)
    {
        bool tooSmall = bounds.width < minRoomSize * 2 + cellSize ||
                        bounds.height < minRoomSize * 2 + cellSize;

        if (depth >= maxDepth || tooSmall)
        {
            Room leaf = new Room(bounds);
            _rooms.Add(leaf);
            return new List<Room> { leaf };
        }

        bool splitVertically = bounds.width > bounds.height ? true
                             : bounds.height > bounds.width ? false
                             : Random.value > 0.5f;

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

        List<Room> roomsA = Split(boundsA, depth + 1);
        List<Room> roomsB = Split(boundsB, depth + 1);

        Room connectedA = null, connectedB = null;

        foreach (Room rA in roomsA)
        {
            foreach (Room rB in roomsB)
            {
                RectInt wall = AlgorithmsUtils.Intersect(rA.Bounds, rB.Bounds);
                bool longEnough = splitVertically ? wall.height >= cellSize * 3 : wall.width >= cellSize * 3;
                if (longEnough) { connectedA = rA; connectedB = rB; break; }
            }
            if (connectedA != null) break;
        }

        if (connectedA != null && connectedB != null)
        {
            RectInt wall = AlgorithmsUtils.Intersect(connectedA.Bounds, connectedB.Bounds);
            RectInt doorBounds;

            if (splitVertically)
            {
                int doorY = SnapToGrid(Random.Range(wall.y + cellSize, wall.y + wall.height - cellSize));
                doorBounds = new RectInt(wall.x, doorY, cellSize, cellSize);
            }
            else
            {
                int doorX = SnapToGrid(Random.Range(wall.x + cellSize, wall.x + wall.width - cellSize));
                doorBounds = new RectInt(doorX, wall.y, cellSize, cellSize);
            }

            Door door = new Door(connectedA, connectedB, doorBounds,
                ConnectionType.Door, doorWidth, doorHeight);
            connectedA.Doors.Add(door);
            connectedB.Doors.Add(door);
            _doors.Add(door);
        }

        var all = new List<Room>(roomsA);
        all.AddRange(roomsB);
        return all;
    }

    private int SnapToGrid(int value) => Mathf.RoundToInt((float)value / cellSize) * cellSize;

    private void DrawDebug()
    {
        DebugDrawingBatcher.GetInstance().BatchCall(() =>
        {
            foreach (var room in _rooms)
            {
                DebugExtension.DebugBounds(
                    new Bounds(
                        new Vector3(room.Center.x, 0, room.Center.y),
                        new Vector3(room.Bounds.width, 0.05f, room.Bounds.height)
                    ), Color.green);
            }

            foreach (var door in _doors)
            {
                DebugExtension.DebugBounds(
                    new Bounds(
                        new Vector3(door.Bounds.center.x, 0, door.Bounds.center.y),
                        new Vector3(door.Bounds.width, 0.05f, door.Bounds.height)
                    ), Color.cyan);

                Vector3 centreA = new Vector3(door.RoomA.Center.x, 0.05f, door.RoomA.Center.y);
                Vector3 centreB = new Vector3(door.RoomB.Center.x, 0.05f, door.RoomB.Center.y);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(centreA, centreB);
            }
        });
    }

    public List<Room> GetRooms() => _rooms;
    public List<Door> GetDoors() => _doors;
    public RectInt GetDungeonBounds() => dungeonBounds;
}