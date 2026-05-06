using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;
using CompressionLevel = PurrNet.CompressionLevel;

public class ModuleTargetRpcsModule : NetworkModule
{
    public static int DoneCount;

    public static int? IEnumeratorTargetReceived;
    public static int IEnumeratorTargetRunCount;
    public static int? IntReceived;
    public static string StringReceived;
    public static TestPayload? StructReceived;
    public static int? GenericIntReceived;
    public static int? CompNoneReceived;
    public static int? CompFastReceived;
    public static int? CompBalancedReceived;
    public static int? CompBestReceived;
    public static int? UnreliableReceived;
    public static int? MultiArrayReceived;
    public static int? MultiListReceived;
    public static int? MultiEnumReceived;
    public static int? AsyncTargetReceived;
    public static int? P2PTargetReceived;
    public static int? AsyncSeedReceived;
    public static int? AsyncPackStampReceived;
    public static int? AsyncUnpackStampReceived;
    public static readonly List<int> DeltaSequence = new();
    public static readonly List<int> DeltaPackedSequence = new();

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

    [ServerRpc(requireOwnership: false)] public void TriggerTargetInt(int payload, RPCInfo info = default) => SendTargetInt(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetString(string payload, RPCInfo info = default) => SendTargetString(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetStruct(TestPayload payload, RPCInfo info = default) => SendTargetStruct(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetGeneric<T>(T payload, RPCInfo info = default) => SendTargetGeneric(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetCompNone(int payload, RPCInfo info = default) => SendTargetCompNone(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetCompFast(int payload, RPCInfo info = default) => SendTargetCompFast(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetCompBalanced(int payload, RPCInfo info = default) => SendTargetCompBalanced(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetCompBest(int payload, RPCInfo info = default) => SendTargetCompBest(info.sender, payload);
    [ServerRpc(requireOwnership: false, channel: Channel.Unreliable)] public void TriggerTargetUnreliable(int payload, RPCInfo info = default) => SendTargetUnreliable(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetDeltaOff(int payload, RPCInfo info = default) => SendTargetDeltaOff(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetDeltaOn(int payload, RPCInfo info = default) => SendTargetDeltaOn(info.sender, payload);
    [ServerRpc(requireOwnership: false)] public void TriggerTargetIEnumerator(int payload, RPCInfo info = default) => SendTargetIEnumerator(info.sender, payload);

    [ServerRpc(requireOwnership: false)]
    public void TriggerTargetArray(int payload, RPCInfo info = default)
    {
        var targets = new[] { info.sender };
        SendTargetArray(targets, payload);
    }

    [ServerRpc(requireOwnership: false)]
    public void TriggerTargetList(int payload, RPCInfo info = default)
    {
        var targets = new List<PlayerID> { info.sender };
        SendTargetList(targets, payload);
    }

    [ServerRpc(requireOwnership: false)]
    public void TriggerTargetEnumerable(int payload, RPCInfo info = default)
    {
        var targets = new HashSet<PlayerID> { info.sender };
        SendTargetEnumerable(targets, payload);
    }

    [ServerRpc(requireOwnership: false)]
    public async Task<int> TriggerAsyncTarget(int payload, RPCInfo info = default)
    {
        return await SendTargetEchoAsync(info.sender, payload);
    }

    [ServerRpc(requireOwnership: false)]
    public void TriggerTargetAsyncPackable(int seed, RPCInfo info = default)
    {
        SendTargetAsyncPackable(info.sender, new AsyncPayload { seed = seed });
    }

    [TargetRpc]
    public void SendTargetAsyncPackable(PlayerID target, AsyncPayload p)
    {
        AsyncSeedReceived = p.seed;
        AsyncPackStampReceived = p.packStamp;
        AsyncUnpackStampReceived = p.unpackStamp;
    }

    [TargetRpc] public void SendTargetInt(PlayerID target, int payload) => IntReceived = payload;
    [TargetRpc] public void SendTargetString(PlayerID target, string payload) => StringReceived = payload;
    [TargetRpc] public void SendTargetStruct(PlayerID target, TestPayload payload) => StructReceived = payload;

    [TargetRpc]
    public void SendTargetGeneric<T>(PlayerID target, T payload)
    {
        if (typeof(T) == typeof(int))
            GenericIntReceived = (int)(object)payload;
    }

    [TargetRpc(compressionLevel: CompressionLevel.None)] public void SendTargetCompNone(PlayerID target, int payload) => CompNoneReceived = payload;
    [TargetRpc(compressionLevel: CompressionLevel.Fast)] public void SendTargetCompFast(PlayerID target, int payload) => CompFastReceived = payload;
    [TargetRpc(compressionLevel: CompressionLevel.Balanced)] public void SendTargetCompBalanced(PlayerID target, int payload) => CompBalancedReceived = payload;
    [TargetRpc(compressionLevel: CompressionLevel.Best)] public void SendTargetCompBest(PlayerID target, int payload) => CompBestReceived = payload;
    [TargetRpc(channel: Channel.Unreliable)] public void SendTargetUnreliable(PlayerID target, int payload) => UnreliableReceived = payload;

    [TargetRpc(deltaPacked: false)]
    public void SendTargetDeltaOff(PlayerID target, int payload) => DeltaSequence.Add(payload);

    [TargetRpc(deltaPacked: true)]
    public void SendTargetDeltaOn(PlayerID target, int payload) => DeltaPackedSequence.Add(payload);

    [TargetRpc]
    public IEnumerator SendTargetIEnumerator(PlayerID target, int payload)
    {
        yield return null;
        yield return null;
        IEnumeratorTargetReceived = payload;
        IEnumeratorTargetRunCount++;
    }

    [TargetRpc] public void SendTargetArray(PlayerID[] targets, int payload) => MultiArrayReceived = payload;
    [TargetRpc] public void SendTargetList(IList<PlayerID> targets, int payload) => MultiListReceived = payload;
    [TargetRpc] public void SendTargetEnumerable(ICollection<PlayerID> targets, int payload) => MultiEnumReceived = payload;

    [TargetRpc]
    public Task<int> SendTargetEchoAsync(PlayerID target, int payload)
    {
        AsyncTargetReceived = payload;
        return Task.FromResult(payload + 1);
    }

    [TargetRpc(requireServer: false)]
    public Task SendTargetServer(PlayerID target, int payload)
    {
        Debug.Log($"[ModuleTargetRpcsModule] SendTargetServer received: {payload}");
        return Task.CompletedTask;
    }

    [TargetRpc(requireServer: false)]
    public void SendTargetP2P(PlayerID target, int payload) => P2PTargetReceived = payload;

    [TargetRpc(requireServer: false)]
    public Task<ulong> PingPlayer(PlayerID target)
    {
        return Task.FromResult(NetworkManager.main.localPlayer.id.value);
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone()
    {
        DoneCount++;
        Debug.Log($"[ModuleTargetRpcsModule] SignalDone received (total {DoneCount})");
    }
}

public class ModuleTargetRpcs : NetworkIdentity
{
    public static ModuleTargetRpcs LocalInstance;
    public readonly ModuleTargetRpcsModule module = new();

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }
}
