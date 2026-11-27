using System.Collections.Generic;
using PurrNet;

public class KeepSpawnedReconnectIdentity : NetworkIdentity
{
    public static readonly HashSet<KeepSpawnedReconnectIdentity> Alive = new();

    public static void ResetAll() => Alive.Clear();

    protected override void OnSpawned(bool asServer)
    {
        if (!asServer)
            Alive.Add(this);
    }

    protected override void OnDestroy()
    {
        Alive.Remove(this);
        base.OnDestroy();
    }
}
