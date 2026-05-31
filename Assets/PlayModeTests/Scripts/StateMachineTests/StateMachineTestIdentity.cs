using System.Collections.Generic;
using PurrNet;
using PurrNet.StateMachine;
using UnityEngine;

public class StateMachineTestIdentity : NetworkIdentity
{
    public static readonly int[] InitialKeys = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    public static readonly int[] PhaseOneKeys = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    public static readonly int[] InsertedCurrentKeys = { 0, 1, 2, 3, 4, 100, 5, 6, 7, 8, 9 };
    public static readonly int[] AddedCurrentKeys = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 101 };
    public static readonly int[] FinalKeys = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 101 };

    [SerializeField] private StateMachine _machine;
    [SerializeField] private StateMachineTestNode[] _initialStates;
    [SerializeField] private StateMachineTestNode _insertedState;
    [SerializeField] private StateMachineTestNode _addedState;

    public static StateMachineTestIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int InitialMatchCount;
    public static int PhaseOneMatchCount;
    public static int FinalMatchCount;
    public static int ServerDoneCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool PhaseOneReleased;
    public static bool PhaseDoneReceived;

    /// <summary>Current state id reported by the state machine.</summary>
    public int currentStateId => _machine.currentState.stateId;

    /// <summary>Stable key of the current test state.</summary>
    public int currentKey => _machine.currentStateNode is StateMachineTestNode node ? node.key : -1;

    internal void Configure(
        StateMachine machine,
        StateMachineTestNode[] initialStates,
        StateMachineTestNode insertedState,
        StateMachineTestNode addedState)
    {
        _machine = machine;
        _initialStates = initialStates;
        _insertedState = insertedState;
        _addedState = addedState;
    }

    /// <summary>Clears shared scenario counters before a state machine scenario starts.</summary>
    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        InitialMatchCount = 0;
        PhaseOneMatchCount = 0;
        FinalMatchCount = 0;
        ServerDoneCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        PhaseOneReleased = false;
        PhaseDoneReceived = false;
    }

    /// <summary>Returns whether the state list reached the initial state order.</summary>
    public bool MatchesInitial() => SequenceEquals(_machine.states, InitialKeys);

    /// <summary>Returns whether the inserted state was removed while preserving the current state.</summary>
    public bool MatchesPhaseOne()
    {
        return SequenceEquals(_machine.states, PhaseOneKeys) &&
               currentKey == 9 &&
               currentStateId == 9;
    }

    /// <summary>Returns whether add and remove-at preserved the current state.</summary>
    public bool MatchesFinal()
    {
        return SequenceEquals(_machine.states, FinalKeys) &&
               currentKey == 101 &&
               currentStateId == 9;
    }

    /// <summary>Returns whether the current state moved after inserting before it.</summary>
    public bool MatchesInsertedCurrent()
    {
        return SequenceEquals(_machine.states, InsertedCurrentKeys) &&
               currentKey == 9 &&
               currentStateId == 10;
    }

    /// <summary>Returns whether the added state became current before removing an earlier state.</summary>
    public bool MatchesAddedCurrent()
    {
        return SequenceEquals(_machine.states, AddedCurrentKeys) &&
               currentKey == 101 &&
               currentStateId == 10;
    }

    /// <summary>Describes the current state id, current key, and state order.</summary>
    public string Describe()
    {
        var states = _machine.states;
        var keys = new int[states.Count];
        for (var i = 0; i < keys.Length; i++)
            keys[i] = states[i] is StateMachineTestNode node ? node.key : -1;

        return $"stateId={currentStateId}, current={currentKey}, states=[{string.Join(",", keys)}]";
    }

    /// <summary>Returns whether the nested state machine is controlled by this peer.</summary>
    public bool MachineIsController(bool ownerAuth) => _machine.IsController(ownerAuth);

    /// <summary>Inserts the spare state before the target current state.</summary>
    public void InsertRegressionState() => _machine.InsertState(_insertedState, 5);

    /// <summary>Queues a transition to the original last state.</summary>
    public bool SetStateToOriginalLast() => _machine.SetState(_initialStates[9], force: true);

    /// <summary>Removes the inserted spare state by reference.</summary>
    public bool RemoveRegressionState() => _machine.RemoveState(_insertedState);

    /// <summary>Adds the spare state at the end of the state list.</summary>
    public void AddExtraState() => _machine.AddState(_addedState);

    /// <summary>Queues a transition to the added spare state.</summary>
    public bool SetStateToAdded() => _machine.SetState(_addedState, force: true);

    /// <summary>Removes the first state to force a current-state remap.</summary>
    public void RemoveFirstState() => _machine.RemoveStateAt(0);

    private static bool SequenceEquals(IReadOnlyList<StateNode> states, int[] expected)
    {
        if (states.Count != expected.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (states[i] is not StateMachineTestNode node || node.key != expected[i])
                return false;
        }

        return true;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    /// <summary>Signals that this peer spawned the test identity.</summary>
    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    /// <summary>Signals that this peer received the initial state order.</summary>
    [ServerRpc(requireOwnership: false)]
    public void SignalInitialMatched(RPCInfo info = default) => InitialMatchCount++;

    /// <summary>Signals that this peer matched the insert/remove remap phase.</summary>
    [ServerRpc(requireOwnership: false)]
    public void SignalPhaseOneMatched(RPCInfo info = default) => PhaseOneMatchCount++;

    /// <summary>Signals that this peer matched the final add/remove-at phase.</summary>
    [ServerRpc(requireOwnership: false)]
    public void SignalFinalMatched(RPCInfo info = default) => FinalMatchCount++;

    /// <summary>Signals that this peer completed the scenario.</summary>
    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    /// <summary>Broadcasts which player should own the owner-authoritative state machine.</summary>
    [ObserversRpc(runLocally: true)]
    public void BroadcastOwner(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerIdReceived = true;
    }

    /// <summary>Releases the owner to run the final phase after all observers matched phase one.</summary>
    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseOneReleased() => PhaseOneReleased = true;

    /// <summary>Broadcasts that the scenario is complete.</summary>
    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
