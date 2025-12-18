using PurrNet.Packing;
using PurrNet.StateMachine;
using UnityEngine;

public class StateTwo : StateNode<MyData>
{
    [SerializeField] private bool canEnter;
    
    public override void Enter()
    {
        base.Enter();
        //Debug.Log($"Entered state {this}");
    }

    public override void Enter(MyData data)
    {
        base.Enter(data);
        Debug.Log($"Entered stateTwo with data: {data}");
    }

    public override void Enter(bool asServer)
    {
        base.Enter(asServer);
        //Debug.Log($"Entered state {this} | asServer: {asServer}");
    }

    public override void Exit()
    {
        base.Exit();
        //Debug.Log($"Exited state {this}");
    }

    public override void Exit(bool asServer)
    {
        base.Exit(asServer);
        //Debug.Log($"Exited state {this} | asServer: {asServer}");
    }

    public override bool CanEnter()
    {
        return canEnter;
    }

    [ContextMenu("Next state")]
    private void NextState()
    {
        var goesNext = machine.Next();
        Debug.Log($"{goesNext}");
    }
}

public struct MyData : IPackedAuto
{
    public int Fails;
    public float Time;

    public MyData(int fails, float time)
    {
        Fails = fails;
        Time = time;
    }

    public override string ToString()
    {
        return $"MyData: Fails: {Fails} | Time: {Time}";
    }
}
