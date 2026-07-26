using System.Collections.Generic;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

public class IdentityImmediateTimingRpcs : NetworkIdentity
{
    public const int Rounds = 20;
    public const int MinValidRounds = 10;

    public static IdentityImmediateTimingRpcs localInstance;
    public static int serverReadyCount;
    public static int serverDoneCount;
    public static bool probesDoneReceived;
    public static int tickPhaseViolations;
    public static int lastPreTickFrame;
    public static readonly int[] immediateFrames = new int[Rounds];
    public static readonly int[] deferredFrames = new int[Rounds];
    public static readonly HashSet<PlayerID> readyPlayers = new();

    private int _pendingRound = -1;
    private TickManager _observedTicks;

    public static void ResetAll()
    {
        localInstance = null;
        serverReadyCount = 0;
        serverDoneCount = 0;
        probesDoneReceived = false;
        tickPhaseViolations = 0;
        lastPreTickFrame = -1;

        for (int i = 0; i < Rounds; i++)
        {
            immediateFrames[i] = -1;
            deferredFrames[i] = -1;
        }

        readyPlayers.Clear();
    }

    public static void CountRounds(out int valid, out int wins)
    {
        valid = 0;
        wins = 0;

        for (int i = 0; i < Rounds; i++)
        {
            if (immediateFrames[i] < 0 || deferredFrames[i] < 0)
                continue;

            valid++;
            if (immediateFrames[i] < deferredFrames[i])
                wins++;
        }
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned()
    {
        localInstance = this;

        if (networkManager.isClient && !networkManager.isServer)
        {
            _observedTicks = networkManager.tickModule;
            if (_observedTicks != null)
                _observedTicks.onPreTick += RecordPreTickFrame;
        }
    }

    protected override void OnDespawned()
    {
        if (_observedTicks != null)
        {
            _observedTicks.onPreTick -= RecordPreTickFrame;
            _observedTicks = null;
        }

        if (localInstance == this)
            localInstance = null;
    }

    static void RecordPreTickFrame() => lastPreTickFrame = Time.frameCount;

    public void QueueRound(int round) => _pendingRound = round;

    private void Update()
    {
        if (!isServer || _pendingRound < 0)
            return;

        int round = _pendingRound;
        _pendingRound = -1;
        SendImmediateProbe(round);
        SendDeferredProbe(round);
    }

    [ObserversRpc(channel: Channel.Unreliable, immediate: true)]
    public void SendImmediateProbe(int round)
    {
        if (round < 0 || round >= Rounds || immediateFrames[round] >= 0)
            return;

        immediateFrames[round] = Time.frameCount;
    }

    // deferred dispatch drains inside the onTick chain, strictly after the full onPreTick
    // chain of the same tick, so on a pure client Time.frameCount == lastPreTickFrame is an
    // exact invariant at dispatch time; host loopback bypasses the transport, so no timing
    // is recorded there
    [ObserversRpc(channel: Channel.Unreliable)]
    public void SendDeferredProbe(int round)
    {
        if (round < 0 || round >= Rounds || deferredFrames[round] >= 0)
            return;

        deferredFrames[round] = Time.frameCount;

        if (networkManager.isServer)
            return;

        if (Time.frameCount != lastPreTickFrame)
            tickPhaseViolations++;
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
    public void BroadcastProbesDone() => probesDoneReceived = true;
}
