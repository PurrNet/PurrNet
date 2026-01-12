using System;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;

public struct TestData : IDisposable, IPackedAuto, IDuplicate<TestData>
{
    public int a;
    public DisposableList<int> b;

    public void Dispose()
    {
        b.Dispose();
    }

    public TestData Duplicate()
    {
        return new TestData
        {
            a = this.a,
            b = PurrCopy<DisposableList<int>>.Duplicate(in this.b)
        };
    }
}

public class SpamCopy : MonoBehaviour
{
    private TestData _toCopy;
    public int count;

    private void Awake()
    {
        _toCopy = new TestData
        {
            a = 42,
            b = DisposableList<int>.Create(1000)
        };

        for (int i = 0; i < 1000; i++)
        {
            _toCopy.b.Add(i);
        }
    }

    void Update()
    {
        for (int i = 0; i < count; i++)
        {
            var copy = PurrCopy<TestData>.Copy(_toCopy);
            if (!PurrEquality<TestData>.Equals(copy, _toCopy))
                Debug.LogError("Copied data does not match original!");
            copy.Dispose();
        }
    }
}
