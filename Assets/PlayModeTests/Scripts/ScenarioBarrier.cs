using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public static class ScenarioBarrier
{
    private static readonly Dictionary<int, int> _arrivedByBarrier = new();
    private static int _lastProceedBarrier = -1;

    public static async UniTask Wait(ScenarioContext ctx, int barrierId, float timeoutSeconds)
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
