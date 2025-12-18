using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using PurrNet;
using UnityEngine;
using Random = UnityEngine.Random;

public enum BenchmarkType
{
    Normal,
    NormalDelta,
    Generic,
    GenericDelta,
    Returnable,
    ReturnableDelta,
    GenericReturnable,
    GenericReturnableDelta,

    StaticNormal,
    StaticNormalDelta,
    StaticGeneric,
    StaticGenericDelta,
    StaticReturnable,
    StaticReturnableDelta,
    StaticGenericReturnable,
    StaticGenericReturnableDelta,

    ServerNormal,
    ServerNormalDelta,
    ServerGeneric,
    ServerGenericDelta,
}

public struct IntContainer
{
    [UsedImplicitly]
    public int value;
}

public class TestModule : NetworkModule
{
    public int received;

    [ServerRpc(deltaPacked: false)]
    public void NormalServer()
    {
        Debug.Log("NormalServer " + received);
        ++received;
    }

    [ServerRpc(deltaPacked: true)]
    public void NormalDeltaServer(IntContainer someRandomValue)
    {
        Debug.Log("NormalDeltaServer " + someRandomValue.value);
        ++received;
    }

    [ServerRpc(deltaPacked: false)]
    public void GenericServer<T>(T someRandomValue)
    {
        Debug.Log("GenericServer " + someRandomValue);
        ++received;
    }

    [ServerRpc(deltaPacked: true)]
    public void GenericDeltaServer<T>(T someRandomValue)
    {
        Debug.Log("GenericDeltaServer " + someRandomValue);
        ++received;
    }
}

public class RandomIntBenchmark : Benchmark
{
    [SerializeField] private BenchmarkType _type;
    [SerializeField] private int _rpcCount = 1000;
    [SerializeField] private int _minRandomValue;
    [SerializeField] private int _maxRandomValue = 1000;

    static int _received;
    static int _sent;

    private readonly TestModule _testModule = new ();

    public override bool hasFinished
    {
        get
        {
            if (isClient)
                return _received == _sent;
            return true;
        }
    }

