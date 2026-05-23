using System.Collections.Generic;
using UnityEngine;

public class Room
{
    public RectInt Bounds { get; }
    public int Floor { get; }
    public List<Door> Doors { get; } = new();

    public Vector2 Center => new(
        Bounds.x + Bounds.width  * 0.5f,
        Bounds.y + Bounds.height * 0.5f
    );

    public Room(RectInt bounds, int floor = 0)
    {
        Bounds = bounds;
        Floor  = floor;
    }

    public Room GetNeighbour(Door door) => door.GetOtherRoom(this);
}