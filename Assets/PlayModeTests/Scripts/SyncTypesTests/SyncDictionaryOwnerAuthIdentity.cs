using System.Text;
using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncDictionary{TKey,TValue}"/>. The owning client drives every
/// operation; the server and all other observers must converge, and only the owner is the controller.
/// </summary>
public class SyncDictionaryOwnerAuthIdentity : NetworkIdentity
{
    public static readonly int[] FinalKeys = { 7, 8 };
    public static readonly int[] FinalVals = { 70, 80 };

    [SerializeField] private SyncDictionary<int, int> _dict = new(ownerAuth: true);

    public static SyncDictionaryOwnerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ConvergedCount;
    public static int ServerDoneCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ConvergedCount = 0;
        ServerDoneCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        PhaseDoneReceived = false;
    }

    public bool MatchesFinal()
    {
        if (_dict.Count != FinalKeys.Length) return false;
        for (int i = 0; i < FinalKeys.Length; i++)
            if (!_dict.TryGetValue(FinalKeys[i], out var v) || v != FinalVals[i]) return false;
        return true;
    }

    public string Describe()
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in _dict.ToDictionary())
        {
            if (!first) sb.Append(",");
            sb.Append(kv.Key).Append(":").Append(kv.Value);
            first = false;
        }
        return sb.Append("}").ToString();
    }

    public void RunOwnerSequence()
    {
        _dict.Clear();
        _dict.Add(1, 10); _dict.Add(2, 20); _dict.Add(3, 30);
        _dict.Add(4, 40);   // Add
        _dict[2] = 21;      // update existing key
        _dict.Remove(3);    // Remove
        _dict.Clear();      // Clear
        _dict.Add(7, 70);   // refill
        _dict.Add(8, 80);
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalConverged(RPCInfo info = default) => ConvergedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastOwner(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
