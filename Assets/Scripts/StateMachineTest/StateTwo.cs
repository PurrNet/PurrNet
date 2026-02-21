using PurrNet.Packing;
using PurrNet.Pooling;
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

public class HMMM : MyDataWtf<int>
{
    public HMMM(int fails, float time) : base(fails, time)
    {
    }
}

public class MyDataWtf<T> : IPackedAuto, IDuplicate<MyDataWtf<T>>
{
    private readonly int Fails;
    public float Time;
    public MyDataWtf test;
    public T test2;

    public MyDataWtf(int fails, float time)
    {
        Fails = fails;
        Time = time;
    }

    public bool EqualsFEF(MyDataWtf<T> other)
    {
        if (other.Fails != Fails || other.Time != Time || !other.test.Equals(test) || !PurrEquality<T>.Equals(other.test2, test2))
        {
            return false;
        }
        return true;
    }

    public MyDataWtf<T> Duplicate()
    {
        return null;
    }

    public override string ToString()
    {
        return $"MyData: Fails: {Fails} | Time: {Time}";
    }
}

public struct Brug
{
    public int fef;

    public Brug Duplicate()
    {
        return default;
    }
}

public class MyDataWtf : IPackedAuto
{
    private readonly int Fails;
    public float Time;
    public MyDataWtf test;
    private MyDataWtf<int> fefe;

    public MyDataWtf(int fails, float time)
    {
        Fails = fails;
        Time = time;
    }

    public MyDataWtf Duplicate()
    {
        return null;
    }

    public bool EqualsFf4e(MyDataWtf other)
    {
        if (other.Fails != Fails || other.Time != Time || !PurrEquality<MyDataWtf>.Equals(other.test, test) || !PurrEquality<MyDataWtf<int>>.Equals(other.fefe, fefe))
        {
            return false;
        }
        return true;
    }

    public override string ToString()
    {
        return $"MyData: Fails: {Fails} | Time: {Time}";
    }
}

public struct MyData : IPackedAuto
{
    public int Fails;
    public float Time;
    public Brug testrg;
    public MyDataWtf test;
    public DisposableList<int> list;

    public MyData(int fails, float time)
    {
        Fails = fails;
        Time = time;
        test = null;
        list = default;
        testrg = default;
    }

    public MyData DuplicateEE()
    {
        var copy = this;
        copy.test = test?.Duplicate();
        copy.list = list.Duplicate();
        return copy;
    }


    public bool Equalsfefe(MyData other)
    {
        if (other.Fails != Fails || other.Time != Time || !PurrEquality<MyDataWtf>.Equals(other.test, test))
        {
            return false;
        }
        return true;
    }

    public static bool WriteDeltaF(BitPacker stream, MyData oldValue, MyData value)
    {
        int flagPos = stream.AdvanceOneBitAndSet();
        bool wasChanged = DeltaPacker<int>.Write(stream, oldValue.Fails, value.Fails);
        wasChanged |= DeltaPacker<float>.Write(stream, oldValue.Time, value.Time);
        if (!(wasChanged | DeltaPacker<MyDataWtf>.Write(stream, oldValue.test, value.test)))
        {
            stream.ResetFlagAtAndMovePosition(flagPos);
            return false;
        }
        return true;
    }

    public static void ReadDeltaF(BitPacker stream, MyData oldValue, ref MyData value)
    {
        if (stream.ReadBit())
        {
            DeltaPacker<int>.Read(stream, oldValue.Fails, ref value.Fails);
            DeltaPacker<float>.Read(stream, oldValue.Time, ref value.Time);
            DeltaPacker<MyDataWtf>.Read(stream, oldValue.test, ref value.test);
        }
        else
        {
            value = Packer.Copy(in oldValue);
        }
    }

    public override string ToString()
    {
        return $"MyData: Fails: {Fails} | Time: {Time}";
    }
}
