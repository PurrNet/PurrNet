using PurrNet;
using UnityEngine;

public class PostSpawnSyncIdentity : NetworkIdentity
{
    public const int InitialValue = 0;
    public const int PostSpawnValue = 7777;

    [SerializeField] private SyncVar<int> _value = new(InitialValue, sendIntervalInSeconds: 0f, ownerAuth: false);

    public static PostSpawnSyncIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerSawNewValueCount;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerSawNewValueCount = 0;
    }

    public int currentValue => _value.value;

    public void SetPostSpawnValue()
    {
        _value.value = PostSpawnValue;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;

        // Write a SyncVar value from the server during OnSpawned(asServer:true). Clients
        // must see this value (PostSpawnValue), not InitialValue, when their local spawn
        // resolves — initial state must reflect server-side writes that happened before
        // the spawn packet was flushed.
        if (asServer)
            _value.value = PostSpawnValue;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalSawNewValue(RPCInfo info = default) => ServerSawNewValueCount++;
}
