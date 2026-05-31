using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

internal static class StateMachineScenarioOps
{
    internal static async UniTask RunExpandedChecks(
        ScenarioContext ctx,
        StateMachineTestRig inst,
        bool isController,
        int barrierBase,
        float stateTimeoutSeconds,
        float barrierTimeoutSeconds,
        List<string> failures)
    {
        if (isController)
        {
            inst.AddPayloadState();

            if (inst.TrySetPayloadWithInvalidData())
                failures.Add("SetState to payload state with invalid data returned true");

            if (!inst.SetStateToPayload())
                failures.Add("SetState to payload state returned false");
        }

        await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesPayloadCurrent,
            barrierBase,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "payload-current");

        inst.CaptureStateChangeCount();

        await WaitBarrierOrFail(
            ctx,
            barrierBase + 1,
            barrierTimeoutSeconds,
            failures,
            () => $"payload baseline barrier timeout: {inst.Describe()}");

        if (isController)
            inst.InsertAfterCurrentStates();

        await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesInsertedAfterCurrent,
            barrierBase + 2,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "insert-after-current");

        if (isController && !inst.NextValid())
            failures.Add("NextValid returned false");

        await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesNextValid,
            barrierBase + 3,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "next-valid");

        if (isController && !inst.PreviousValid())
            failures.Add("PreviousValid returned false");

        await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesPreviousValid,
            barrierBase + 4,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "previous-valid");

        if (isController && !inst.Previous())
            failures.Add("Previous returned false");

        await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesPrevious,
            barrierBase + 5,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "previous");

        if (isController && !inst.Next())
            failures.Add("Next returned false");

        await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesNext,
            barrierBase + 6,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "next");

        inst.CaptureStateChangeCount();

        await WaitBarrierOrFail(
            ctx,
            barrierBase + 7,
            barrierTimeoutSeconds,
            failures,
            () => $"remove-by-reference baseline barrier timeout: {inst.Describe()}");

        if (isController && !inst.RemoveLaterStateByReference())
            failures.Add("RemoveState for later state returned false");

        await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesRemovedByReference,
            barrierBase + 8,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "remove-by-reference");

        inst.CaptureStateChangeCount();

        await WaitBarrierOrFail(
            ctx,
            barrierBase + 9,
            barrierTimeoutSeconds,
            failures,
            () => $"remove-at baseline barrier timeout: {inst.Describe()}");

        if (isController)
            inst.RemoveLaterStateAt();

        await WaitStateAndBarrier(
            ctx,
            inst,
            inst.MatchesExpandedFinal,
            barrierBase + 10,
            stateTimeoutSeconds,
            barrierTimeoutSeconds,
            failures,
            "remove-at");

        if (!isController && inst.receivedNewDataCount == 0)
            failures.Add("observer did not receive any state machine data callbacks");
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

    private static async UniTask WaitStateAndBarrier(
        ScenarioContext ctx,
        StateMachineTestRig inst,
        Func<bool> condition,
        int barrierId,
        float stateTimeoutSeconds,
        float barrierTimeoutSeconds,
        List<string> failures,
        string name)
    {
        await WaitOrFail(
            ctx,
            condition,
            stateTimeoutSeconds,
            failures,
            () => $"never saw {name}; got {inst.Describe()}");

        await WaitBarrierOrFail(
            ctx,
            barrierId,
            barrierTimeoutSeconds,
            failures,
            () => $"{name} barrier timeout: {inst.Describe()}");
    }
}
