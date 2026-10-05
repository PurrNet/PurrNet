using System.Collections.Generic;
using PurrNet;

public class ServerOnlyHolderItem : NetworkIdentity
{
    private static readonly HashSet<ServerOnlyHolderItem> Alive = new();

    public static IReadOnlyCollection<ServerOnlyHolderItem> all => Alive;
    public static int AliveCount => Alive.Count;

    public static void ResetAll()
    {
        Alive.Clear();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned()
    {
        Alive.Add(this);
    }

    protected override void OnDespawned()
    {
        Alive.Remove(this);
    }
}
