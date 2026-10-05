using System.Collections.Generic;
using PurrNet;

public class ServerOnlyHolderParent : NetworkIdentity
{
    private static readonly HashSet<ServerOnlyHolderParent> Alive = new();

    public static IReadOnlyCollection<ServerOnlyHolderParent> all => Alive;
    public static int AliveCount => Alive.Count;

    public bool hasModel;

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
