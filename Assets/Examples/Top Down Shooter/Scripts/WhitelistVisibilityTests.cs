using PurrNet;
using UnityEngine;

public class WhitelistVisibilityTests : MonoBehaviour
{
    [SerializeField] private NetworkIdentity _target;

    private void OnTriggerEnter(Collider other)
    {
        var go = other.gameObject;
        if (go.TryGetComponent<NetworkIdentity>(out var networkIdentity) && networkIdentity.owner.HasValue)
        {
            if (networkIdentity.isServer)
                OnEntered(networkIdentity.owner.Value);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var go = other.gameObject;
        if (go.TryGetComponent<NetworkIdentity>(out var networkIdentity) && networkIdentity.owner.HasValue)
        {
            if (networkIdentity.isServer)
                OnExited(networkIdentity.owner.Value);
        }
    }

    private void OnEntered(PlayerID player)
    {
        _target.WhitelistPlayer(player);
    }

    private void OnExited(PlayerID player)
    {
        _target.RemoveWhitelistPlayer(player);
    }
}
