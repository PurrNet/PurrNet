using PurrNet;
using UnityEngine;

// Churn object for DestroyDuringSpawnScenario. The spawning client destroys each one right after
// spawning it, reproducing "client spawned then destroyed it during / just after the spawn
// handshake". ServerSawCount confirms the spawn actually reached the server before the destroy.
public class DestroyDuringSpawnIdentity : NetworkIdentity
{
    public static int ServerSawCount;

    public static void ResetAll()
    {
        ServerSawCount = 0;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
        if (isServer)
            ServerSawCount++;
    }
}
