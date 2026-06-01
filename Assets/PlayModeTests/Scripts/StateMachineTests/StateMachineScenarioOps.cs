using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

internal static class StateMachineScenarioOps
{
    internal static async UniTask<bool> RunExpandedChecks(
        ScenarioContext ctx,
        StateMachineTestRig inst,
        bool isController,
        int barrierBase,
        float stateTimeoutSeconds,
        float barrierTimeoutSeconds,
        List<string> failures,
        bool observerFinalOnly = false)
    {
        if (!isController && observerFinalOnly)
            return await RunObserverFinalOnlyChecks(
                ctx,
                inst,
                barrierBase,
                stateTimeoutSeconds,
                barrierTimeoutSeconds,
                failures);

        if (isController)
        {
            inst.AddPayloadState();

            if (inst.TrySetPayloadWithInvalidData())
            {
                failures.Add("SetState to payload state with invalid data returned true");
                return false;
            }

            if (!inst.SetStateToPayload())
            {
                failures.Add("SetState to payload state returned false");
                return false;
            }
        }

        if (!await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesPayloadCurrent,
            barrierBase,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "payload-current"))
            return false;

        inst.CaptureStateChangeCount();

        if (!await WaitBarrierOrFail(
            ctx,
            barrierBase + 1,
            barrierTimeoutSeconds,
            failures,
            () => $"payload baseline barrier timeout: {inst.Describe()}"))
            return false;

        if (isController)
            inst.InsertAfterCurrentStates();

        if (!await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesInsertedAfterCurrent,
            barrierBase + 2,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "insert-after-current"))
            return false;

        if (isController && !inst.NextValid())
        {
            failures.Add("NextValid returned false");
            return false;
        }

        if (!await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesNextValid,
            barrierBase + 3,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "next-valid"))
            return false;

        if (isController && !inst.PreviousValid())
        {
            failures.Add("PreviousValid returned false");
            return false;
        }

        if (!await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesPreviousValid,
            barrierBase + 4,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "previous-valid"))
            return false;

        if (isController && !inst.Previous())
        {
            failures.Add("Previous returned false");
            return false;
        }

        if (!await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesPrevious,
            barrierBase + 5,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "previous"))
            return false;

        if (isController && !inst.Next())
        {
            failures.Add("Next returned false");
            return false;
        }

        if (!await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesNext,
            barrierBase + 6,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "next"))
            return false;

        inst.CaptureStateChangeCount();

        if (!await WaitBarrierOrFail(
            ctx,
            barrierBase + 7,
            barrierTimeoutSeconds,
            failures,
            () => $"remove-by-reference baseline barrier timeout: {inst.Describe()}"))
            return false;

        if (isController && !inst.RemoveLaterStateByReference())
        {
            failures.Add("RemoveState for later state returned false");
            return false;
        }

        if (!await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesRemovedByReference,
            barrierBase + 8,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "remove-by-reference"))
            return false;

        inst.CaptureStateChangeCount();

        if (!await WaitBarrierOrFail(
            ctx,
            barrierBase + 9,
            barrierTimeoutSeconds,
            failures,
            () => $"remove-at baseline barrier timeout: {inst.Describe()}"))
            return false;

        if (isController)
            inst.RemoveLaterStateAt();

        if (!await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesExpandedFinal,
            barrierBase + 10,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "remove-at"))
            return false;

        if (!isController && inst.receivedNewDataCount == 0)
        {
            failures.Add("observer did not receive any state machine data callbacks");
            return false;
        }

        return true;
    }

    private static async UniTask<bool> RunObserverFinalOnlyChecks(
        ScenarioContext ctx,
        StateMachineTestRig inst,
        int barrierBase,
        float stateTimeoutSeconds,
        float barrierTimeoutSeconds,
        List<string> failures)
    {
        for (var i = 0; i <= 10; i++)
        {
            if (!await WaitBarrierOrFail(
                ctx,
                barrierBase + i,
                barrierTimeoutSeconds,
                failures,
                () => $"expanded observer barrier timeout: barrier={barrierBase + i}, {inst.Describe()}"))
                return false;
        }

        if (!await WaitOrFail(
            ctx,
            inst.MatchesExpandedFinalState,
            stateTimeoutSeconds,
            failures,
            () => $"never saw expanded final state; got {inst.Describe()}"))
            return false;

        if (inst.receivedNewDataCount == 0)
        {
            failures.Add("observer did not receive any state machine data callbacks");
            return false;
        }

        return true;
    }

    internal static async UniTask<bool> WaitOrFail(
        ScenarioContext ctx,
        Func<bool> condition,
        float timeoutSeconds,
        List<string> failures,
        Func<string> message)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(condition, timeoutSeconds, ctx.cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            failures.Add(message());
            return false;
        }
    }

    internal static async UniTask<bool> WaitBarrierOrFail(
        ScenarioContext ctx,
        int barrierId,
        float timeoutSeconds,
        List<string> failures,
        Func<string> message)
    {
        try
        {
            await ScenarioBarrier.Wait(ctx, barrierId, timeoutSeconds);
            return true;
        }
        catch (TimeoutException)
        {
            failures.Add(message());
            return false;
        }
    }

    private static async UniTask<bool> WaitStateAndBarrier(
        ScenarioContext ctx,
        StateMachineTestRig inst,
        Func<bool> condition,
        int barrierId,
        float stateTimeoutSeconds,
        float barrierTimeoutSeconds,
        List<string> failures,
        string name)
    {
        if (!await WaitOrFail(
            ctx,
            condition,
            stateTimeoutSeconds,
            failures,
            () => $"never saw {name}; got {inst.Describe()}"))
            return false;

        if (!await WaitBarrierOrFail(
            ctx,
            barrierId,
            barrierTimeoutSeconds,
            failures,
            () => $"{name} barrier timeout: {inst.Describe()}"))
            return false;

        return true;
    }
}
