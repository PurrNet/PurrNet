using PurrNet;
using UnityEngine;

public class NTReconnectTeleportIdentity : NetworkIdentity
{
    public static NTReconnectTeleportIdentity localInstance;
    public static int lastOwnerSpawnTeleport = -1;

    public static void ResetAll()
    {
        localInstance = null;
        lastOwnerSpawnTeleport = -1;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        localInstance = this;

        if (!asServer && NTReconnectTeleportScenario.restoreOwnerOnSpawn &&
            networkManager.isLocalPlayerReady &&
            networkManager.localPlayer.id.value == NTReconnectTeleportScenario.ownerId)
        {
            // Application-style saved-position restoration: no ForceSync or NT toggle.
            transform.position = NTReconnectTeleportScenario.savedPosition;
            lastOwnerSpawnTeleport = NTReconnectTeleportScenario.spawnPlan;
        }
    }

    protected override void OnDespawned()
    {
        if (localInstance == this)
            localInstance = null;
    }
}
