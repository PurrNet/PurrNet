using System;
using System.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using CompressionLevel = PurrNet.CompressionLevel;

public class ModuleObserversBufferedRpcsModule : NetworkModule
{
    public static int ServerDoneCount;
    public static int ServerKickoffCount;
    public static int PhaseACompleteCount;

    public static int? LastIntReceived;
    public static int IntReceiveCount;
    public static string LastStringReceived;
    public static int StringReceiveCount;
    public static TestPayload? LastStructReceived;
    public static int StructReceiveCount;
    public static int? LastGenericIntReceived;
    public static int GenericIntReceiveCount;
    public static int? LastDeltaOffReceived;
    public static int DeltaOffReceiveCount;
    public static int? LastCompNoneReceived;
    public static int CompNoneReceiveCount;
    public static int? LastCompFastReceived;
    public static int CompFastReceiveCount;
    public static int? LastCompBalancedReceived;
    public static int CompBalancedReceiveCount;
    public static int? LastCompBestReceived;
    public static int CompBestReceiveCount;
    public static int? LastAsyncSeedReceived;
    public static int? LastAsyncPackStampReceived;
    public static int? LastAsyncUnpackStampReceived;
    public static int AsyncReceiveCount;

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

    public static void ResetClientState()
    {
        LastIntReceived = null; IntReceiveCount = 0;
        LastStringReceived = null; StringReceiveCount = 0;
        LastStructReceived = null; StructReceiveCount = 0;
        LastGenericIntReceived = null; GenericIntReceiveCount = 0;
        LastDeltaOffReceived = null; DeltaOffReceiveCount = 0;
        LastCompNoneReceived = null; CompNoneReceiveCount = 0;
        LastCompFastReceived = null; CompFastReceiveCount = 0;
        LastCompBalancedReceived = null; CompBalancedReceiveCount = 0;
        LastCompBestReceived = null; CompBestReceiveCount = 0;
        LastAsyncSeedReceived = null; LastAsyncPackStampReceived = null; LastAsyncUnpackStampReceived = null;
        AsyncReceiveCount = 0;
        PhaseACompleteCount = 0;
    }

    [ServerRpc(requireOwnership: false)]
    public void Kickoff(RPCInfo info = default)
    {
        ServerKickoffCount++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default)
    {
        ServerDoneCount++;
    }

    [ObserversRpc]
    public void NotifyPhaseAComplete() => PhaseACompleteCount++;

    [ObserversRpc(bufferLast: true)]
    public void BroadcastBufferedInt(int payload) { LastIntReceived = payload; IntReceiveCount++; }

    [ObserversRpc(bufferLast: true)]
    public void BroadcastBufferedString(string payload) { LastStringReceived = payload; StringReceiveCount++; }

    [ObserversRpc(bufferLast: true)]
    public void BroadcastBufferedStruct(TestPayload payload) { LastStructReceived = payload; StructReceiveCount++; }

    [ObserversRpc(bufferLast: true)]
    public void BroadcastBufferedGeneric<T>(T payload)
    {
        if (typeof(T) == typeof(int))
        {
            LastGenericIntReceived = (int)(object)payload;
            GenericIntReceiveCount++;
        }
    }

    [ObserversRpc(bufferLast: true, deltaPacked: false)]
    public void BroadcastBufferedDeltaOff(int payload) { LastDeltaOffReceived = payload; DeltaOffReceiveCount++; }

    [ObserversRpc(bufferLast: true, compressionLevel: CompressionLevel.None)]
    public void BroadcastBufferedCompNone(int payload) { LastCompNoneReceived = payload; CompNoneReceiveCount++; }

    [ObserversRpc(bufferLast: true, compressionLevel: CompressionLevel.Fast)]
    public void BroadcastBufferedCompFast(int payload) { LastCompFastReceived = payload; CompFastReceiveCount++; }

    [ObserversRpc(bufferLast: true, compressionLevel: CompressionLevel.Balanced)]
    public void BroadcastBufferedCompBalanced(int payload) { LastCompBalancedReceived = payload; CompBalancedReceiveCount++; }

    [ObserversRpc(bufferLast: true, compressionLevel: CompressionLevel.Best)]
    public void BroadcastBufferedCompBest(int payload) { LastCompBestReceived = payload; CompBestReceiveCount++; }

    [ObserversRpc(bufferLast: true)]
    public void BroadcastBufferedAsyncPackable(AsyncPayload p)
    {
        LastAsyncSeedReceived = p.seed;
        LastAsyncPackStampReceived = p.packStamp;
        LastAsyncUnpackStampReceived = p.unpackStamp;
        AsyncReceiveCount++;
    }
}

public class ModuleObserversBufferedRpcs : NetworkIdentity
{
    public static ModuleObserversBufferedRpcs LocalInstance;
    public readonly ModuleObserversBufferedRpcsModule module = new();

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;
    }
}
