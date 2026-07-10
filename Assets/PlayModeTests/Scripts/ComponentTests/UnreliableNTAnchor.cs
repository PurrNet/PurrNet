using System.Collections.Generic;
using PurrNet;

public class UnreliableNTAnchor : NetworkIdentity
{
    public static UnreliableNTAnchor localInstance;

    static readonly HashSet<NetworkID> _alive = new();

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
        if (localInstance == this)
            localInstance = null;

        if (_trackedId.HasValue)
            _alive.Remove(_trackedId.Value);
        _trackedId = null;
    }
}
