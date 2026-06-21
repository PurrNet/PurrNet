using System.Collections.Generic;
using System.Reflection;
using PurrNet;
using UnityEngine;

public class SinglePromotedPlayerPrefabRoot : PlayerIdentity<SinglePromotedPlayerPrefabRoot>
{
    private static readonly HashSet<SinglePromotedPlayerPrefabRoot> _serverAlive = new();
    private static readonly HashSet<SinglePromotedPlayerPrefabRoot> _clientAlive = new();
    private static readonly Dictionary<ulong, HashSet<SinglePromotedPlayerPrefabRoot>> _serverByOwner = new();
    private static readonly Dictionary<ulong, HashSet<SinglePromotedPlayerPrefabRoot>> _clientByOwner = new();
    private static readonly MethodInfo PlayerIdentityInit =
        typeof(PlayerIdentity<SinglePromotedPlayerPrefabRoot>).GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Static);

    public static SinglePromotedPlayerPrefabRoot LocalClientInstance;
    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static bool SawBadId;
    public static string ServerOwners => FormatOwners(_serverByOwner);
    public static string ClientOwners => FormatOwners(_clientByOwner);

    private ulong? _serverTrackedOwner;
    private ulong? _clientTrackedOwner;

    public static void ResetAll()
    {
        PlayerIdentityInit?.Invoke(null, null);
        _serverAlive.Clear();
        _clientAlive.Clear();
        _serverByOwner.Clear();
        _clientByOwner.Clear();
        LocalClientInstance = null;
        SawBadId = false;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!id.HasValue || id.Value == default)
        {
            SawBadId = true;
            return;
        }

        if (asServer)
        {
            _serverAlive.Add(this);
        }
        else
        {
            _clientAlive.Add(this);
            if (isOwner)
                LocalClientInstance = this;
        }

        TrackOwner(asServer, owner);
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
        {
            UntrackOwner(true);
            _serverAlive.Remove(this);
            return;
        }

        UntrackOwner(false);
        _clientAlive.Remove(this);
        if (LocalClientInstance == this)
            LocalClientInstance = null;
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        base.OnOwnerChanged(oldOwner, newOwner, asServer);
        TrackOwner(asServer, newOwner);
        if (!asServer)
            LocalClientInstance = isOwner ? this : LocalClientInstance == this ? null : LocalClientInstance;
    }

    public static int ServerOwnerCount(PlayerID player) => OwnerCount(_serverByOwner, player);

    public static bool ServerHasDuplicateOwner()
    {
        foreach (var pair in _serverByOwner)
        {
            if (pair.Value.Count > 1)
                return true;
        }

        return false;
    }

    public static bool HasCorrectLocalPlayer(NetworkManager manager)
    {
        if (!manager || !manager.isClient || !manager.isLocalPlayerReady)
            return false;

        if (!TryGetLocal(out var local) || !local)
            return false;

        if (LocalClientInstance != local)
            return false;

        var player = manager.localPlayer;
        if (!local.owner.HasValue || local.owner.Value != player)
            return false;

        if (!local.isOwner || !local.isController || !local.hasConnectedOwner)
            return false;

        if (!TryGetPlayer(player, out var byPlayer) || byPlayer != local)
            return false;

        if (OwnerCount(_clientByOwner, player) != 1)
            return false;

        var child = local.GetComponentInChildren<SinglePromotedPlayerPrefabChild>(true);
        return child
               && child.owner.HasValue
               && child.owner.Value == player
               && child.isOwner
               && child.isController
               && child.hasConnectedOwner
               && SinglePromotedPlayerPrefabChild.ClientOwnerCount(player) == 1;
    }

    public static string DescribeLocal(NetworkManager manager)
    {
        var local = LocalClientInstance;
        string localPlayer = manager && manager.isLocalPlayerReady ? manager.localPlayer.id.value.ToString() : "<none>";
        string ownerId = local && local.owner.HasValue ? local.owner.Value.id.value.ToString() : "<none>";
        return $"localPlayer={localPlayer}, owner={ownerId}, isOwner={local && local.isOwner}, " +
               $"controller={local && local.isController}, connectedOwner={local && local.hasConnectedOwner}, " +
               $"tryLocal={TryGetLocal(out var tryLocal) && tryLocal}, sameTryLocal={local && tryLocal == local}, " +
               $"clientOwners=[{ClientOwners}]";
    }

    private void TrackOwner(bool asServer, PlayerID? newOwner)
    {
        UntrackOwner(asServer);
        if (!newOwner.HasValue)
            return;

        var ownerId = newOwner.Value.id.value;
        var map = asServer ? _serverByOwner : _clientByOwner;
        if (!map.TryGetValue(ownerId, out var identities))
        {
            identities = new HashSet<SinglePromotedPlayerPrefabRoot>();
            map[ownerId] = identities;
        }

        identities.Add(this);
        if (asServer)
            _serverTrackedOwner = ownerId;
        else
            _clientTrackedOwner = ownerId;
    }

    private void UntrackOwner(bool asServer)
    {
        var owner = asServer ? _serverTrackedOwner : _clientTrackedOwner;
        if (!owner.HasValue)
            return;

        var map = asServer ? _serverByOwner : _clientByOwner;
        if (map.TryGetValue(owner.Value, out var identities))
        {
            identities.Remove(this);
            if (identities.Count == 0)
                map.Remove(owner.Value);
        }

        if (asServer)
            _serverTrackedOwner = null;
        else
            _clientTrackedOwner = null;
    }

    private static int OwnerCount(Dictionary<ulong, HashSet<SinglePromotedPlayerPrefabRoot>> owners, PlayerID player)
    {
        return owners.TryGetValue(player.id.value, out var identities) ? identities.Count : 0;
    }

    private static string FormatOwners(Dictionary<ulong, HashSet<SinglePromotedPlayerPrefabRoot>> owners)
    {
        var parts = new List<string>(owners.Count);
        foreach (var pair in owners)
            parts.Add($"{pair.Key}:{pair.Value.Count}");
        return string.Join(",", parts);
    }
}
