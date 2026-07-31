using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Shared probe used by the server- and client-authoritative InstantiateAsync phases.
/// It deliberately changes state as soon as the caller's native operation finishes. Once a
/// receiver reports ready, the server sends normal state/RPC traffic before FinishSpawnPacket.
/// </summary>
public sealed class AsyncInstantiateProbe : NetworkIdentity
{
    [SerializeField] private SyncVar<int> _state = new(0, sendIntervalInSeconds: 0f, ownerAuth: true);

    // The native result can be used before PurrNet's delayed OnSpawned callback. This mirrors the
    // existing SpawnPacket lifecycle: identities are integrated early and FinishSpawnPacket is the
    // final callback boundary.
    private bool _isLocalOperationResult;
    private int _stateSeenOnSpawn;

    private static readonly HashSet<AsyncInstantiateProbe> _instances = new();
    private static readonly HashSet<int> _serverRpcTokens = new();
    private static readonly HashSet<int> _observerRpcTokens = new();
    private static readonly HashSet<int> _targetRpcTokens = new();
    private static readonly HashSet<int> _forwardedRpcTokens = new();
    private static readonly HashSet<int> _clientEchoTokens = new();
    private static readonly Dictionary<AsyncInstantiateProbe, int> _expectedState = new();
    private static readonly Dictionary<NetworkID, HashSet<ulong>> _observerAdds = new();
    private static readonly List<GameObject> _despawnedObjects = new();
    private static readonly HashSet<int> _poolResetInstanceIds = new();

    public static int aliveCount => _instances.Count;
    public static int serverRpcTokenCount => _serverRpcTokens.Count;
    public static int observerRpcTokenCount => _observerRpcTokens.Count;
    public static int targetRpcTokenCount => _targetRpcTokens.Count;
    public static int forwardedRpcTokenCount => _forwardedRpcTokens.Count;
    public static int clientEchoTokenCount => _clientEchoTokens.Count;
    public static int despawnedObjectCount => _despawnedObjects.Count;

    public static bool stateMissingAtSpawn { get; private set; }
    public static bool sawPooledAsyncInstance { get; private set; }
    public static bool reusedPooledInstance { get; private set; }

    public int currentState => _state.value;

    public static void ResetAll()
    {
        _instances.Clear();
        _poolResetInstanceIds.Clear();
        ResetCycle();
    }

    public static void ResetCycle()
    {
        _serverRpcTokens.Clear();
        _observerRpcTokens.Clear();
        _targetRpcTokens.Clear();
        _forwardedRpcTokens.Clear();
        _clientEchoTokens.Clear();
        _expectedState.Clear();
        _observerAdds.Clear();
        _despawnedObjects.Clear();
        stateMissingAtSpawn = false;
        sawPooledAsyncInstance = false;
        reusedPooledInstance = false;
    }

    public static AsyncInstantiateProbe[] SnapshotInstances()
    {
        var result = new AsyncInstantiateProbe[_instances.Count];
        _instances.CopyTo(result);
        return result;
    }

    public static bool AllInstancesAreSpawnedAndUnpooled(int expectedCount)
    {
        if (_instances.Count != expectedCount)
            return false;

        foreach (var instance in _instances)
        {
            if (!instance || !instance.isSpawned || !instance.id.HasValue || instance.shouldBePooled)
                return false;
        }

        return true;
    }

    public static bool AllInstancesOwnedBy(ulong expectedOwner)
    {
        foreach (var instance in _instances)
        {
            if (!instance || !instance.owner.HasValue || instance.owner.Value.id.value != expectedOwner)
                return false;
        }

        return true;
    }

    public static bool AllExpectedStatesApplied(int expectedCount)
    {
        if (_expectedState.Count != expectedCount)
            return false;

        foreach (var pair in _expectedState)
        {
            if (!pair.Key || pair.Key.currentState != pair.Value)
                return false;
        }

        return true;
    }

    public static int ObserverPairCount()
    {
        int count = 0;
        foreach (var pair in _observerAdds)
            count += pair.Value.Count;
        return count;
    }

