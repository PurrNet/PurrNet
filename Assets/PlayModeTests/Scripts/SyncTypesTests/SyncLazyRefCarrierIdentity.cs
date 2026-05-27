using PurrNet;
using UnityEngine;

/// <summary>
/// Carries a server-authoritative <see cref="SyncLazyRef{T}"/> to a <see cref="SyncLazyRefTargetIdentity"/>.
/// Every observer must resolve the reference to its own local copy of the target.
/// </summary>
public class SyncLazyRefCarrierIdentity : NetworkIdentity
{
    [SerializeField] private SyncLazyRef<SyncLazyRefTargetIdentity> _ref = new(ownerAuth: false);

    public static SyncLazyRefCarrierIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ResolvedCount;
    public static int ServerDoneCount;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ResolvedCount = 0;
        ServerDoneCount = 0;
        PhaseDoneReceived = false;
    }

    public SyncLazyRefTargetIdentity RefValue => _ref.value;

    public void SetRef(SyncLazyRefTargetIdentity target) => _ref.value = target;

    /// <summary>True once the reference resolved to this peer's local target instance.</summary>
    public bool Resolved() =>
        _ref.value != null
        && SyncLazyRefTargetIdentity.LocalInstance != null
        && ReferenceEquals(_ref.value, SyncLazyRefTargetIdentity.LocalInstance);

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalResolved(RPCInfo info = default) => ResolvedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
