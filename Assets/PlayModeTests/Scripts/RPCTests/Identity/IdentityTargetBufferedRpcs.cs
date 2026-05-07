using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using CompressionLevel = PurrNet.CompressionLevel;

public class IdentityTargetBufferedRpcs : NetworkIdentity
{
    public static IdentityTargetBufferedRpcs LocalInstance;
    public static int ServerDoneCount;
    public static readonly List<PlayerID> KickoffPlayers = new();
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
    public static int? LastIEnumeratorReceived;
    public static int IEnumeratorReceiveCount;

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
        LastIEnumeratorReceived = null; IEnumeratorReceiveCount = 0;
        PhaseACompleteCount = 0;
    }

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

    [ServerRpc(requireOwnership: false)]
    public void Kickoff(RPCInfo info = default)
    {
        if (!KickoffPlayers.Contains(info.sender))
            KickoffPlayers.Add(info.sender);
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default)
    {
        ServerDoneCount++;
    }

    [ObserversRpc]
    public void NotifyPhaseAComplete() => PhaseACompleteCount++;

    [TargetRpc(bufferLast: true)]
    public void SendTargetBufferedInt(PlayerID target, int payload)
    {
        LastIntReceived = payload;
        IntReceiveCount++;
    }

    [TargetRpc(bufferLast: true)]
    public void SendTargetBufferedString(PlayerID target, string payload)
    {
        LastStringReceived = payload;
        StringReceiveCount++;
    }

    [TargetRpc(bufferLast: true)]
    public void SendTargetBufferedStruct(PlayerID target, TestPayload payload)
    {
        LastStructReceived = payload;
        StructReceiveCount++;
    }

    [TargetRpc(bufferLast: true)]
    public void SendTargetBufferedGeneric<T>(PlayerID target, T payload)
    {
        if (typeof(T) == typeof(int))
        {
            LastGenericIntReceived = (int)(object)payload;
            GenericIntReceiveCount++;
        }
    }

    [TargetRpc(bufferLast: true, deltaPacked: false)]
    public void SendTargetBufferedDeltaOff(PlayerID target, int payload)
    {
        LastDeltaOffReceived = payload;
        DeltaOffReceiveCount++;
    }

    [TargetRpc(bufferLast: true, compressionLevel: CompressionLevel.None)]
    public void SendTargetBufferedCompNone(PlayerID target, int payload)
    {
        LastCompNoneReceived = payload;
        CompNoneReceiveCount++;
    }

    [TargetRpc(bufferLast: true, compressionLevel: CompressionLevel.Fast)]
    public void SendTargetBufferedCompFast(PlayerID target, int payload)
    {
        LastCompFastReceived = payload;
        CompFastReceiveCount++;
    }

    [TargetRpc(bufferLast: true, compressionLevel: CompressionLevel.Balanced)]
    public void SendTargetBufferedCompBalanced(PlayerID target, int payload)
    {
        LastCompBalancedReceived = payload;
        CompBalancedReceiveCount++;
    }

    [TargetRpc(bufferLast: true, compressionLevel: CompressionLevel.Best)]
    public void SendTargetBufferedCompBest(PlayerID target, int payload)
    {
        LastCompBestReceived = payload;
        CompBestReceiveCount++;
    }

    [TargetRpc(bufferLast: true)]
    public void SendTargetBufferedAsyncPackable(PlayerID target, AsyncPayload p)
    {
        LastAsyncSeedReceived = p.seed;
        LastAsyncPackStampReceived = p.packStamp;
        LastAsyncUnpackStampReceived = p.unpackStamp;
        AsyncReceiveCount++;
    }

    [TargetRpc(bufferLast: true)]
    public IEnumerator SendTargetBufferedIEnumerator(PlayerID target, int payload)
    {
        yield return null;
        yield return null;
        LastIEnumeratorReceived = payload;
        IEnumeratorReceiveCount++;
    }
}
