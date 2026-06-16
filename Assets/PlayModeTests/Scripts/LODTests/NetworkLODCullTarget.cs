using System.Collections.Generic;
using PurrNet;

/// <summary>
/// LOD-governed identity for NetworkLODCullScenario; tracks liveness per peer.
/// </summary>
public class NetworkLODCullTarget : NetworkIdentity
{
    public static NetworkLODCullTarget localInstance;

    static readonly HashSet<NetworkID> _alive = new HashSet<NetworkID>();

    public static int aliveCount => _alive.Count;

    NetworkID? _trackedId;

    public static void ResetAll()
    {
        localInstance = null;
        _alive.Clear();
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned()
    {
        localInstance = this;
        if (id.HasValue)
        {
            _trackedId = id.Value;
            _alive.Add(id.Value);
        }
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            _alive.Remove(_trackedId.Value);
        _trackedId = null;

        if (localInstance == this)
            localInstance = null;
    }
}
