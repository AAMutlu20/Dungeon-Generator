using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private RectInt dungeonBounds = new(0, 0, 50, 50);
    [SerializeField] private RectInt secondFloorBounds = new(0, 0, 50, 50);
    [SerializeField] private int minRoomSize = 8;
    [SerializeField] private int maxDepth = 4;

    [Header("Height")]
    [SerializeField] private int wallHeight = 3;
    [SerializeField] private int floorHeight = 4;
    [SerializeField] private int doorWidth = 1;
    [SerializeField] private int doorHeight = 2;

    [Header("Settings")]
    [SerializeField] private bool generateOnStart = true;

    private List<Room> _rooms = new();
    private List<Door> _doors = new();

    public int WallHeight => wallHeight;
    public int FloorHeight => floorHeight;
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

        Split(dungeonBounds, 0, floor: 0);
        Split(secondFloorBounds, 0, floor: 1);

        List<Room> floor0Rooms = GetRoomsOnFloor(0);
        List<Room> floor1Rooms = GetRoomsOnFloor(1);

        if (floor0Rooms.Count > 0 && floor1Rooms.Count > 0)
        {
            Room stairBottom = floor0Rooms[Random.Range(0, floor0Rooms.Count)];
            Room stairTop = floor1Rooms[Random.Range(0, floor1Rooms.Count)];

            Vector2Int centre = new Vector2Int(
                Mathf.RoundToInt(stairBottom.Center.x),
                Mathf.RoundToInt(stairBottom.Center.y)
            );
            RectInt staircaseBounds = new RectInt(centre.x, centre.y, 2, 2);

            Door staircase = new Door(stairBottom, stairTop, staircaseBounds,
                ConnectionType.Staircase, doorWidth, doorHeight);
            stairBottom.Doors.Add(staircase);
            stairTop.Doors.Add(staircase);
            _doors.Add(staircase);
        }

        DrawDebug();

        Debug.Log($"Dungeon — Floor 0: {floor0Rooms.Count} rooms | " +
                  $"Floor 1: {GetRoomsOnFloor(1).Count} rooms | " +
                  $"Doors: {_doors.FindAll(d => !d.IsStaircase).Count} | " +
                  $"Staircases: {_doors.FindAll(d => d.IsStaircase).Count}");
    }

    private List<Room> Split(RectInt bounds, int depth, int floor)
    {
        bool tooSmall = bounds.width < minRoomSize * 2 + 1 ||
                        bounds.height < minRoomSize * 2 + 1;

        if (depth >= maxDepth || tooSmall)
        {
            Room leaf = new Room(bounds, floor);
            _rooms.Add(leaf);
            return new List<Room> { leaf };
        }

        bool splitVertically = bounds.width > bounds.height || (bounds.height <= bounds.width && Random.value > 0.5f);

        RectInt boundsA, boundsB;

        if (splitVertically)
        {
            int splitX = Random.Range(bounds.x + minRoomSize, bounds.x + bounds.width - minRoomSize);
            boundsA = new RectInt(bounds.x, bounds.y, splitX - bounds.x + 1, bounds.height);
            boundsB = new RectInt(splitX, bounds.y, bounds.x + bounds.width - splitX, bounds.height);
        }
        else
        {
            int splitY = Random.Range(bounds.y + minRoomSize, bounds.y + bounds.height - minRoomSize);
            boundsA = new RectInt(bounds.x, bounds.y, bounds.width, splitY - bounds.y + 1);
            boundsB = new RectInt(bounds.x, splitY, bounds.width, bounds.y + bounds.height - splitY);
        }

        List<Room> roomsA = Split(boundsA, depth + 1, floor);
        List<Room> roomsB = Split(boundsB, depth + 1, floor);

        Room connectedA = null, connectedB = null;

        foreach (Room rA in roomsA)
        {
            foreach (Room rB in roomsB)
            {
                RectInt wall = AlgorithmsUtils.Intersect(rA.Bounds, rB.Bounds);
                bool longEnough = splitVertically ? wall.height >= 3 : wall.width >= 3;
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
                int doorY = Random.Range(wall.y + 1, wall.y + wall.height - 1);
                doorBounds = new RectInt(wall.x, doorY, 1, 1);
            }
            else
            {
                int doorX = Random.Range(wall.x + 1, wall.x + wall.width - 1);
                doorBounds = new RectInt(doorX, wall.y, 1, 1);
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

    private void DrawDebug()
    {
        DebugDrawingBatcher.GetInstance().BatchCall(() =>
        {
            foreach (var room in _rooms)
            {
                float worldY = room.Floor * floorHeight;
                Color color = room.Floor == 0 ? Color.green : Color.blue;
                DebugExtension.DebugBounds(
                    new Bounds(
                        new Vector3(room.Center.x, worldY, room.Center.y),
                        new Vector3(room.Bounds.width, 0.05f, room.Bounds.height)
                    ), color);
            }

            foreach (var door in _doors)
            {
                if (door.IsStaircase)
                {
                    Vector3 bottom = new Vector3(door.RoomA.Center.x, door.RoomA.Floor * floorHeight, door.RoomA.Center.y);
                    Vector3 top = new Vector3(door.RoomB.Center.x, door.RoomB.Floor * floorHeight, door.RoomB.Center.y);
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawLine(bottom, top);
                    Gizmos.DrawWireSphere(bottom, 0.5f);
                    Gizmos.DrawWireSphere(top, 0.5f);
                }
                else
                {
                    float worldY = door.RoomA.Floor * floorHeight;
                    DebugExtension.DebugBounds(
                        new Bounds(
                            new Vector3(door.Bounds.center.x, worldY, door.Bounds.center.y),
                            new Vector3(door.Bounds.width, 0.05f, door.Bounds.height)
                        ), Color.cyan);

                    Vector3 centreA = new Vector3(door.RoomA.Center.x, worldY + 0.05f, door.RoomA.Center.y);
                    Vector3 centreB = new Vector3(door.RoomB.Center.x, worldY + 0.05f, door.RoomB.Center.y);
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(centreA, centreB);
                }
            }
        });
    }

    public List<Room> GetRooms() => _rooms;
    public List<Door> GetDoors() => _doors;
    public RectInt GetDungeonBounds() => dungeonBounds;
    public List<Room> GetRoomsOnFloor(int floor) => _rooms.FindAll(r => r.Floor == floor);
}