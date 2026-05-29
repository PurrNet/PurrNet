using System.Collections.Generic;
using PurrNet;

// Child identity for SameFrameChildDestroyScenario. Its own type (separate statics) so it never
// shares live-set state with HierarchyRespawnChild — scenario Setup runs once up front, so shared
// statics would bleed between scenarios.
public class SameFrameDestroyChild : NetworkIdentity
{
    public bool isDisposable;

    static readonly HashSet<NetworkID> _alive = new();

    public static int AliveCount => _alive.Count;
    public static bool SawBadId;

    NetworkID? _trackedId;

    public static void ResetAll()
    {
        _alive.Clear();
        SawBadId = false;
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
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            _alive.Remove(_trackedId.Value);
        _trackedId = null;
    }
}
