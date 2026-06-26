using System.Collections.Generic;
using System.Reflection;
using System.Text;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative collection SyncTypes driven through observer removal and re-add. The
/// re-added observer must be seeded from live collection state, not editor serialization mirrors.
/// </summary>
public class SyncCollectionsObserverReaddIdentity : NetworkIdentity
{
    [SerializeField] private SyncList<int> _list = new(ownerAuth: false);
    [SerializeField] private SyncDictionary<int, int> _dict = new(ownerAuth: false);
    [SerializeField] private SyncHashSet<int> _set = new(ownerAuth: false);
    [SerializeField] private SyncArray<int> _array = new(length: 0, ownerAuth: false);
    [SerializeField] private SyncQueue<int> _queue = new(ownerAuth: false);

    public static SyncCollectionsObserverReaddIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerSeedConvergedCount;
    public static int ServerDoneCount;
    public static ulong VictimPlayerId;
    public static bool VictimIdReceived;
    public static readonly List<ulong> RemovedObservers = new();

    private static readonly int[] SeedList = { 10, 11 };
    private static readonly int[] SeedSet = { 30, 31 };
    private static readonly int[] SeedArray = { 40, 41 };
    private static readonly int[] SeedQueue = { 50, 51 };
    private static readonly int[] FinalList = { 101, 102, 103 };
    private static readonly int[] FinalSet = { 301, 302 };
    private static readonly int[] FinalArray = { 401, 402, 403 };
    private static readonly int[] FinalQueue = { 501, 502 };

    private bool _listenersRegistered;
    private int _listCatchupChangeCount;
    private int _dictCatchupChangeCount;
    private int _setCatchupChangeCount;
    private int _arrayCatchupChangeCount;
    private int _queueCatchupChangeCount;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerSeedConvergedCount = 0;
        ServerDoneCount = 0;
        VictimPlayerId = 0;
        VictimIdReceived = false;
        RemovedObservers.Clear();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
        ResetCatchupChangeCounters();
        RegisterListenersOnce();
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;

