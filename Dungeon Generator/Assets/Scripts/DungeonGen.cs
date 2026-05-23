using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    private RectInt _roomA;
    private RectInt _roomB;
    
    public bool splitHorizontally = false;

    private void Start()
    {
        var room = new RectInt(0, 0, 100, 50);

        if (splitHorizontally)
        {
            var splitY = room.height / 2;
            
            _roomA = new RectInt(room.x, room.y, room.width, splitY + 1);
            _roomB = new RectInt(room.x, room.y + splitY, room.width, room.height - splitY);
        }
        else
        {
            var splitX = room.width / 2;
            
            _roomA = new RectInt(room.x, room.y, splitX + 1, room.height);
            _roomB = new RectInt(room.x + splitX, room.y, room.width - splitX, room.height);
        }
    }

    private void Update()
    {
        AlgorithmsUtils.DebugRectInt(_roomA, Color.purple);
        AlgorithmsUtils.DebugRectInt(_roomB, Color.hotPink);
    }
}