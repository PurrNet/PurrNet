using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Generic NetworkModule. Two closed instantiations live side by side on the same
// host NetworkIdentity (ModuleGenericRpcs below), exercising codegen for multiple
// closed types of the same module template.
public class ModuleGenericRpcsModule<T> : NetworkModule
{
    // Per closed-generic-type counter for SignalDone.
    public static int DoneCount;

    [Serializable]
    public struct GenericPair<U>
    {
        public U first;
        public U second;
    }

    // Class-level T used directly as RPC parameter and return type.
    [ServerRpc(requireOwnership: false)]
    public Task<T> Echo_T(T value, RPCInfo info = default) => Task.FromResult(value);

    // Generic struct closed over the class T.
    [ServerRpc(requireOwnership: false)]
    public Task<GenericPair<T>> Echo_PairOfT(GenericPair<T> p, RPCInfo info = default) =>
        Task.FromResult(p);

    // List<T> closed over the class T.
    [ServerRpc(requireOwnership: false)]
    public Task<int> Echo_ListOfTCount(List<T> list, RPCInfo info = default) =>
        Task.FromResult(list?.Count ?? 0);

    // Multi-parameter generic method (independent of class T).
    [ServerRpc(requireOwnership: false)]
    public Task<string> Echo_Two<T1, T2>(T1 a, T2 b, RPCInfo info = default) =>
        Task.FromResult($"{a}|{b}");

    [ServerRpc(requireOwnership: false)]
    public Task<string> Echo_Three<T1, T2, T3>(T1 a, T2 b, T3 c, RPCInfo info = default) =>
        Task.FromResult($"{a}|{b}|{c}");

    // Mixes class-level T with a method-level generic parameter.
    [ServerRpc(requireOwnership: false)]
    public Task<string> Echo_Mixed<U>(T classT, U methodT, RPCInfo info = default) =>
        Task.FromResult($"{classT}|{methodT}");

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default)
    {
        DoneCount++;
    }
}

// Host NetworkIdentity that owns both closed-generic modules.
public class ModuleGenericRpcs : NetworkIdentity
{
    public static ModuleGenericRpcs LocalInstance;

    public readonly ModuleGenericRpcsModule<int> intModule = new();
    public readonly ModuleGenericRpcsModule<string> stringModule = new();

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }
}
