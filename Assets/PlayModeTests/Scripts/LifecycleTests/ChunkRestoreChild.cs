using System.Collections.Generic;
using PurrNet;

// Child identity for ChunkRestoreScenario. Own type/statics so it never shares live-set state with
// the other lifecycle scenarios (Setup runs once up front).
public class ChunkRestoreChild : NetworkIdentity
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
