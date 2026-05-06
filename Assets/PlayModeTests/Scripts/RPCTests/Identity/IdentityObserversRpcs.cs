using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;
using CompressionLevel = PurrNet.CompressionLevel;

public class IdentityObserversRpcs : NetworkIdentity
{
    public static IdentityObserversRpcs LocalInstance;
    public static int DoneCount;
    public static readonly Dictionary<ulong, int[]> AsyncObservedStamps = new();

    public static readonly Dictionary<ulong, int> IntReceived = new();
    public static readonly Dictionary<ulong, string> StringReceived = new();
    public static readonly Dictionary<ulong, TestPayload> StructReceived = new();
    public static readonly Dictionary<ulong, int> GenericIntReceived = new();
    public static readonly Dictionary<ulong, int> CompNoneReceived = new();
    public static readonly Dictionary<ulong, int> CompFastReceived = new();
    public static readonly Dictionary<ulong, int> CompBalancedReceived = new();
    public static readonly Dictionary<ulong, int> CompBestReceived = new();
    public static readonly Dictionary<ulong, int> UnreliableReceived = new();
    public static readonly Dictionary<ulong, List<int>> DeltaSequence = new();
    public static readonly Dictionary<ulong, List<int>> DeltaPackedSequence = new();
    public static readonly Dictionary<ulong, int> P2PBroadcastReceived = new();

    [Serializable]
    public struct TestPayload
    {
        public int id;
        public string label;
        public float weight;
    }

    [Serializable]
    public struct AsyncPayload : IAsyncPackable
    {
        public int seed;
        public int packStamp;
        public int unpackStamp;

        public async ValueTask<IAsyncPackable> PrepareForPackAsync()
        {
            await Task.Yield();
            packStamp = seed + 1;
            return this;
        }

        public async ValueTask<IAsyncPackable> PrepareAfterUnpackAsync()
        {
            await Task.Yield();
            unpackStamp = packStamp + 10;
            return this;
        }
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastInt(ulong corr, int payload, RPCInfo info = default) => BroadcastInt(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastString(ulong corr, string payload, RPCInfo info = default) => BroadcastString(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastStruct(ulong corr, TestPayload payload, RPCInfo info = default) => BroadcastStruct(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastGeneric<T>(ulong corr, T payload, RPCInfo info = default) => BroadcastGeneric(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastCompNone(ulong corr, int payload, RPCInfo info = default) => BroadcastCompNone(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastCompFast(ulong corr, int payload, RPCInfo info = default) => BroadcastCompFast(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastCompBalanced(ulong corr, int payload, RPCInfo info = default) => BroadcastCompBalanced(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastCompBest(ulong corr, int payload, RPCInfo info = default) => BroadcastCompBest(corr, payload);
    [ServerRpc(requireOwnership: false, channel: Channel.Unreliable)] public void TriggerBroadcastUnreliable(ulong corr, int payload, RPCInfo info = default) => BroadcastUnreliable(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastDeltaOff(ulong corr, int payload, RPCInfo info = default) => BroadcastDeltaOff(corr, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerBroadcastDeltaOn(ulong corr, int payload, RPCInfo info = default) => BroadcastDeltaOn(corr, payload);

    [ServerRpc(requireOwnership: false)]
    public void TriggerBroadcastAsyncPackable(ulong corr, int seed, RPCInfo info = default)
    {
        BroadcastAsyncPackable(corr, new AsyncPayload { seed = seed });
    }

    [ObserversRpc]
    public void BroadcastInt(ulong corr, int payload) => IntReceived[corr] = payload;

    [ObserversRpc]
    public void BroadcastString(ulong corr, string payload) => StringReceived[corr] = payload;

    [ObserversRpc]
    public void BroadcastStruct(ulong corr, TestPayload payload) => StructReceived[corr] = payload;

    [ObserversRpc]
    public void BroadcastGeneric<T>(ulong corr, T payload)
    {
        if (typeof(T) == typeof(int))
            GenericIntReceived[corr] = (int)(object)payload;
    }

    [ObserversRpc(compressionLevel: CompressionLevel.None)]
    public void BroadcastCompNone(ulong corr, int payload) => CompNoneReceived[corr] = payload;

    [ObserversRpc(compressionLevel: CompressionLevel.Fast)]
    public void BroadcastCompFast(ulong corr, int payload) => CompFastReceived[corr] = payload;

    [ObserversRpc(compressionLevel: CompressionLevel.Balanced)]
    public void BroadcastCompBalanced(ulong corr, int payload) => CompBalancedReceived[corr] = payload;

    [ObserversRpc(compressionLevel: CompressionLevel.Best)]
    public void BroadcastCompBest(ulong corr, int payload) => CompBestReceived[corr] = payload;

    [ObserversRpc(channel: Channel.Unreliable)]
    public void BroadcastUnreliable(ulong corr, int payload) => UnreliableReceived[corr] = payload;

    [ObserversRpc(deltaPacked: false)]
    public void BroadcastDeltaOff(ulong corr, int payload)
    {
        if (!DeltaSequence.TryGetValue(corr, out var list))
            DeltaSequence[corr] = list = new List<int>();
        list.Add(payload);
    }

    [ObserversRpc(deltaPacked: true)]
    public void BroadcastDeltaOn(ulong corr, int payload)
    {
        if (!DeltaPackedSequence.TryGetValue(corr, out var list))
            DeltaPackedSequence[corr] = list = new List<int>();
        list.Add(payload);
    }

    [ObserversRpc(requireServer: false)]
    public void BroadcastP2P(ulong corr, int payload) => P2PBroadcastReceived[corr] = payload;

    [ObserversRpc]
    public void BroadcastAsyncPackable(ulong corr, AsyncPayload p)
    {
        AsyncObservedStamps[corr] = new[] { p.seed, p.packStamp, p.unpackStamp };
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default)
    {
        DoneCount++;
        Debug.Log($"[IdentityObserversRpcs] SignalDone received from sender {info.sender.id.value} (total {DoneCount})");
    }
}
