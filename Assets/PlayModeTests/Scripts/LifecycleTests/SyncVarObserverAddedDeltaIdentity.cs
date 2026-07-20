using System.Collections.Generic;
using System.Text;
using PurrNet;
using UnityEngine;

public class SyncVarObserverAddedDeltaIdentity : NetworkIdentity
{
    public const int SeedValue = 10;
    public const int FirstDeltaValue = 101;

    [SerializeField] private SyncVar<int> _value = new(0, sendIntervalInSeconds: 0f, ownerAuth: false);
    [SerializeField] private SyncVarObserverAddedDeltaProbeModule _postSeedProbe = new();

    public static SyncVarObserverAddedDeltaIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static ulong VictimPlayerId;
    public static bool VictimIdReceived;
    public static bool PhaseDoneReceived;
    public static bool PostSeedProbeRan;
    public static bool SawFirstDelta;
    public static readonly List<int> ObservedValues = new();
    public static readonly List<ulong> RemovedObservers = new();

    private bool _listenerRegistered;
    private ulong? _armedVictimId;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        VictimPlayerId = 0;
        VictimIdReceived = false;
        PhaseDoneReceived = false;
        PostSeedProbeRan = false;
        SawFirstDelta = false;
        ObservedValues.Clear();
        RemovedObservers.Clear();
    }

    public int currentValue => _value.value;

    public string DescribeLocalState() =>
        $"value={_value.value}, sawFirst={SawFirstDelta}, observed=[{DescribeObservedValues()}]";

    public static string DescribeObservedValues()
    {
        if (ObservedValues.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (int i = 0; i < ObservedValues.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(ObservedValues[i]);
        }
        return builder.ToString();
    }

    public void ArmPostSeedProbe(ulong victimId) => _armedVictimId = victimId;

    public void TryRunPostSeedProbe(PlayerID player)
    {
        if (!isServer || PostSeedProbeRan || !_armedVictimId.HasValue || _armedVictimId.Value != player.id.value)
            return;

        PostSeedProbeRan = true;
        _armedVictimId = null;

        _value.value = FirstDeltaValue;
        _value.FlushImmediately();

        BroadcastPhaseDone();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        RegisterListenerOnce();

        if (asServer)
            _value.value = SeedValue;
    }

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;

        if (_listenerRegistered)
        {
            _value.onChanged -= OnValueChanged;
            _listenerRegistered = false;
        }
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        RemovedObservers.Add(player.id.value);
    }

    private void RegisterListenerOnce()
    {
        if (_listenerRegistered)
            return;

        _listenerRegistered = true;
        _value.onChanged += OnValueChanged;
    }

    private void OnValueChanged(int value)
    {
        ObservedValues.Add(value);

        if (value == FirstDeltaValue)
            SawFirstDelta = true;
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimPlayerId = victimId;
        VictimIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;
}
