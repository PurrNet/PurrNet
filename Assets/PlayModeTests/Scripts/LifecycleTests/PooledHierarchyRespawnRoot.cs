using System.Collections.Generic;
using PurrNet;
using UnityEngine;

// Root of the pooled multi-child prefab spawned/despawned by PooledHierarchyRespawnScenario.
public class PooledHierarchyRespawnRoot : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _alive = new();

    public static PooledHierarchyRespawnRoot LocalInstance;
    public static int AliveCount => _alive.Count;
    public static bool SawBadId;

    private NetworkID? _trackedId;

    public static void ResetAll()
    {
        _alive.Clear();
        LocalInstance = null;
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
        _alive.Add(id.Value);
        LocalInstance = this;
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            _alive.Remove(_trackedId.Value);
        _trackedId = null;

        if (LocalInstance == this)
            LocalInstance = null;
    }
}
