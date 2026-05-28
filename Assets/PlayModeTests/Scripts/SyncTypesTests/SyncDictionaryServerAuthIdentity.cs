using System.Collections.Generic;
using System.Text;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncDictionary{TKey,TValue}"/>. Exercises initial state, Add,
/// value update via indexer, Remove, Clear and refill, asserting convergence on every observer.
/// </summary>
public class SyncDictionaryServerAuthIdentity : NetworkIdentity
{
    public static readonly int[] InitialKeys = { 1, 2, 3 };
    public static readonly int[] InitialVals = { 10, 20, 30 };
    public static readonly int[] FinalKeys = { 7, 8 };
    public static readonly int[] FinalVals = { 70, 80 };

    [SerializeField] private SyncDictionary<int, int> _dict = new(ownerAuth: false);

    public static SyncDictionaryServerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int InitialMatchCount;
    public static int ConvergedCount;
    public static int ServerDoneCount;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        InitialMatchCount = 0;
        ConvergedCount = 0;
        ServerDoneCount = 0;
        PhaseDoneReceived = false;
    }

    public bool MatchesInitial() => Matches(InitialKeys, InitialVals);
    public bool MatchesFinal() => Matches(FinalKeys, FinalVals);

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

    public void SeedInitial()
    {
        _dict.Clear();
        for (int i = 0; i < InitialKeys.Length; i++)
            _dict.Add(InitialKeys[i], InitialVals[i]);
    }

    public void RunServerOps()
    {
        _dict.Add(4, 40);   // Add
        _dict[2] = 21;      // update existing key
        _dict.Remove(3);    // Remove
        _dict.Clear();      // Clear
        _dict.Add(7, 70);   // refill
        _dict.Add(8, 80);
    }

    private bool Matches(int[] keys, int[] vals)
    {
        if (_dict.Count != keys.Length) return false;
        for (int i = 0; i < keys.Length; i++)
            if (!_dict.TryGetValue(keys[i], out var v) || v != vals[i]) return false;
        return true;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        if (asServer)
            SeedInitial();
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalInitialMatched(RPCInfo info = default) => InitialMatchCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalConverged(RPCInfo info = default) => ConvergedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
