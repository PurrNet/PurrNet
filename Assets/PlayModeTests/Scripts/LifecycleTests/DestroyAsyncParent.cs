using PurrNet;

public class DestroyAsyncParent : NetworkIdentity
{
    public static DestroyAsyncParent LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;
}
