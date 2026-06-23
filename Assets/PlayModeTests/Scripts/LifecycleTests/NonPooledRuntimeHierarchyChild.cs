using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class NonPooledRuntimeHierarchyChild : NetworkIdentity
{
    private static readonly HashSet<NetworkID> Alive = new();

    public static int AliveCount => Alive.Count;
    public static bool SawBadId;
    public static bool SawWrongParent;

    private NetworkID? _trackedId;

    public static void ResetAll()
    {
        Alive.Clear();
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

        if (!GetComponentInParent<NonPooledRuntimeHierarchyRoot>())
            SawWrongParent = true;

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
