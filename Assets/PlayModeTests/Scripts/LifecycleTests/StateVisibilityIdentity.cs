using PurrNet;
using UnityEngine;

public class StateVisibilityIdentity : NetworkIdentity
{
    public const int ServerSetValue = 4242;
    public const int InitialValue = 0;

    [SerializeField] private SyncVar<int> _value = new(InitialValue, sendIntervalInSeconds: 0f, ownerAuth: false);

    public static StateVisibilityIdentity LocalInstance;
    public static int ServerReadyCount;
    public static bool ValueAvailableInOnSpawned;
    public static int ValueObservedInOnSpawned;
    public static bool AsServerSnapshotMatched;
    public static int OnSpawnedCount;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ValueAvailableInOnSpawned = false;
        ValueObservedInOnSpawned = 0;
        AsServerSnapshotMatched = false;
        OnSpawnedCount = 0;
    }

    public int currentValue => _value.value;

    /// <summary>
    /// Server-only: set the SyncVar to a sentinel value before the prefab is spawned, so the
    /// initial spawn packet carries it. Validates that SyncVar values are visible inside
    /// OnSpawned on every receiving peer.
    /// </summary>
    public void SetSentinelValue()
    {
        _value.value = ServerSetValue;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        OnSpawnedCount++;
        ValueObservedInOnSpawned = _value.value;
        ValueAvailableInOnSpawned = _value.value == ServerSetValue;

        // On the server side, the snapshot must already match (since we set it just before spawn).
        if (asServer && _value.value == ServerSetValue)
            AsServerSnapshotMatched = true;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;
}
