using System;
using PurrNet;

[Serializable]
public class SyncVarObserverAddedDeltaProbeModule : NetworkModule
{
    public override void OnObserverAdded(PlayerID player, bool isSpawner)
    {
        if (parent is SyncVarObserverAddedDeltaIdentity identity)
            identity.TryRunPostSeedProbe(player);
    }
}
