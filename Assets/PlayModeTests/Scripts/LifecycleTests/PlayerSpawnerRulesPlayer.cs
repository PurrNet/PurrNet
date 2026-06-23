using System.Collections.Generic;
using PurrNet;

public class PlayerSpawnerRulesPlayer : NetworkIdentity
{
    private static readonly HashSet<NetworkID> Alive = new();
    private static readonly Dictionary<ulong, HashSet<NetworkID>> ServerOwnerIds = new();
    private static readonly Dictionary<ulong, HashSet<NetworkID>> ClientOwnerIds = new();

    private NetworkID? _trackedId;
    private ulong? _serverOwner;
    private ulong? _clientOwner;

    public static int AliveCount => Alive.Count;
    public static bool SawBadId;
    public static ulong TargetPlayer;
    public static bool TargetPlayerReceived;
    public static bool DuplicateCheckStarted;

    public static string ServerOwners => FormatOwners(ServerOwnerIds);
    public static string ClientOwners => FormatOwners(ClientOwnerIds);

    public static void ResetAll()
    {
        Alive.Clear();
        ServerOwnerIds.Clear();
        ClientOwnerIds.Clear();
        SawBadId = false;
        TargetPlayer = 0;
        TargetPlayerReceived = false;
        DuplicateCheckStarted = false;
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

        _trackedId = id.Value;
        Alive.Add(id.Value);

        if (owner.HasValue)
            TrackOwner(owner.Value, asServer);
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            Alive.Remove(_trackedId.Value);

        RemoveOwner(ServerOwnerIds, _serverOwner, _trackedId);
        RemoveOwner(ClientOwnerIds, _clientOwner, _trackedId);
        _trackedId = null;
        _serverOwner = null;
        _clientOwner = null;
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        if (!_trackedId.HasValue || !newOwner.HasValue)
            return;

        TrackOwner(newOwner.Value, asServer);
    }

    private void TrackOwner(PlayerID newOwner, bool asServer)
    {
        if (!_trackedId.HasValue)
            return;

        if (asServer)
        {
            RemoveOwner(ServerOwnerIds, _serverOwner, _trackedId);
            AddOwner(ServerOwnerIds, newOwner.id.value, _trackedId.Value);
            _serverOwner = newOwner.id.value;
        }
        else
        {
            RemoveOwner(ClientOwnerIds, _clientOwner, _trackedId);
            AddOwner(ClientOwnerIds, newOwner.id.value, _trackedId.Value);
            _clientOwner = newOwner.id.value;
        }
    }

    public static int ServerOwnerCount(ulong playerId)
    {
        return ServerOwnerIds.TryGetValue(playerId, out var identities) ? identities.Count : 0;
    }

    public static int ClientOwnerCount(ulong playerId)
    {
        return ClientOwnerIds.TryGetValue(playerId, out var identities) ? identities.Count : 0;
    }

    private static void AddOwner(Dictionary<ulong, HashSet<NetworkID>> owners, ulong owner, NetworkID id)
    {
        if (!owners.TryGetValue(owner, out var identities))
        {
            identities = new HashSet<NetworkID>();
            owners[owner] = identities;
        }

        identities.Add(id);
    }

    private static void RemoveOwner(Dictionary<ulong, HashSet<NetworkID>> owners, ulong? owner, NetworkID? id)
    {
        if (!owner.HasValue || !id.HasValue)
            return;

        if (!owners.TryGetValue(owner.Value, out var identities))
            return;

        identities.Remove(id.Value);
        if (identities.Count == 0)
            owners.Remove(owner.Value);
    }

    private static string FormatOwners(Dictionary<ulong, HashSet<NetworkID>> owners)
    {
        var parts = new List<string>(owners.Count);
        foreach (var pair in owners)
            parts.Add($"{pair.Key}:{pair.Value.Count}");
        return string.Join(",", parts);
    }
}
