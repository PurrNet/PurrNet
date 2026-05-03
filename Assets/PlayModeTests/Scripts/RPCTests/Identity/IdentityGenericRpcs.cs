using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Generic NetworkIdentity. Unity cannot AddComponent open-generic types, so we
// provide concrete sealed subclasses (IdentityGenericRpcsInt / IdentityGenericRpcsString)
// at the bottom — codegen still has to generate per-closed-type RPC entries.
public class IdentityGenericRpcs<T> : NetworkIdentity
{
    // Per closed-generic-type LocalInstance + DoneCount. Each instantiation has its own
    // static state (CLR semantics), which is exactly what we want for parity-with-Identity.
    public static IdentityGenericRpcs<T> LocalInstance;
    public static int DoneCount;

    [Serializable]
    public struct GenericPair<U>
    {
        public U first;
        public U second;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
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
        Debug.Log($"[IdentityGenericRpcs<{typeof(T).Name}>] SignalDone received from sender {info.sender.id.value} (total {DoneCount})");
    }
}

// Concrete closed-generic subclasses. These are the actual Unity-addable components.
public sealed class IdentityGenericRpcsInt : IdentityGenericRpcs<int> { }
public sealed class IdentityGenericRpcsString : IdentityGenericRpcs<string> { }