    public static bool AllDespawnedObjectsDestroyed(int expectedCount)
    {
        if (_despawnedObjects.Count != expectedCount)
            return false;

        for (int i = 0; i < _despawnedObjects.Count; i++)
        {
            if (_despawnedObjects[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Server-authoritative immediate post-operation traffic.
    /// </summary>
    public void SetStateAndBroadcast(int token)
    {
        _isLocalOperationResult = true;
        _state.value = token;
    }

    /// <summary>
    /// Client-authoritative immediate post-operation traffic. The server echoes from inside
    /// the ServerRpc, exercising both client-to-server ordering and server-to-observer queuing.
    /// </summary>
    public void SetStateAndSignalServer(int token)
    {
        _isLocalOperationResult = true;
        _state.value = token;
        _state.FlushImmediately();
        ReceiveClientSpawnTraffic(token);
        ReceiveForwardedClientTraffic(token);
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        _stateSeenOnSpawn = _state.value;
        if (!_isLocalOperationResult &&
            (!_expectedState.TryGetValue(this, out var expected) || _stateSeenOnSpawn != expected))
            stateMissingAtSpawn = true;
        _instances.Add(this);
        if (shouldBePooled)
            sawPooledAsyncInstance = true;
        if (_poolResetInstanceIds.Contains(GetHashCode()))
            reusedPooledInstance = true;
    }

    protected override void OnObserverAdded(PlayerID player)
    {
        if (!id.HasValue)
            return;

        if (!_observerAdds.TryGetValue(id.Value, out var players))
        {
            players = new HashSet<ulong>();
            _observerAdds.Add(id.Value, players);
        }

        players.Add(player.id.value);

        // These use the ordinary observer/target paths after Ready promoted this player, and are
        // intentionally sent before FinishSpawnPacket just like bootstrap state on a normal spawn.
        ReceiveServerSpawnTraffic(_state.value);
        ReceiveTargetSpawnTraffic(player, _state.value);
    }

    protected override void OnDespawned()
    {
        // A host receives both server- and client-side callbacks for the same object.
        // HashSet.Remove keeps the live count stable; only retain the GameObject once.
        if (_instances.Remove(this))
            _despawnedObjects.Add(gameObject);
    }

    protected override void OnPoolReset()
    {
        _poolResetInstanceIds.Add(GetHashCode());
        base.OnPoolReset();
    }

    [ObserversRpc(runLocally: true)]
    private void ReceiveServerSpawnTraffic(int token)
    {
        _observerRpcTokens.Add(token);
        _expectedState[this] = token;
    }

    [ServerRpc(requireOwnership: false)]
    private void ReceiveClientSpawnTraffic(int token)
    {
        // The owner-auth SyncVar update is ordered before this RPC. Do not write it again on the
        // server: the server is not the owner, and doing so would only test a permission failure.
        _serverRpcTokens.Add(token);
        EchoClientSpawnTraffic(token);

        var players = networkManager.players;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer && !networkManager.isHost)
                continue;
            ReceiveTargetSpawnTraffic(player, token);
        }
    }

    [ObserversRpc(runLocally: true)]
    private void EchoClientSpawnTraffic(int token)
    {
        _clientEchoTokens.Add(token);
        _expectedState[this] = token;
    }

    [TargetRpc]
    private void ReceiveTargetSpawnTraffic(PlayerID target, int token)
    {
        _targetRpcTokens.Add(token);
    }

    [ObserversRpc(requireServer: false, runLocally: true)]
    private void ReceiveForwardedClientTraffic(int token)
    {
        _forwardedRpcTokens.Add(token);
    }
}

/// <summary>Separate type so cancellation can assert that no identity ever spawned.</summary>
public sealed class AsyncInstantiateCancellationIdentity : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _spawned = new();
    private NetworkID? _trackedId;
    private bool _trackedClone;

    public static int spawnedCount => _spawned.Count;
    public static int everSpawnedCount { get; private set; }
    public static int liveCloneCount { get; private set; }

    public static void ResetAll()
    {
        _spawned.Clear();
        everSpawnedCount = 0;
        liveCloneCount = 0;
    }

    private void Awake()
    {
        if (!gameObject.name.EndsWith("(Clone)", System.StringComparison.Ordinal))
            return;
        _trackedClone = true;
        liveCloneCount++;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned()
    {
        if (!id.HasValue)
            return;

        _trackedId = id.Value;
        _spawned.Add(id.Value);
        everSpawnedCount++;
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            _spawned.Remove(_trackedId.Value);
        _trackedId = null;
    }

    protected override void OnDestroy()
    {
        if (_trackedClone)
            liveCloneCount--;
        _trackedClone = false;
        base.OnDestroy();
    }
}
