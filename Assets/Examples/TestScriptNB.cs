using JetBrains.Annotations;
using PurrNet;
using PurrNet.Logging;
using PurrNet.Packing;
using UnityEngine;

namespace System.Runtime.CompilerServices
{
    [UsedImplicitly]
    internal static class IsExternalInit { }
}

public record PickedUpEvent(string ItemName, int Quantity) : IPackedAuto;

public class TestScriptNB : NetworkIdentity
{
    [SerializeField] GameObject prefab;
    [SerializeField] Transform p_parent;
    [SerializeField] private Vector3 pos;
    [SerializeField] Vector3 rot;

    protected override void OnSpawned()
    {
        if (isOwner)
        {
            UnityProxy.Instantiate(prefab, pos, Quaternion.Euler(rot), p_parent);
        }
    }

    [PurrButton]
    public void ServerRpcTesTButton()
    {
        ServerRpcTesT(new PickedUpEvent("TestItem", 1));
    }

    [PurrButton]
    public void WhitelistAllPlayers()
    {
        foreach (var p in networkManager.players)
        {
            if (!WhitelistPlayer(p))
                PurrLogger.LogError($"Failed to whitelist player {p} on {this}");
        }
    }

    [ServerRpc]
    public void ServerRpcTesT(PickedUpEvent f, RPCInfo info = default)
    {
        PurrLogger.Log($"{f.ItemName} {f.Quantity} received from {info.sender} {isClient}");
        PurrLogger.Log($"ServerRpcTesT called from {info.sender} {isClient}");
        TargetRpcT(info.sender);
    }

    [TargetRpc]
    public void TargetRpcT(PlayerID target)
    {
        PurrLogger.Log($"TargetRpcT called on target {target}");
    }
}
