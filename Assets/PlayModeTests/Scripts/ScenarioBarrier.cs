using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public static class ScenarioBarrier
{
    private static readonly Dictionary<int, int> _arrivedByBarrier = new();
    private static readonly Dictionary<int, UniTask> _inFlight = new();
    private static int _lastProceedBarrier = -1;

    public static UniTask Wait(ScenarioContext ctx, int barrierId, float timeoutSeconds)
    {
        if (_inFlight.TryGetValue(barrierId, out var existing))
            return existing;

        var task = WaitImpl(ctx, barrierId, timeoutSeconds).Preserve();
        _inFlight[barrierId] = task;
        return task;
    }

    private static async UniTask WaitImpl(ScenarioContext ctx, int barrierId, float timeoutSeconds)
    {
        try
        {
            if (ctx.isClient)
                ReportArrived(barrierId);

            if (ctx.isServer)
            {
                try
                {
                    await UniTaskUtils.WaitWithTimeout(
                        () => _arrivedByBarrier.TryGetValue(barrierId, out var c) && c >= ctx.expectedConnections,
                        timeoutSeconds,
                        ctx.cancellationToken);
                }
                finally
                {
                    // Always release clients, even on timeout, so a single missing
                    // process doesn't strand the rest of the run.
                    _arrivedByBarrier.Remove(barrierId);
                    BroadcastProceed(barrierId);
                }
            }

            if (ctx.isClient)
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _lastProceedBarrier >= barrierId,
                    timeoutSeconds,
                    ctx.cancellationToken);
            }
        }
        finally
        {
            _inFlight.Remove(barrierId);
        }
    }

    [ServerRpc(requireOwnership: false)]
    private static void ReportArrived(int barrierId)
    {
        _arrivedByBarrier.TryGetValue(barrierId, out var count);
        _arrivedByBarrier[barrierId] = count + 1;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastProceed(int barrierId)
    {
        if (barrierId > _lastProceedBarrier)
            _lastProceedBarrier = barrierId;
    }
}
