using System.Collections.Generic;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class IdentityMtuBehaviourRpcs : NetworkIdentity
{
    public const int PayloadSize = 4096;
    public const int DefaultSeed = 1;
    public const int FragmentSeed = 2;
    public const int UpgradeSeed = 3;
    public const int ToServerSeed = 4;

    public static IdentityMtuBehaviourRpcs localInstance;
    public static int serverReadyCount;
    public static int serverDoneCount;
    public static bool phaseDoneReceived;
    public static bool defaultReceived;
    public static bool fragmentReceived;
    public static bool fragmentCorrupt;
    public static bool upgradeReceived;
    public static bool upgradeCorrupt;
    public static bool serverFragmentCorrupt;
    public static readonly HashSet<ulong> serverFragmentSenders = new();

    public static void ResetAll()
    {
        localInstance = null;
        serverReadyCount = 0;
        serverDoneCount = 0;
        phaseDoneReceived = false;
        defaultReceived = false;
        fragmentReceived = false;
        fragmentCorrupt = false;
        upgradeReceived = false;
        upgradeCorrupt = false;
        serverFragmentCorrupt = false;
        serverFragmentSenders.Clear();
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

    [ObserversRpc(channel: Channel.Unreliable)]
    public void SendDefaultOversized(byte[] data) => defaultReceived = true;

    [ObserversRpc(channel: Channel.Unreliable, mtuExceededBehaviour: MTUExceededBehaviourOverride.Fragment)]
    public void SendFragmentOversized(byte[] data)
    {
        fragmentReceived = true;
        if (!Matches(data, FragmentSeed))
            fragmentCorrupt = true;
    }

    [ObserversRpc(channel: Channel.Unreliable, mtuExceededBehaviour: MTUExceededBehaviourOverride.UpgradeToReliable)]
    public void SendUpgradeOversized(byte[] data)
    {
        upgradeReceived = true;
        if (!Matches(data, UpgradeSeed))
            upgradeCorrupt = true;
    }

    [ServerRpc(channel: Channel.Unreliable, requireOwnership: false, mtuExceededBehaviour: MTUExceededBehaviourOverride.Fragment)]
    public void SendToServerFragment(byte[] data, RPCInfo info = default)
    {
        serverFragmentSenders.Add(info.sender.id.value);
        if (!Matches(data, ToServerSeed))
            serverFragmentCorrupt = true;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => serverReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => serverDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => phaseDoneReceived = true;
}
