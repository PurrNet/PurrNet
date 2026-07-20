using PurrNet;
using UnityEngine;

/// <summary>
/// Networked <see cref="ValidatedSyncVar{T}"/> coverage for server writes, owner optimistic writes,
/// server acceptance, and rejection rollback.
/// </summary>
public class ValidatedSyncVarIdentity : NetworkIdentity
{
    public const int ServerAcceptedValue = 10;
    public const int OwnerAcceptedValue = 20;
    public const int RejectedValue = -5;

    [SerializeField] private ValidatedSyncVar<int> _value = new(0);

    public static ValidatedSyncVarIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerAcceptedCount;
    public static int OwnerAcceptedCount;
    public static int OwnerRejectResolvedCount;
    public static int RejectedVerifiedCount;
    public static int ServerDoneCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool RejectCommandReceived;
    public static bool VerifyRejectedReceived;
    public static bool PhaseDoneReceived;

    private bool _listenersRegistered;

    public bool SawOwnerAcceptedValidated { get; private set; }
    public bool SawValidationFail { get; private set; }
    public int FailedValue { get; private set; } = int.MinValue;
    public int AuthoritativeAfterFail { get; private set; } = int.MinValue;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerAcceptedCount = 0;
        OwnerAcceptedCount = 0;
        OwnerRejectResolvedCount = 0;
        RejectedVerifiedCount = 0;
        ServerDoneCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        RejectCommandReceived = false;
        VerifyRejectedReceived = false;
        PhaseDoneReceived = false;
    }

    public bool MatchesServerAccepted() => _value.value == ServerAcceptedValue;

    public bool MatchesOwnerAccepted() => _value.value == OwnerAcceptedValue;

    public string Describe()
    {
        return $"value={_value.value}, sawOwnerAcceptedValidated={SawOwnerAcceptedValidated}, " +
               $"sawValidationFail={SawValidationFail}, failed={FailedValue}, authoritative={AuthoritativeAfterFail}";
    }

    public void SetServerAccepted() => _value.value = ServerAcceptedValue;

    public void SetOwnerAccepted() => _value.value = OwnerAcceptedValue;

    public void SetRejected() => _value.value = RejectedValue;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        RegisterListenersOnce();

        if (asServer)
            _value.serverValidation += Validate;
    }

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;

        if (_listenersRegistered)
        {
            _listenersRegistered = false;
            _value.onChangedWithOld -= OnValueChanged;
            _value.onValidationFail -= OnValidationFail;
        }
    }

    private void RegisterListenersOnce()
    {
        if (_listenersRegistered)
            return;

        _listenersRegistered = true;
        _value.onChangedWithOld += OnValueChanged;
        _value.onValidationFail += OnValidationFail;
    }

    private static bool Validate(int oldValue, int newValue) => newValue >= 0;

    private void OnValueChanged(int oldValue, int newValue, bool serverValidated)
    {
        if (newValue == OwnerAcceptedValue && serverValidated)
            SawOwnerAcceptedValidated = true;
    }

    private void OnValidationFail(int failedValue, int authoritativeValue)
    {
        SawValidationFail = true;
        FailedValue = failedValue;
        AuthoritativeAfterFail = authoritativeValue;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalServerAccepted(RPCInfo info = default) => ServerAcceptedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalOwnerAccepted(RPCInfo info = default) => OwnerAcceptedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalOwnerRejectResolved(RPCInfo info = default) => OwnerRejectResolvedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalRejectedVerified(RPCInfo info = default) => RejectedVerifiedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastOwner(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastRejectCommand() => RejectCommandReceived = true;

    [ObserversRpc(runLocally: true)]
    public void BroadcastVerifyRejected() => VerifyRejectedReceived = true;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
