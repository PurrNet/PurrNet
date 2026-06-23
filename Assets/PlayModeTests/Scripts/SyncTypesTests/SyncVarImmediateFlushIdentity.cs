using PurrNet;
using UnityEngine;

/// <summary>
/// SyncVar target for first-delta FlushImmediately coverage. The long send interval prevents the
/// regular dirty tick from sending a backup packet before the explicit flush is verified.
/// </summary>
public class SyncVarImmediateFlushIdentity : NetworkIdentity
{
    public const int ServerFlushValue = 111;
    public const int OwnerFlushValue = 222;

    [SerializeField] private SyncVar<int> _serverValue = new(0, sendIntervalInSeconds: 3600f, ownerAuth: false);
    [SerializeField] private SyncVar<int> _ownerValue = new(0, sendIntervalInSeconds: 3600f, ownerAuth: true);

    public static SyncVarImmediateFlushIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerFlushSeenCount;
    public static int OwnerFlushSeenCount;
    public static int ServerDoneCount;
    public static ulong OwnerId;
    public static bool OwnerFlushCommandReceived;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerFlushSeenCount = 0;
        OwnerFlushSeenCount = 0;
        ServerDoneCount = 0;
        OwnerId = 0;
        OwnerFlushCommandReceived = false;
        PhaseDoneReceived = false;
    }

    public bool MatchesServerFlush() => _serverValue.value == ServerFlushValue;
    public bool MatchesOwnerFlush() => _ownerValue.value == OwnerFlushValue;

    public string Describe() =>
        $"serverValue={_serverValue.value}, ownerValue={_ownerValue.value}, isOwner={isOwner}, isServer={isServer}";

    public void RunServerFlush()
    {
        _serverValue.value = ServerFlushValue;
        _serverValue.FlushImmediately();
    }

    public void RunOwnerFlush()
    {
        _ownerValue.value = OwnerFlushValue;
        _ownerValue.FlushImmediately();
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalServerFlushSeen(RPCInfo info = default) => ServerFlushSeenCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalOwnerFlushSeen(RPCInfo info = default) => OwnerFlushSeenCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastOwnerFlush(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerFlushCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
