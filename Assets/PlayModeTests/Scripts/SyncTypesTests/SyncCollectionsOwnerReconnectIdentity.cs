using System.Collections.Generic;
using System.Text;
using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative collection SyncTypes driven through an owner reconnect. The reconnecting
/// owner mutates the fresh local copy immediately after it is spawned again; every peer must still
/// converge to the post-reconnect state.
/// </summary>
public class SyncCollectionsOwnerReconnectIdentity : NetworkIdentity
{
    [SerializeField] private SyncList<int> _list = new(ownerAuth: true);
    [SerializeField] private SyncDictionary<int, int> _dict = new(ownerAuth: true);
    [SerializeField] private SyncHashSet<int> _set = new(ownerAuth: true);
    [SerializeField] private SyncArray<int> _array = new(length: 0, ownerAuth: true);
    [SerializeField] private SyncQueue<int> _queue = new(ownerAuth: true);

    public static SyncCollectionsOwnerReconnectIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerPreConvergedCount;
    public static int ServerDoneCount;
    public static int VictimReturnedCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool DisconnectCommandReceived;
    public static bool PhaseDoneReceived;
    public static bool RunFinalOnNextOwnerSpawn;
    public static bool FinalRanAfterReconnect;
    public static readonly List<ulong> DisconnectCalls = new();
    public static readonly List<ulong> ReconnectCalls = new();
    public static bool DisconnectCacheWrong;
    public static bool ReconnectCacheWrong;

    private static readonly int[] PreList = { 10, 11, 12, 13 };
    private static readonly int[] PreSet = { 1, 3 };
    private static readonly int[] PreArray = { 21, 22, 23 };
    private static readonly int[] PreQueue = { 32, 33 };
    private static readonly int[] FinalList = { 101, 102, 103 };
    private static readonly int[] FinalSet = { 101, 102 };
    private static readonly int[] FinalArray = { 201, 202 };
    private static readonly int[] FinalQueue = { 301, 302 };

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerPreConvergedCount = 0;
        ServerDoneCount = 0;
        VictimReturnedCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        DisconnectCommandReceived = false;
        PhaseDoneReceived = false;
        RunFinalOnNextOwnerSpawn = false;
        FinalRanAfterReconnect = false;
        DisconnectCalls.Clear();
        ReconnectCalls.Clear();
        DisconnectCacheWrong = false;
        ReconnectCacheWrong = false;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnOwnerDisconnected(PlayerID ownerId)
    {
        DisconnectCalls.Add(ownerId.id.value);
        if (hasConnectedOwner)
            DisconnectCacheWrong = true;
    }

    protected override void OnOwnerReconnected(PlayerID ownerId)
    {
        ReconnectCalls.Add(ownerId.id.value);
        if (!hasConnectedOwner)
            ReconnectCacheWrong = true;
    }

    private void Update()
    {
        TryRunFinalAfterReconnect();
    }

    public void RunPreReconnectState()
    {
        _list.Clear();
        _list.Add(10);
        _list.Add(12);
        _list.Insert(1, 11);
        _list.Add(99);
        _list.Remove(99);
        _list.Add(13);

        _dict.Clear();
        _dict.Add(1, 10);
        _dict.Add(2, 20);
        _dict[2] = 22;
        _dict.Add(3, 30);
        _dict.Remove(3);

        _set.Clear();
        _set.Add(1);
        _set.Add(2);
        _set.Add(3);
        _set.Remove(2);

        _array.Length = 3;
        _array[0] = 21;
        _array[1] = 22;
        _array[2] = 23;

        _queue.Clear();
        _queue.Enqueue(31);
        _queue.Enqueue(32);
        _queue.Enqueue(33);
        _queue.Dequeue();
    }

    public void RunPostReconnectState()
    {
        _list.Clear();
        _list.Add(101);
        _list.Add(102);
        _list.Add(103);

        _dict.Clear();
        _dict.Add(101, 201);
        _dict.Add(102, 202);

        _set.Clear();
        _set.Add(101);
        _set.Add(102);

        _array.Length = 2;
        _array[0] = 201;
        _array[1] = 202;

        _queue.Clear();
        _queue.Enqueue(301);
        _queue.Enqueue(302);
    }

    private void TryRunFinalAfterReconnect()
    {
        if (!RunFinalOnNextOwnerSpawn || !isSpawned || !isOwner)
            return;

        RunFinalOnNextOwnerSpawn = false;
        RunPostReconnectState();
        FinalRanAfterReconnect = true;
    }

    public bool MatchesPreReconnectState()
    {
        return SequenceEquals(_list.list, PreList)
               && DictEquals(new[] { 1, 2 }, new[] { 10, 22 })
               && SetEquals(PreSet)
               && ArrayEquals(PreArray)
               && QueueEquals(PreQueue);
    }

    public bool MatchesFinalState()
    {
        return SequenceEquals(_list.list, FinalList)
               && DictEquals(new[] { 101, 102 }, new[] { 201, 202 })
               && SetEquals(FinalSet)
               && ArrayEquals(FinalArray)
               && QueueEquals(FinalQueue);
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
        foreach (var kv in _dict.ToDictionary())
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

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalPreConverged(RPCInfo info = default) => ServerPreConvergedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalVictimReturned(RPCInfo info = default) => VictimReturnedCount++;

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastOwner(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastDisconnectCommand()
    {
        DisconnectCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone()
    {
        PhaseDoneReceived = true;
    }
}
