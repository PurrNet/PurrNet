using System.Collections.Generic;
using PurrNet;
using UnityEngine;

// Child identity for PooledHierarchyRespawnScenario. Tracks live children by NetworkID so host
// callbacks dedupe and every peer can assert the pooled prefab rebuilt its full hierarchy.
public class PooledHierarchyRespawnChild : NetworkIdentity
{
    static readonly HashSet<NetworkID> _alive = new();

    public static int AliveCount => _alive.Count;
    public static bool SawBadId;
    public static bool SawWrongParent;

    NetworkID? _trackedId;

    public static void ResetAll()
    {
        _alive.Clear();
        SawBadId = false;
        SawWrongParent = false;
    }

    protected override void OnSpawned()
    {
        if (!id.HasValue || id.Value == default)
        {
            SawBadId = true;
            return;
        }

        if (!GetComponentInParent<PooledHierarchyRespawnRoot>())
            SawWrongParent = true;

        _trackedId = id.Value;
        _alive.Add(id.Value);
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            _alive.Remove(_trackedId.Value);
        _trackedId = null;
    }
}
