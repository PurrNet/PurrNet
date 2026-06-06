using System.Reflection;
using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative SyncVar used to reproduce reconnect packet-id drift. The owner sends a burst
/// before disconnecting, waits for the reconnect copy to restore that value, then sends one more
/// owner-auth value.
/// </summary>
public class SyncVarOwnerReconnectIdentity : NetworkIdentity
{
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
    public static bool RestoredAfterReconnect;
    public static int BurstReportCount;
    public static ulong BurstReportSender;
    public static int BurstReportValueBefore;
    public static int BurstReportValueAfter;
    public static ulong BurstReportPacketIdBefore;
    public static ulong BurstReportPacketIdAfter;
    public static bool BurstReportIgnoreServerUpdatesAfter;

    private static readonly FieldInfo PacketIdField = typeof(SyncVar<int>).GetField(
        "_id", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo IgnoreServerUpdatesField = typeof(SyncVar<int>).GetField(
        "_ignoreServerUpdates", BindingFlags.Instance | BindingFlags.NonPublic);

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
        RestoredAfterReconnect = false;
        BurstReportCount = 0;
        BurstReportSender = 0;
        BurstReportValueBefore = 0;
        BurstReportValueAfter = 0;
        BurstReportPacketIdBefore = 0;
        BurstReportPacketIdAfter = 0;
        BurstReportIgnoreServerUpdatesAfter = false;
    }

    public int currentValue => _value.value;

    public ulong debugPacketId => PacketIdField != null ? (ulong)PacketIdField.GetValue(_value) : ulong.MaxValue;

    public bool debugIgnoreServerUpdates =>
        IgnoreServerUpdatesField != null && (bool)IgnoreServerUpdatesField.GetValue(_value);

    public string DescribeLocalSyncVar() =>
        $"value={currentValue}, packetId={debugPacketId}, ignoreServerUpdates={debugIgnoreServerUpdates}";

    public static string DescribeBurstReport() =>
        $"count={BurstReportCount}, sender={BurstReportSender}, beforeValue={BurstReportValueBefore}, " +
        $"afterValue={BurstReportValueAfter}, packetIdBefore={BurstReportPacketIdBefore}, " +
        $"packetIdAfter={BurstReportPacketIdAfter}, ignoreServerUpdatesAfter={BurstReportIgnoreServerUpdatesAfter}";

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    public void RunOwnerBurst(int firstValue, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _value.value = firstValue + i;
            _value.FlushImmediately();
        }
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalVictimReturned(RPCInfo info = default) => VictimReturnedCount++;

    [ServerRpc(requireOwnership: false)]
    private void ReportOwnerBurstSent(
        int valueBefore, int valueAfter, ulong packetIdBefore, ulong packetIdAfter,
        bool ignoreServerUpdatesAfter, RPCInfo info = default)
    {
        BurstReportCount++;
        BurstReportSender = info.sender.id.value;
        BurstReportValueBefore = valueBefore;
        BurstReportValueAfter = valueAfter;
        BurstReportPacketIdBefore = packetIdBefore;
        BurstReportPacketIdAfter = packetIdAfter;
        BurstReportIgnoreServerUpdatesAfter = ignoreServerUpdatesAfter;
    }

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

        int valueBefore = currentValue;
        ulong packetIdBefore = debugPacketId;
        RunOwnerBurst(firstValue, count);
        ReportOwnerBurstSent(
            valueBefore, currentValue, packetIdBefore, debugPacketId, debugIgnoreServerUpdates);
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
