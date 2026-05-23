using UnityEngine;

public enum ConnectionType { Door, Staircase }

public class Door
{
    public Room RoomA  { get; }
    public Room RoomB  { get; }
    public RectInt Bounds { get; }
    public ConnectionType Type { get; }
    public int  Width  { get; } // world-unit width
    public int Height { get; } // world-unit height

    public bool IsStaircase => Type == ConnectionType.Staircase;

    public Door(Room roomA, Room roomB, RectInt bounds,
        ConnectionType type = ConnectionType.Door,
        int width  = 1,
        int height = 2)
    {
        RoomA  = roomA;
        RoomB  = roomB;
        Bounds = bounds;
        Type   = type;
        Width  = width;
        Height = height;
    }

    public Room GetOtherRoom(Room room) => room == RoomA ? RoomB : RoomA;
}