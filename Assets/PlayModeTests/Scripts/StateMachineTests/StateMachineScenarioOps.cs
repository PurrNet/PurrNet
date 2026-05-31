using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

internal static class StateMachineScenarioOps
{
    internal static async UniTask WaitOrFail(
        ScenarioContext ctx,
        Func<bool> condition,
        float timeoutSeconds,
        List<string> failures,
        Func<string> message)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(condition, timeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(message());
        }
    }

    internal static async UniTask WaitBarrierOrFail(
        ScenarioContext ctx,
        int barrierId,
        float timeoutSeconds,
        List<string> failures,
        Func<string> message)
    {
        try
        {
            await ScenarioBarrier.Wait(ctx, barrierId, timeoutSeconds);
        }
        catch (TimeoutException)
        {
            failures.Add(message());
        }
    }
}
