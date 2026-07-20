using System.Collections.Generic;
using PurrNet;

/// <summary>
/// LOD-governed identity for NetworkLODTierScenario; records server-side tier changes per player.
/// </summary>
public class NetworkLODTierTarget : NetworkIdentity
{
    public static NetworkLODTierTarget localInstance;

    static readonly Dictionary<PlayerID, int> _virtualCounts = new Dictionary<PlayerID, int>();
    static readonly Dictionary<PlayerID, int> _eventCounts = new Dictionary<PlayerID, int>();
    static readonly Dictionary<PlayerID, (byte previous, byte next)> _lastChange =
        new Dictionary<PlayerID, (byte, byte)>();

    public static void ResetAll()
    {
        localInstance = null;
        _virtualCounts.Clear();
        _eventCounts.Clear();
        _lastChange.Clear();
    }

    public static int GetVirtualCount(PlayerID player) =>
        _virtualCounts.TryGetValue(player, out var c) ? c : 0;

    public static int GetEventCount(PlayerID player) =>
        _eventCounts.TryGetValue(player, out var c) ? c : 0;

    public static (byte previous, byte next)? GetLastChange(PlayerID player) =>
        _lastChange.TryGetValue(player, out var c) ? c : ((byte, byte)?)null;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned()
    {
        localInstance = this;
        onLODTierChanged += OnTierEvent;
    }

    protected override void OnDespawned()
    {
        onLODTierChanged -= OnTierEvent;
        if (localInstance == this)
            localInstance = null;
    }

    private static void OnTierEvent(PlayerID player, byte previousTier, byte newTier)
    {
        _eventCounts[player] = GetEventCount(player) + 1;
    }

    protected override void OnLODTierChanged(PlayerID player, byte previousTier, byte newTier)
    {
        _virtualCounts[player] = GetVirtualCount(player) + 1;
        _lastChange[player] = (previousTier, newTier);
    }
}
