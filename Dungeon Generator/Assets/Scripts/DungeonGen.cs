using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

public class DungeonGenerator : MonoBehaviour
{
    [SerializeField] private RectInt dungeonBounds = new(0, 0, 50, 50);
    [SerializeField] private int minRoomSize = 8;
    [SerializeField] private int maxDepth = 4;
    [SerializeField] private bool generateOnStart = true;

    private readonly List<Room> _rooms = new();
    private readonly List<Door> _doors = new();

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

        Debug.Log($"Dungeon generated — {_rooms.Count} rooms, {_doors.Count} doors.");
    }
    
    // BSP
    private List<Room> Split(RectInt bounds, int depth)
    {
        var tooSmall = bounds.width  < minRoomSize * 2 + 1 ||
                       bounds.height < minRoomSize * 2 + 1;

        // Create a room and return it. Leaf
        if (depth >= maxDepth || tooSmall)
        {
            var leaf = new Room(bounds);
            _rooms.Add(leaf);
            return new List<Room> { leaf };
        }

        // Split along the longer axis and randomise when square
        var splitVertically = bounds.width > bounds.height || bounds.height <= bounds.width && Random.value > 0.5f;

        RectInt boundsA, boundsB;

        if (splitVertically)
        {
            var splitX = Random.Range(bounds.x + minRoomSize,
                                      bounds.x + bounds.width - minRoomSize);

            // +1 width so both rooms share the wall column at splitX
            boundsA = new RectInt(bounds.x,  bounds.y, splitX - bounds.x + 1, bounds.height);
            boundsB = new RectInt(splitX,    bounds.y, bounds.x + bounds.width - splitX, bounds.height);
        }
        else
        {
            var splitY = Random.Range(bounds.y + minRoomSize,
                                      bounds.y + bounds.height - minRoomSize);

            boundsA = new RectInt(bounds.x, bounds.y,  bounds.width, splitY - bounds.y + 1);
            boundsB = new RectInt(bounds.x, splitY,    bounds.width, bounds.y + bounds.height - splitY);
        }

        // Lists
        var roomsA = Split(boundsA, depth + 1);
        var roomsB = Split(boundsB, depth + 1);
        
        // Find a room from each side that share a wall long enough for a door
        Room connectedA = null;
        Room connectedB = null;

        foreach (var rA in roomsA)
        {
            foreach (var rB in roomsB)
            {
                var wall = AlgorithmsUtils.Intersect(rA.Bounds, rB.Bounds);
                var longEnough = splitVertically ? wall.height >= 3
                                                  : wall.width  >= 3;
                if (!longEnough) continue;
                connectedA = rA;
                connectedB = rB;
                break;
            }
            if (connectedA != null) break;
        }

        // Place a door on the shared wall
        if (connectedA != null)
        {
            var wall = AlgorithmsUtils.Intersect(connectedA.Bounds, connectedB.Bounds);
            RectInt doorBounds;

            if (splitVertically)
            {
                var doorY = Random.Range(wall.y + 1, wall.y + wall.height - 1);
                doorBounds = new RectInt(wall.x, doorY, 1, 1);
            }
            else
            {
                var doorX = Random.Range(wall.x + 1, wall.x + wall.width - 1);
                doorBounds = new RectInt(doorX, wall.y, 1, 1);
            }

            var door = new Door(connectedA, connectedB, doorBounds);
            connectedA.Doors.Add(door);
            connectedB.Doors.Add(door);
            _doors.Add(door);
        }

        var all = new List<Room>(roomsA);
        all.AddRange(roomsB);
        return all;
    }

    // Debug visualisation
    private void DrawDebug()
    {
        DebugDrawingBatcher.GetInstance().BatchCall(() =>
        {
            // Rooms
            foreach (var room in _rooms)
                AlgorithmsUtils.DebugRectInt(room.Bounds, Color.purple);

            // Doors + graph edges between room centres
            foreach (var door in _doors)
            {
                AlgorithmsUtils.DebugRectInt(door.Bounds, Color.hotPink);

                Vector3 centreA = new Vector3(door.RoomA.Center.x, 0.05f, door.RoomA.Center.y);
                Vector3 centreB = new Vector3(door.RoomB.Center.x, 0.05f, door.RoomB.Center.y);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(centreA, centreB);
            }
        });
    }
    
    // Public accessors
    public List<Room> GetRooms() => _rooms;
    public List<Door> GetDoors() => _doors;
    public RectInt GetDungeonBounds() => dungeonBounds;
}