using PurrNet.StateMachine;
using UnityEngine;

public class StateMachinePayloadTestNode : StateNode<int>, IStateMachineTestState
{
    [SerializeField] private int _key;
    private int _lastData;
    private int _enterWithDataCount;

    /// <summary>Stable test id used to verify synced state order.</summary>
    public int key => _key;

    /// <summary>Last payload received when this state was entered.</summary>
    public int lastData => _lastData;

    /// <summary>Number of payload enters received by this node.</summary>
    public int enterWithDataCount => _enterWithDataCount;

    internal void Configure(int value)
    {
        _key = value;
    }

    public override void Enter(int data)
    {
        _lastData = data;
        _enterWithDataCount++;
    }

    public override bool CanEnter(int data) => data == StateMachineTestRig.PayloadValue;
}
