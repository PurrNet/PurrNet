using PurrNet.StateMachine;
using UnityEngine;

public class StateMachineTestNode : StateNode
{
    [SerializeField] private int _key;

    /// <summary>Stable test id used to verify synced state order.</summary>
    public int key => _key;

    internal void Configure(int value)
    {
        _key = value;
    }
}
