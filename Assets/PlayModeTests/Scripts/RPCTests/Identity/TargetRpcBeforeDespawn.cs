using System.Collections.Generic;
using PurrNet;

public class TargetRpcBeforeDespawn : NetworkIdentity
{
    public static TargetRpcBeforeDespawn ServerInstance;
    public static int? Received;

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
            ServerInstance = this;
    }

    public void ReplyToEveryoneThenDespawn(int payload)
    {
        var observersCopy = new List<PlayerID>(observers);
        for (var i = 0; i < observersCopy.Count; i++)
            Reply(observersCopy[i], payload);

        Despawn();
    }

    [TargetRpc]
    private void Reply(PlayerID target, int payload) => Received = payload;
}
