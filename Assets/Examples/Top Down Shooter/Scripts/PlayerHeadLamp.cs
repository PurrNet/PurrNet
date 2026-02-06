using PurrNet;
using UnityEngine;

public class PlayerHeadLamp : NetworkIdentity
{
    private readonly SyncList<int> _playerStats = new SyncList<int>();
    public SyncVar<bool> _lampIsOn = new SyncVar<bool>(true, ownerAuth: true);

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
        {
            _playerStats.Clear();
            _playerStats.Add(1);
        }
    }

    protected override void OnSpawned()
    {
        if (isOwner)
        {
            _lampIsOn.value = false;
        }
    }

    [PurrButton]
    public void ToggleLamp()
    {
        _lampIsOn.value = !_lampIsOn.value;
    }
}