    protected override void OnBenchmarkTick()
    {
        for (int i = 0; i < _rpcCount; i++)
        {
            var v = new IntContainer
            {
                value = Random.Range(_minRandomValue, _maxRandomValue)
            };
            ++_sent;

            switch (_type)
            {
                // Static
                case BenchmarkType.StaticNormal:
                    StaticNormal(v);
                    break;
                case BenchmarkType.StaticNormalDelta:
                    StaticNormalDelta(v);
                    break;
                case BenchmarkType.StaticGeneric:
                    StaticGeneric(v);
                    break;
                case BenchmarkType.StaticGenericDelta:
                    StaticGenericDelta(v);
                    break;
                case BenchmarkType.StaticReturnable:
                    for (var o = 0; o < observers.Count; o++)
                        _ = StaticReturnable(observers[o], v);
                    break;
                case BenchmarkType.StaticReturnableDelta:
                    for (var o = 0; o < observers.Count; o++)
                        _ = StaticReturnableDelta(observers[o], v);
                    break;
                case BenchmarkType.StaticGenericReturnable:
                    for (var o = 0; o < observers.Count; o++)
                        _ = StaticReturnableGeneric(observers[o], v);
                    break;
                case BenchmarkType.StaticGenericReturnableDelta:
                    for (var o = 0; o < observers.Count; o++)
                        _ = StaticReturnableGenericDelta(observers[o], v);
                    break;
                // Normal
                case BenchmarkType.Normal:
                    Normal(v);
                    break;
                case BenchmarkType.NormalDelta:
                    NormalDelta(v);
                    break;
                case BenchmarkType.Generic:
                    Generic(v);
                    break;
                case BenchmarkType.GenericDelta:
                    GenericDelta(v);
                    break;
                case BenchmarkType.Returnable:
                    for (var o = 0; o < observers.Count; o++)
                        _ = Returnable(observers[o], v);
                    break;
                case BenchmarkType.ReturnableDelta:
                    for (var o = 0; o < observers.Count; o++)
                        _ = ReturnableDelta(observers[o], v);
                    break;
                case BenchmarkType.GenericReturnable:
                    for (var o = 0; o < observers.Count; o++)
                        _ = ReturnableGeneric(observers[o], v);
                    break;
                case BenchmarkType.GenericReturnableDelta:
                    for (var o = 0; o < observers.Count; o++)
                        _ = ReturnableGenericDelta(observers[o], v);
                    break;
                case BenchmarkType.ServerNormal:
                    _testModule.NormalServer();
                    break;
                case BenchmarkType.ServerNormalDelta:
                    _testModule.NormalDeltaServer(v);
                    break;
                case BenchmarkType.ServerGeneric:
                    _testModule.GenericServer(v);
                    break;
                case BenchmarkType.ServerGenericDelta:
                    _testModule.GenericDeltaServer(v);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public override void OnBenchmarkStart()
    {
        _received = 0;
        _sent = 0;
    }

    [TargetRpc(deltaPacked: false)]
    Task<IntContainer> Returnable(PlayerID target, IntContainer value)
    {
        ++_received;
        return Task.FromResult(value);
    }

    [TargetRpc(deltaPacked: true)]
    Task<IntContainer> ReturnableDelta(PlayerID target, IntContainer value)
    {
        ++_received;
        return Task.FromResult(value);
    }

    [TargetRpc(deltaPacked: false)]
    Task<T> ReturnableGeneric<T>(PlayerID target, T value)
    {
        ++_received;
        return Task.FromResult(value);
    }

    [TargetRpc(deltaPacked: true)]
    Task<T> ReturnableGenericDelta<T>(PlayerID target, T value)
    {
        ++_received;
        return Task.FromResult(value);
    }

    [ObserversRpc(deltaPacked: false)]
    void Normal(IntContainer someRandomValue)
    {
        ++_received;
    }

    [ObserversRpc(deltaPacked: true)]
    void NormalDelta(IntContainer someRandomValue)
    {
        ++_received;
    }

    [ObserversRpc(deltaPacked: false)]
    void Generic<T>(T someRandomValue)
    {
        ++_received;
    }

    [ObserversRpc(deltaPacked: true)]
    void GenericDelta<T>(T someRandomValue)
    {
        ++_received;
    }

    // STATICS

    [TargetRpc(deltaPacked: false)]
    static Task<IntContainer> StaticReturnable(PlayerID target, IntContainer value)
    {
        ++_received;
        return Task.FromResult(value);
    }

    [TargetRpc(deltaPacked: true)]
    static Task<IntContainer> StaticReturnableDelta(PlayerID target, IntContainer value)
    {
        ++_received;
        return Task.FromResult(value);
    }

    [TargetRpc(deltaPacked: false)]
    static Task<T> StaticReturnableGeneric<T>(PlayerID target, T value)
    {
        ++_received;
        return Task.FromResult(value);
    }

    [TargetRpc(deltaPacked: true)]
    static Task<T>  StaticReturnableGenericDelta<T>(PlayerID target, T value)
    {
        ++_received;
        return Task.FromResult(value);
    }

    [ObserversRpc(deltaPacked: false)]
    static void StaticNormal(IntContainer someRandomValue)
    {
        ++_received;
    }

    [ObserversRpc(deltaPacked: true)]
    static void StaticNormalDelta(IntContainer someRandomValue)
    {
        ++_received;
    }

    [ObserversRpc(deltaPacked: false)]
    static void StaticGeneric<T>(T someRandomValue)
    {
        ++_received;
    }

    [ObserversRpc(deltaPacked: true)]
    static void StaticGenericDelta<T>(T someRandomValue)
    {
        ++_received;
    }
}
