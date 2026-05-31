using System.Collections.Generic;
using PurrNet;
using PurrNet.StateMachine;
using UnityEngine;

public class StateMachineTestRig : NetworkIdentity
{
    public static readonly int[] InitialKeys = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    public static readonly int[] PhaseOneKeys = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    public static readonly int[] InsertedCurrentKeys = { 0, 1, 2, 3, 4, 100, 5, 6, 7, 8, 9 };
    public static readonly int[] AddedCurrentKeys = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 101 };
    public static readonly int[] FinalKeys = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 101 };
    public static readonly int[] PayloadKeys = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 101, 200 };
    public static readonly int[] InsertedAfterCurrentKeys = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 101, 200, 201, 102 };
    public static readonly int[] RemovedByReferenceKeys = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 101, 200, 201 };
    public static readonly int[] ExpandedFinalKeys = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 101, 200 };
    public const int PayloadValue = 4242;

    [SerializeField] private string _scenarioName;
    [SerializeField] private StateMachine _machine;
    [SerializeField] private StateMachineTestNode[] _initialStates;
    [SerializeField] private StateMachineTestNode _insertedState;
    [SerializeField] private StateMachineTestNode _addedState;
    [SerializeField] private StateMachineTestNode _removeByReferenceState;
    [SerializeField] private StateMachinePayloadTestNode _payloadState;
    [SerializeField] private StateMachineTestNode _blockedState;

    private static readonly Dictionary<string, StateMachineTestRig> LocalInstances = new();
    private bool _eventsHooked;
    private int _stateChangedCount;
    private int _receivedNewDataCount;
    private int _lastPreviousKey = -1;
    private int _lastNewKey = -1;
    private int _stateChangedBaseline;

    public static ulong OwnerId;
    public static bool OwnerIdReceived;

    /// <summary>State machine under test.</summary>
    public StateMachine machine => _machine;

    /// <summary>Current state id reported by the state machine.</summary>
    public int currentStateId => _machine.currentState.stateId;

    /// <summary>Stable key of the current test state.</summary>
    public int currentKey => GetKey(_machine.currentStateNode);

    /// <summary>Stable key of the previous test state.</summary>
    public int previousKey => GetKey(_machine.previousStateNode);

    /// <summary>Number of state-change callbacks seen by this rig.</summary>
    public int stateChangedCount => _stateChangedCount;

    /// <summary>Number of network data callbacks seen by this rig.</summary>
    public int receivedNewDataCount => _receivedNewDataCount;

    internal void Configure(
        string scenarioName,
        StateMachine machine,
        StateMachineTestNode[] initialStates,
        StateMachineTestNode insertedState,
        StateMachineTestNode addedState,
        StateMachineTestNode removeByReferenceState,
        StateMachinePayloadTestNode payloadState,
        StateMachineTestNode blockedState)
    {
        _scenarioName = scenarioName;
        _machine = machine;
        _initialStates = initialStates;
        _insertedState = insertedState;
        _addedState = addedState;
        _removeByReferenceState = removeByReferenceState;
        _payloadState = payloadState;
        _blockedState = blockedState;
    }

    /// <summary>Clears shared scenario counters before a state machine scenario starts.</summary>
    public static void ResetAll(string scenarioName)
    {
        LocalInstances.Remove(scenarioName);
        OwnerId = 0;
        OwnerIdReceived = false;
    }

    /// <summary>Returns the local rig for a specific state machine scenario.</summary>
    public static StateMachineTestRig GetLocalInstance(string scenarioName)
    {
        LocalInstances.TryGetValue(scenarioName, out var instance);
        return instance;
    }

    /// <summary>Returns whether the state list reached the initial state order.</summary>
    public bool MatchesInitial() => SequenceEquals(_machine.states, InitialKeys);

    /// <summary>Returns whether the inserted state was removed while preserving the current state.</summary>
    public bool MatchesPhaseOne()
    {
        return SequenceEquals(_machine.states, PhaseOneKeys) &&
               currentKey == 9 &&
               currentStateId == 9 &&
               StateChangeCountUnchanged();
    }

    /// <summary>Returns whether add and remove-at preserved the current state.</summary>
    public bool MatchesFinal()
    {
        return SequenceEquals(_machine.states, FinalKeys) &&
               currentKey == 101 &&
               currentStateId == 9 &&
               StateChangeCountUnchanged();
    }

    /// <summary>Returns whether the current state moved after inserting before it.</summary>
    public bool MatchesInsertedCurrent()
    {
        return SequenceEquals(_machine.states, InsertedCurrentKeys) &&
               currentKey == 9 &&
               currentStateId == 10 &&
               previousKey == 0 &&
               _lastPreviousKey == 0 &&
               _lastNewKey == 9;
    }

    /// <summary>Returns whether the added state became current before removing an earlier state.</summary>
    public bool MatchesAddedCurrent()
    {
        return SequenceEquals(_machine.states, AddedCurrentKeys) &&
               currentKey == 101 &&
               currentStateId == 10 &&
               previousKey == 9;
    }

    /// <summary>Returns whether the generic payload state became current with the expected data.</summary>
    public bool MatchesPayloadCurrent()
    {
        return SequenceEquals(_machine.states, PayloadKeys) &&
               currentKey == 200 &&
               currentStateId == 10 &&
               previousKey == 101 &&
               _payloadState.lastData == PayloadValue &&
               _payloadState.enterWithDataCount > 0;
    }

    /// <summary>Returns whether inserting after current preserved the current state without a change event.</summary>
    public bool MatchesInsertedAfterCurrent()
    {
        return SequenceEquals(_machine.states, InsertedAfterCurrentKeys) &&
               currentKey == 200 &&
               currentStateId == 10 &&
               StateChangeCountUnchanged();
    }

    /// <summary>Returns whether NextValid skipped the blocked state.</summary>
    public bool MatchesNextValid()
    {
        return SequenceEquals(_machine.states, InsertedAfterCurrentKeys) &&
               currentKey == 102 &&
               currentStateId == 12 &&
               previousKey == 200;
    }

    /// <summary>Returns whether PreviousValid skipped the blocked state.</summary>
    public bool MatchesPreviousValid()
    {
        return SequenceEquals(_machine.states, InsertedAfterCurrentKeys) &&
               currentKey == 200 &&
               currentStateId == 10 &&
               previousKey == 102;
    }

    /// <summary>Returns whether Previous moved to the previous state.</summary>
    public bool MatchesPrevious()
    {
        return SequenceEquals(_machine.states, InsertedAfterCurrentKeys) &&
               currentKey == 101 &&
               currentStateId == 9 &&
               previousKey == 200;
    }

    /// <summary>Returns whether Next moved to the next state.</summary>
    public bool MatchesNext()
    {
        return SequenceEquals(_machine.states, InsertedAfterCurrentKeys) &&
               currentKey == 200 &&
               currentStateId == 10 &&
               previousKey == 101;
    }

    /// <summary>Returns whether removing a later state by reference preserved the current state.</summary>
    public bool MatchesRemovedByReference()
    {
        return SequenceEquals(_machine.states, RemovedByReferenceKeys) &&
               currentKey == 200 &&
               currentStateId == 10 &&
               StateChangeCountUnchanged();
    }

    /// <summary>Returns whether removing a later state by index preserved the current state.</summary>
    public bool MatchesExpandedFinal()
    {
        return SequenceEquals(_machine.states, ExpandedFinalKeys) &&
               currentKey == 200 &&
               currentStateId == 10 &&
               StateChangeCountUnchanged();
    }

    /// <summary>Describes the current state id, current key, and state order.</summary>
    public string Describe()
    {
        var states = _machine.states;
        var keys = new int[states.Count];
        for (var i = 0; i < keys.Length; i++)
            keys[i] = GetKey(states[i]);

        return $"stateId={currentStateId}, current={currentKey}, previous={previousKey}, changes={_stateChangedCount}, received={_receivedNewDataCount}, last=({_lastPreviousKey}->{_lastNewKey}), payload={_payloadState.lastData}/{_payloadState.enterWithDataCount}, states=[{string.Join(",", keys)}]";
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

    /// <summary>Captures the current state-change count for later no-op mutation checks.</summary>
    public void CaptureStateChangeCount() => _stateChangedBaseline = _stateChangedCount;

    /// <summary>Returns whether the state-change count is unchanged since the last capture.</summary>
    public bool StateChangeCountUnchanged() => _stateChangedCount == _stateChangedBaseline;

    /// <summary>Adds the generic payload state at the end of the state list.</summary>
    public void AddPayloadState() => _machine.AddState(_payloadState);

    /// <summary>Attempts to enter the generic payload state with invalid data.</summary>
    public bool TrySetPayloadWithInvalidData() => _machine.SetState(_payloadState, -1);

    /// <summary>Queues a transition to the generic payload state with valid data.</summary>
    public bool SetStateToPayload() => _machine.SetState(_payloadState, PayloadValue);

    /// <summary>Inserts two states after the current state.</summary>
    public void InsertAfterCurrentStates()
    {
        _machine.InsertState(_blockedState, currentStateId + 1);
        _machine.InsertState(_removeByReferenceState, currentStateId + 2);
    }

    /// <summary>Queues a transition to the next valid state.</summary>
    public bool NextValid() => _machine.NextValid();

    /// <summary>Queues a transition to the previous valid state.</summary>
    public bool PreviousValid() => _machine.PreviousValid();

    /// <summary>Queues a transition to the previous state.</summary>
    public bool Previous() => _machine.Previous();

    /// <summary>Queues a transition to the next state.</summary>
    public bool Next() => _machine.Next();

    /// <summary>Removes a later state by reference.</summary>
    public bool RemoveLaterStateByReference() => _machine.RemoveState(_removeByReferenceState);

    /// <summary>Removes a later state by index.</summary>
    public void RemoveLaterStateAt() => _machine.RemoveStateAt(_machine.states.Count - 1);

    private static bool SequenceEquals(IReadOnlyList<StateNode> states, int[] expected)
    {
        if (states.Count != expected.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (GetKey(states[i]) != expected[i])
                return false;
        }

        return true;
    }

    private static int GetKey(StateNode state)
    {
        return state is IStateMachineTestState testState ? testState.key : -1;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        LocalInstances[_scenarioName] = this;

        if (_eventsHooked)
            return;

        _machine.onStateChanged += OnStateChanged;
        _machine.onReceivedNewData += OnReceivedNewData;
        _eventsHooked = true;
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);

        if (!_eventsHooked)
            return;

        _machine.onStateChanged -= OnStateChanged;
        _machine.onReceivedNewData -= OnReceivedNewData;
        _eventsHooked = false;
    }

    private void OnStateChanged(StateNode previousState, StateNode newState)
    {
        _stateChangedCount++;
        _lastPreviousKey = GetKey(previousState);
        _lastNewKey = GetKey(newState);
    }

    private void OnReceivedNewData() => _receivedNewDataCount++;
}
