using PurrNet;
using UnityEngine;

public class WhitelistVisibilityTests : MonoBehaviour
{
    [SerializeField] private NetworkIdentity _target;

    private void OnTriggerEnter(Collider other)
    {
        var go = other.gameObject;
        if (go.TryGetComponent<NetworkIdentity>(out var networkIdentity) && networkIdentity.owner.HasValue)
            OnEntered(networkIdentity.owner.Value);
    }

    private void OnTriggerExit(Collider other)
    {
        var go = other.gameObject;
        if (go.TryGetComponent<NetworkIdentity>(out var networkIdentity) && networkIdentity.owner.HasValue)
            OnExited(networkIdentity.owner.Value);
    }

    private void OnEntered(PlayerID player)
    {
        Debug.Log("Adding " + player);
        _target.WhitelistPlayer(player);
    }

    private void OnExited(PlayerID player)
    {
        Debug.Log("Removing " + player);
        _target.RemoveWhitelistPlayer(player);
    }
}
