using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;

public static class ScenarioSequencer
{
    private const float NO_TIMEOUT_SECONDS = 24f * 60f * 60f;

    private static readonly Dictionary<int, int> _ackCountByIndex = new();
    private static int _latestStartedIndex = -1;
    private static bool _sequenceComplete;

    public static bool SequenceComplete => _sequenceComplete;

    public static void IssueStart(int index)
    {
        BroadcastStart(index);
    }

    public static async UniTask WaitForStart(ScenarioContext ctx, int index)
    {
        await UniTaskUtils.WaitWithTimeout(
            () => _latestStartedIndex >= index || _sequenceComplete,
            NO_TIMEOUT_SECONDS,
            ctx.cancellationToken);
    }

    public static async UniTask WaitForAllAcks(ScenarioContext ctx, int index, int expectedAcks)
    {
        if (expectedAcks <= 0)
            return;

        await UniTaskUtils.WaitWithTimeout(
            () => _ackCountByIndex.TryGetValue(index, out var c) && c >= expectedAcks,
            NO_TIMEOUT_SECONDS,
            ctx.cancellationToken);

        _ackCountByIndex.Remove(index);
    }

    public static void AckLocalDone(ScenarioContext ctx, int index)
    {
        if (ctx.role != NetworkRole.Client)
            return;
        AckDone(index);
    }

    public static void IssueSequenceComplete()
    {
        BroadcastSequenceComplete();
    }

    public static int ExpectedAcks(ScenarioContext ctx)
    {
        return ctx.role == NetworkRole.Host
            ? ctx.expectedConnections - 1
            : ctx.expectedConnections;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastStart(int index)
    {
        if (index > _latestStartedIndex)
            _latestStartedIndex = index;
    }

    [ServerRpc(requireOwnership: false)]
    private static void AckDone(int index)
    {
        _ackCountByIndex.TryGetValue(index, out var count);
        _ackCountByIndex[index] = count + 1;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastSequenceComplete()
    {
        _sequenceComplete = true;
    }
}
