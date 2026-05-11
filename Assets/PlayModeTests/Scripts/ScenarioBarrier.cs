using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public static class ScenarioBarrier
{
    private static readonly Dictionary<int, int> _arrivedByBarrier = new();
    // Shared handle for concurrent in-process callers (host's parallel RunSplit halves).
    // System.Threading.Task is multi-awaitable; UniTask.Preserve()/MemoizeSource is not
    // before completion — it forwards OnCompleted to the underlying state-machine source,
    // which only allows one continuation.
    private static readonly Dictionary<int, Task> _inFlight = new();
    private static int _lastProceedBarrier = -1;

    public static async UniTask Wait(ScenarioContext ctx, int barrierId, float timeoutSeconds)
    {
        if (_inFlight.TryGetValue(barrierId, out var existing))
        {
            await existing;
            return;
        }

        var task = WaitImpl(ctx, barrierId, timeoutSeconds).AsTask();
        _inFlight[barrierId] = task;
        try
        {
            await task;
        }
        finally
        {
            _inFlight.Remove(barrierId);
        }
    }

    private static async UniTask WaitImpl(ScenarioContext ctx, int barrierId, float timeoutSeconds)
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
            catch (System.TimeoutException)
            {
                _arrivedByBarrier.TryGetValue(barrierId, out var arrived);
                Debug.LogError(
                    $"[ScenarioBarrier] server timeout barrier={barrierId} arrived={arrived}/{ctx.expectedConnections} role={ctx.role}");
                throw;
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
