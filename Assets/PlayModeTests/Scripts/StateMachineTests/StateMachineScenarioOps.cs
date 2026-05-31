using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

internal static class StateMachineScenarioOps
{
    internal static async UniTask RunPhaseOne(
        ScenarioContext ctx,
        StateMachineTestRig inst,
        List<string> failures,
        float timeoutSeconds)
    {
        inst.InsertRegressionState();

        if (!inst.SetStateToOriginalLast())
            failures.Add("SetState to original last state returned false");

        await WaitOrFail(
            ctx,
            inst.MatchesInsertedCurrent,
            timeoutSeconds,
            failures,
            () => $"inserted-current timeout: {inst.Describe()}");

        if (!inst.RemoveRegressionState())
            failures.Add("RemoveState for inserted state returned false");

        await WaitOrFail(
            ctx,
            inst.MatchesPhaseOne,
            timeoutSeconds,
            failures,
            () => $"phase-one local timeout: {inst.Describe()}");
    }

    internal static async UniTask RunFinalPhase(
        ScenarioContext ctx,
        StateMachineTestRig inst,
        List<string> failures,
        float timeoutSeconds)
    {
        inst.AddExtraState();

        if (!inst.SetStateToAdded())
            failures.Add("SetState to added state returned false");

        await WaitOrFail(
            ctx,
            inst.MatchesAddedCurrent,
            timeoutSeconds,
            failures,
            () => $"added-current timeout: {inst.Describe()}");

        inst.RemoveFirstState();

        await WaitOrFail(
            ctx,
            inst.MatchesFinal,
            timeoutSeconds,
            failures,
            () => $"final local timeout: {inst.Describe()}");
    }

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
