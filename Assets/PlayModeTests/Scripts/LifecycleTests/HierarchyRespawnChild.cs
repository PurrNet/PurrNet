using System.Collections.Generic;
using PurrNet;

// Child identity for HierarchyRespawnScenario. Tracks the live set per peer keyed by NetworkID so
// the host's server+client callbacks dedupe, letting the scenario assert that every despawn->respawn
// cycle replicates the FULL child set with valid ids.
public class HierarchyRespawnChild : NetworkIdentity
{
    // Set in CreatePrefab on the single leaf the scenario destroys mid-life.
    public bool isDisposable;

    static readonly HashSet<NetworkID> _alive = new();

    public static int AliveCount => _alive.Count;

    // Tripped if a child ever spawns with a default/unassigned id (the Server:0 symptom).
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
