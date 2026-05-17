using Arch.Core;
using UnityEngine;

public struct Position
{
    public float X;
    public float Y;

    public override string ToString() => $"{X},{Y}";
}

public class Test : MonoBehaviour
{
    void Start()
    {
        using var world = World.Create();
        var adventurer = world.Create(new Position() { X = 1, Y = 1 });
        var query = new QueryDescription().WithAll<Position>();
        world.Query(in query, (Entity entity, ref Position position) =>
        {
            Debug.LogError(position);
        });
    }
}
