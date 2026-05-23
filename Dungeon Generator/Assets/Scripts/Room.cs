using System.Collections.Generic;
using UnityEngine;

public class Room
{
    public RectInt Bounds { get; }
    public List<Door> Doors { get; } = new List<Door>();

    public Vector2 Center => new Vector2(
        Bounds.x + Bounds.width * 0.5f,
        Bounds.y + Bounds.height * 0.5f
    );

    public Room(RectInt bounds)
    {
        Bounds = bounds;
    }

    public Room GetNeighbour(Door door) => door.GetOtherRoom(this);
}