using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-auth <see cref="SyncVar{T}"/> whose value is assigned on the spawning client BEFORE
/// Spawn is called. Every other peer must still converge to the pre-spawn value instead of the
/// prefab default.
/// </summary>
public class SyncVarPreSpawnSetIdentity : NetworkIdentity
{
    public const int payloadValue = 777;

    [SerializeField] private SyncVar<int> _value = new(0, ownerAuth: true);

    public static SyncVarPreSpawnSetIdentity localInstance;

    public static void ResetAll()
    {
        localInstance = null;
    }

    public int currentValue => _value.value;

    public void SetPayload(int value) => _value.value = value;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => localInstance = this;
}