        if (asServer)
            RunSeedState();
    }

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;

        UnregisterListeners();
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        RemovedObservers.Add(player.id.value);
    }

    public void RunSeedState()
    {
        _list.Clear();
        _list.Add(10);
        _list.Add(11);

        _dict.Clear();
        _dict.Add(1, 10);
        _dict.Add(2, 20);

        _set.Clear();
        _set.Add(30);
        _set.Add(31);

        _array.Length = 2;
        _array[0] = 40;
        _array[1] = 41;

        _queue.Clear();
        _queue.Enqueue(50);
        _queue.Enqueue(51);
    }

    public void RunFinalState()
    {
        _list.Clear();
        _list.Add(101);
        _list.Add(102);
        _list.Add(103);

        _dict.Clear();
        _dict.Add(101, 201);
        _dict.Add(102, 202);

        _set.Clear();
        _set.Add(301);
        _set.Add(302);

        _array.Length = 3;
        _array[0] = 401;
        _array[1] = 402;
        _array[2] = 403;

        _queue.Clear();
        _queue.Enqueue(501);
        _queue.Enqueue(502);
    }

    public void SimulateStaleSerializedHashSetMirror()
    {
        // Editor play mode keeps this mirror current; clear it to exercise player-build semantics.
        var serializedSetField = typeof(SyncHashSet<int>).GetField(
            "_serializedSet",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (serializedSetField?.GetValue(_set) is List<int> serializedSet)
            serializedSet.Clear();
    }

    public bool MatchesSeedState()
    {
        return SequenceEquals(_list.list, SeedList)
               && DictEquals(new[] { 1, 2 }, new[] { 10, 20 })
               && SetEquals(SeedSet)
               && ArrayEquals(SeedArray)
               && QueueEquals(SeedQueue);
    }

    public bool MatchesFinalState()
    {
        return SequenceEquals(_list.list, FinalList)
               && DictEquals(new[] { 101, 102 }, new[] { 201, 202 })
               && SetEquals(FinalSet)
               && ArrayEquals(FinalArray)
               && QueueEquals(FinalQueue);
    }

    public bool SawVictimCatchupChangesForEveryCollection()
    {
        return _listCatchupChangeCount > 0
               && _dictCatchupChangeCount > 0
               && _setCatchupChangeCount > 0
               && _arrayCatchupChangeCount > 0
               && _queueCatchupChangeCount > 0;
    }

    public string DescribeCatchupChanges()
    {
        return $"callbacks=list:{_listCatchupChangeCount}, dict:{_dictCatchupChangeCount}, " +
               $"set:{_setCatchupChangeCount}, array:{_arrayCatchupChangeCount}, queue:{_queueCatchupChangeCount}";
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("list=").Append(DescribeList(_list.list));
        sb.Append(", dict=").Append(DescribeDict());
        sb.Append(", set=").Append(DescribeSet());
        sb.Append(", array=").Append(DescribeArray());
        sb.Append(", queue=").Append(DescribeQueue());
        return sb.ToString();
    }

    private void RegisterListenersOnce()
    {
        if (_listenersRegistered)
            return;

        _listenersRegistered = true;
        _list.onChanged += OnListChanged;
        _dict.onChanged += OnDictChanged;
        _set.onChanged += OnSetChanged;
        _array.onChanged += OnArrayChanged;
        _queue.onChanged += OnQueueChanged;
    }

    private void UnregisterListeners()
    {
        if (!_listenersRegistered)
            return;

        _listenersRegistered = false;
        _list.onChanged -= OnListChanged;
        _dict.onChanged -= OnDictChanged;
        _set.onChanged -= OnSetChanged;
        _array.onChanged -= OnArrayChanged;
        _queue.onChanged -= OnQueueChanged;
    }

    private void ResetCatchupChangeCounters()
    {
        _listCatchupChangeCount = 0;
        _dictCatchupChangeCount = 0;
        _setCatchupChangeCount = 0;
        _arrayCatchupChangeCount = 0;
        _queueCatchupChangeCount = 0;
    }

    private void OnListChanged(SyncListChange<int> _) => TrackVictimCatchupChange(ref _listCatchupChangeCount);

    private void OnDictChanged(SyncDictionaryChange<int, int> _) => TrackVictimCatchupChange(ref _dictCatchupChangeCount);

    private void OnSetChanged(SyncHashSetChange<int> _) => TrackVictimCatchupChange(ref _setCatchupChangeCount);

    private void OnArrayChanged(SyncArrayChange<int> _) => TrackVictimCatchupChange(ref _arrayCatchupChangeCount);

    private void OnQueueChanged(SyncQueueChange<int> _) => TrackVictimCatchupChange(ref _queueCatchupChangeCount);

    private void TrackVictimCatchupChange(ref int count)
    {
        if (ShouldTrackVictimCatchupChanges())
            count++;
    }

    private bool ShouldTrackVictimCatchupChanges()
    {
        return VictimIdReceived
               && networkManager
               && networkManager.isLocalPlayerReady
               && networkManager.localPlayer.id.value == VictimPlayerId;
    }

    private static bool SequenceEquals(IReadOnlyList<int> actual, int[] expected)
    {
        if (actual.Count != expected.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
                return false;
        }

        return true;
    }

    private bool DictEquals(int[] keys, int[] values)
    {
        if (_dict.Count != keys.Length)
            return false;

        for (int i = 0; i < keys.Length; i++)
        {
            if (!_dict.TryGetValue(keys[i], out var value) || value != values[i])
                return false;
        }

        return true;
    }

    private bool SetEquals(int[] expected)
    {
        if (_set.Count != expected.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (!_set.Contains(expected[i]))
                return false;
        }

        return true;
    }

    private bool ArrayEquals(int[] expected)
    {
        if (_array.Count != expected.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (_array[i] != expected[i])
                return false;
        }

        return true;
    }

    private bool QueueEquals(int[] expected)
    {
        if (_queue.Count != expected.Length)
            return false;

        int i = 0;
        foreach (var value in _queue)
        {
            if (value != expected[i])
                return false;

            i++;
        }

        return true;
    }

    private static string DescribeList(IReadOnlyList<int> values)
    {
        var items = new string[values.Count];
        for (int i = 0; i < values.Count; i++)
            items[i] = values[i].ToString();
        return $"[{string.Join(",", items)}]";
    }

    private string DescribeDict()
    {
        var items = new List<string>();
        foreach (var kv in _dict)
            items.Add($"{kv.Key}:{kv.Value}");
        items.Sort();
        return $"{{{string.Join(",", items)}}}";
    }

    private string DescribeSet()
    {
        var items = new List<int>();
        foreach (var value in _set)
            items.Add(value);
        items.Sort();
        return $"{{{string.Join(",", items)}}}";
    }

    private string DescribeArray()
    {
        var items = new string[_array.Count];
        for (int i = 0; i < _array.Count; i++)
            items[i] = _array[i].ToString();
        return $"[{string.Join(",", items)}]";
    }

    private string DescribeQueue()
    {
        var items = new List<int>();
        foreach (var value in _queue)
            items.Add(value);
        return $"[{string.Join(",", items)}]";
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimPlayerId = victimId;
        VictimIdReceived = true;

        if (LocalInstance)
            LocalInstance.ResetCatchupChangeCounters();
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalSeedConverged(RPCInfo info = default) => ServerSeedConvergedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;
}
