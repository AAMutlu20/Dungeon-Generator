using UnityEngine;

namespace Generation
{
    // Helper class to define Doors
    // Each door stores references to the rooms it connects and its tile bounds
    public class Door
    {
        public Room RoomA { get; }
        public Room RoomB { get; }
        public RectInt Bounds { get; }
        public int Width { get; }
        public int Height { get; }

        public Door(Room roomA, Room roomB, RectInt bounds, int width = 4, int height = 4)
        {
            RoomA = roomA;
            RoomB = roomB;
            Bounds = bounds;
            Width = width;
            Height = height;
        }

        public Room GetOtherRoom(Room room) => room == RoomA ? RoomB : RoomA;
    }
}