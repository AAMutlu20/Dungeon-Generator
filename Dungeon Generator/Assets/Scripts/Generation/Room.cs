using System.Collections.Generic;
using UnityEngine;

namespace Generation
{
    // Helper class to define Rooms
    // Each room stores a list of its own doors
    public class Room
    {
        public RectInt Bounds { get; }
        public List<Door> Doors { get; } = new();

        public Vector2 Center => new(
            Bounds.x + Bounds.width * 0.5f,
            Bounds.y + Bounds.height * 0.5f
        );

        public Room(RectInt bounds)
        {
            Bounds = bounds;
        }

        public Room GetNeighbour(Door door) => door.GetOtherRoom(this);
    }
}