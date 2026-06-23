using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class NonPooledRuntimeHierarchyRoot : NetworkIdentity
{
    private static readonly HashSet<NetworkID> Alive = new();

    public static int AliveCount => Alive.Count;
    public static bool SawBadId;

    private NetworkID? _trackedId;

    public static void ResetAll()
    {
        Alive.Clear();
        SawBadId = false;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned()
    {
        if (!id.HasValue || id.Value == default)
        {
            SawBadId = true;
            return;
        }

        _trackedId = id.Value;
        Alive.Add(id.Value);
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            Alive.Remove(_trackedId.Value);

        _trackedId = null;
    }
}
