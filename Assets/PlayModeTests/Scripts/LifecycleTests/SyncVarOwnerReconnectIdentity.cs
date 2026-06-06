using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative SyncVar used to reproduce reconnect packet-id drift. The owner sends a burst
/// before disconnecting, then dirties its freshly-spawned reconnect copy before the server asks for
/// a small post-reconnect burst.
/// </summary>
public class SyncVarOwnerReconnectIdentity : NetworkIdentity
{
    public const int ReconnectPrimeValue = 1500;

    [SerializeField] private SyncVar<int> _value = new(0, sendIntervalInSeconds: 0f, ownerAuth: true);

    public static SyncVarOwnerReconnectIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static int VictimReturnedCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool DisconnectCommandReceived;
    public static bool PostReconnectBurstReceived;
    public static bool PhaseDoneReceived;
    public static bool PrimeOnNextOwnerSpawn;
    public static bool PrimedAfterReconnect;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        VictimReturnedCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        DisconnectCommandReceived = false;
        PostReconnectBurstReceived = false;
        PhaseDoneReceived = false;
        PrimeOnNextOwnerSpawn = false;
        PrimedAfterReconnect = false;
    }

    public int currentValue => _value.value;

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    private void Update()
    {
        TryPrimeAfterReconnect();
    }

    public void RunOwnerBurst(int firstValue, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _value.value = firstValue + i;
            _value.FlushImmediately();
        }
    }

    private void TryPrimeAfterReconnect()
    {
        if (!PrimeOnNextOwnerSpawn || !isSpawned || !isOwner)
            return;

        PrimeOnNextOwnerSpawn = false;
        _value.value = ReconnectPrimeValue;
        _value.FlushImmediately();
        PrimedAfterReconnect = true;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalVictimReturned(RPCInfo info = default) => VictimReturnedCount++;

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastOwner(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastDisconnectCommand()
    {
        DisconnectCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPostReconnectBurst(int firstValue, int count)
    {
        PostReconnectBurstReceived = true;

        if (!networkManager.isLocalPlayerReady || networkManager.localPlayer.id.value != OwnerId)
            return;

        RunOwnerBurst(firstValue, count);
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
