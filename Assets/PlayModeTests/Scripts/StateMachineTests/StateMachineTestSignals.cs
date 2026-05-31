using PurrNet;

public static class StateMachineTestSignals
{
    /// <summary>Signals that this peer spawned the state machine rig.</summary>
    [ServerRpc(requireOwnership: false)]
    public static void SignalReady() => StateMachineTestRig.ServerReadyCount++;

    /// <summary>Signals that this peer received the initial state order.</summary>
    [ServerRpc(requireOwnership: false)]
    public static void SignalInitialMatched() => StateMachineTestRig.InitialMatchCount++;

    /// <summary>Signals that this peer matched the insert/remove remap phase.</summary>
    [ServerRpc(requireOwnership: false)]
    public static void SignalPhaseOneMatched() => StateMachineTestRig.PhaseOneMatchCount++;

    /// <summary>Signals that this peer matched the final add/remove-at phase.</summary>
    [ServerRpc(requireOwnership: false)]
    public static void SignalFinalMatched() => StateMachineTestRig.FinalMatchCount++;

    /// <summary>Signals that this peer completed the scenario.</summary>
    [ServerRpc(requireOwnership: false)]
    public static void SignalDone() => StateMachineTestRig.ServerDoneCount++;

    /// <summary>Broadcasts which player should own the owner-authoritative state machine.</summary>
    [ObserversRpc(runLocally: true)]
    public static void BroadcastOwner(ulong ownerId)
    {
        StateMachineTestRig.OwnerId = ownerId;
        StateMachineTestRig.OwnerIdReceived = true;
    }

    /// <summary>Releases the owner to run the final phase after all observers matched phase one.</summary>
    [ObserversRpc(runLocally: true)]
    public static void BroadcastPhaseOneReleased() => StateMachineTestRig.PhaseOneReleased = true;

    /// <summary>Broadcasts that the scenario is complete.</summary>
    [ObserversRpc(runLocally: true)]
    public static void BroadcastPhaseDone() => StateMachineTestRig.PhaseDoneReceived = true;
}
