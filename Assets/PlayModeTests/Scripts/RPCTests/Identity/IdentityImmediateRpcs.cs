using System.Collections.Generic;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class IdentityImmediateRpcs : NetworkIdentity
{
    public const int PayloadSize = 256;
    public const int ObserversSeed = 11;
    public const int TargetSeed = 12;
    public const int ReliableSeed = 13;
    public const int ToServerSeed = 14;

    public static IdentityImmediateRpcs localInstance;
    public static int serverReadyCount;
    public static int serverDoneCount;
    public static bool phaseDoneReceived;
    public static bool observersReceived;
    public static bool observersCorrupt;
    public static bool targetReceived;
    public static bool targetCorrupt;
    public static bool reliableReceived;
    public static bool reliableCorrupt;
    public static bool serverImmediateCorrupt;
    public static readonly HashSet<PlayerID> readyPlayers = new();
    public static readonly HashSet<ulong> serverImmediateSenders = new();

    public static void ResetAll()
    {
        localInstance = null;
        serverReadyCount = 0;
        serverDoneCount = 0;
        phaseDoneReceived = false;
        observersReceived = false;
        observersCorrupt = false;
        targetReceived = false;
        targetCorrupt = false;
        reliableReceived = false;
        reliableCorrupt = false;
        serverImmediateCorrupt = false;
        readyPlayers.Clear();
        serverImmediateSenders.Clear();
    }

    public static byte[] MakePayload(int seed)
    {
        var data = new byte[PayloadSize];
        for (int i = 0; i < PayloadSize; i++)
            data[i] = (byte)(seed * 31 + i * 7);
        return data;
    }

    static bool Matches(byte[] data, int seed)
    {
        if (data == null || data.Length != PayloadSize)
            return false;

        for (int i = 0; i < PayloadSize; i++)
        {
            if (data[i] != (byte)(seed * 31 + i * 7))
                return false;
        }

        return true;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned() => localInstance = this;

    protected override void OnDespawned()
    {
        if (localInstance == this)
            localInstance = null;
    }

    [ObserversRpc(channel: Channel.Unreliable, immediate: true)]
    public void SendObserversImmediate(byte[] data)
    {
        observersReceived = true;
        if (!Matches(data, ObserversSeed))
            observersCorrupt = true;
    }

    [TargetRpc(channel: Channel.Unreliable, immediate: true)]
    public void SendTargetImmediate(PlayerID target, byte[] data)
    {
        targetReceived = true;
        if (!Matches(data, TargetSeed))
            targetCorrupt = true;
    }

    // reliable control RPC: interleaved ordered traffic must be unaffected by the immediate lane
    // (immediate on a reliable channel is a compile error by contract)
    [ObserversRpc(channel: Channel.ReliableOrdered)]
    public void SendReliableImmediate(byte[] data)
    {
        reliableReceived = true;
        if (!Matches(data, ReliableSeed))
            reliableCorrupt = true;
    }

    [ServerRpc(channel: Channel.Unreliable, requireOwnership: false, immediate: true)]
    public void SendToServerImmediate(byte[] data, RPCInfo info = default)
    {
        serverImmediateSenders.Add(info.sender.id.value);
        if (!Matches(data, ToServerSeed))
            serverImmediateCorrupt = true;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default)
    {
        if (readyPlayers.Add(info.sender))
            serverReadyCount++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => serverDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => phaseDoneReceived = true;
}
