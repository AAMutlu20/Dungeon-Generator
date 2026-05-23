using UnityEngine;

public class Door
{
    public Room RoomA { get; }
    public Room RoomB { get; }
    public RectInt Bounds { get; }

    public Door(Room roomA, Room roomB, RectInt bounds)
    {
        RoomA   = roomA;
        RoomB   = roomB;
        Bounds  = bounds;
    }

    public Room GetOtherRoom(Room room)
    {
        return room == RoomA ? RoomB : RoomA;
    }
}