using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class SinglePromotedPlayerPrefabChild : NetworkIdentity
{
    private static readonly HashSet<GlobalNetworkID> _serverAlive = new();
    private static readonly HashSet<GlobalNetworkID> _clientAlive = new();
    private static readonly Dictionary<ulong, HashSet<GlobalNetworkID>> _serverByOwner = new();
    private static readonly Dictionary<ulong, HashSet<GlobalNetworkID>> _clientByOwner = new();

    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static bool SawBadId;
    public static string ServerOwners => FormatOwners(_serverByOwner);
    public static string ClientOwners => FormatOwners(_clientByOwner);

    private GlobalNetworkID? _serverTrackedId;
    private GlobalNetworkID? _clientTrackedId;
    private ulong? _serverTrackedOwner;
    private ulong? _clientTrackedOwner;

    public static void ResetAll()
    {
        _serverAlive.Clear();
        _clientAlive.Clear();
        _serverByOwner.Clear();
        _clientByOwner.Clear();
        SawBadId = false;
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!id.HasValue || id.Value == default)
        {
            SawBadId = true;
            return;
        }

        var globalId = new GlobalNetworkID(sceneId, id.Value);
        if (asServer)
        {
            _serverTrackedId = globalId;
            _serverAlive.Add(globalId);
        }
        else
        {
            _clientTrackedId = globalId;
            _clientAlive.Add(globalId);
        }

        TrackOwner(asServer, owner);
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
        {
            UntrackOwner(true);
            if (_serverTrackedId.HasValue)
                _serverAlive.Remove(_serverTrackedId.Value);
            _serverTrackedId = null;
            return;
        }

        UntrackOwner(false);
        if (_clientTrackedId.HasValue)
            _clientAlive.Remove(_clientTrackedId.Value);
        _clientTrackedId = null;
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        TrackOwner(asServer, newOwner);
    }

    public static int ServerOwnerCount(PlayerID player) => OwnerCount(_serverByOwner, player);

    public static int ClientOwnerCount(PlayerID player) => OwnerCount(_clientByOwner, player);

    public static bool ServerHasDuplicateOwner()
    {
        foreach (var pair in _serverByOwner)
        {
            if (pair.Value.Count > 1)
                return true;
        }

        return false;
    }

    private void TrackOwner(bool asServer, PlayerID? newOwner)
    {
        if (asServer && !_serverTrackedId.HasValue)
            return;
        if (!asServer && !_clientTrackedId.HasValue)
            return;

        UntrackOwner(asServer);
        if (!newOwner.HasValue)
            return;

        var ownerId = newOwner.Value.id.value;
        var idToTrack = asServer ? _serverTrackedId.Value : _clientTrackedId.Value;
        var map = asServer ? _serverByOwner : _clientByOwner;
        if (!map.TryGetValue(ownerId, out var identities))
        {
            identities = new HashSet<GlobalNetworkID>();
            map[ownerId] = identities;
        }

        identities.Add(idToTrack);
        if (asServer)
            _serverTrackedOwner = ownerId;
        else
            _clientTrackedOwner = ownerId;
    }

    private void UntrackOwner(bool asServer)
    {
        var owner = asServer ? _serverTrackedOwner : _clientTrackedOwner;
        var trackedId = asServer ? _serverTrackedId : _clientTrackedId;
        if (!owner.HasValue || !trackedId.HasValue)
            return;

        var map = asServer ? _serverByOwner : _clientByOwner;
        if (map.TryGetValue(owner.Value, out var identities))
        {
            identities.Remove(trackedId.Value);
            if (identities.Count == 0)
                map.Remove(owner.Value);
        }

        if (asServer)
            _serverTrackedOwner = null;
        else
            _clientTrackedOwner = null;
    }

    private static int OwnerCount(Dictionary<ulong, HashSet<GlobalNetworkID>> owners, PlayerID player)
    {
        return owners.TryGetValue(player.id.value, out var identities) ? identities.Count : 0;
    }

    private static string FormatOwners(Dictionary<ulong, HashSet<GlobalNetworkID>> owners)
    {
        var parts = new List<string>(owners.Count);
        foreach (var pair in owners)
            parts.Add($"{pair.Key}:{pair.Value.Count}");
        return string.Join(",", parts);
    }
}
