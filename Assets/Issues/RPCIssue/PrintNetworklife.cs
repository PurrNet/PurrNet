using PurrNet;
using UnityEngine;

public class PrintNetworklife : NetworkIdentity, ITick
{
    [SerializeField] private bool _earlySpawnPrint = true;
    [SerializeField] private bool _spawnPrint = true;
    [SerializeField] private bool _ownerPrint = true;
    [SerializeField] private bool _onTick = true;

    protected override void OnEarlySpawn()
    {
        if (_earlySpawnPrint)
            Debug.Log($"OnEarlySpawn {this}", this);
    }

    protected override void OnEarlySpawn(bool asServer)
    {
        if (_earlySpawnPrint)
            Debug.Log($"OnEarlySpawn {this} {asServer}", this);
    }

    protected override void OnSpawned()
    {
        if (_spawnPrint)
            Debug.Log($"OnSpawned {this}", this);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (_spawnPrint)
            Debug.Log($"OnSpawned {this} {asServer}", this);
    }

    protected override void OnDespawned()
    {
        if (_spawnPrint)
            Debug.Log($"OnDespawned {this}", this);
    }

    protected override void OnDespawned(bool asServer)
    {
        if (_spawnPrint)
            Debug.Log($"OnDespawned {this} {asServer}", this);
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        if (_ownerPrint)
            Debug.Log($"OnOwnerChanged {this} {oldOwner} -> {newOwner} {asServer}", this);
    }

    public void OnTick(float delta)
    {
        if (_onTick)
            Debug.Log($"OnTick {this} {owner} {delta}", this);
    }

}
