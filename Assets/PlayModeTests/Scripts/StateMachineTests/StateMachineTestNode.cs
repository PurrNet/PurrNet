using PurrNet.StateMachine;
using UnityEngine;

public class StateMachineTestNode : StateNode, IStateMachineTestState
{
    [SerializeField] private int _key;
    [SerializeField] private bool _canEnter = true;

    /// <summary>Stable test id used to verify synced state order.</summary>
    public int key => _key;

    internal void Configure(int value, bool canEnter = true)
    {
        _key = value;
        _canEnter = canEnter;
    }

    public override bool CanEnter() => _canEnter;
}
