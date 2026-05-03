using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

public static class UniTaskUtils
{
    public static async UniTask WaitWithTimeout(Func<bool> untilTrue, double timeoutInSeconds, CancellationToken cancellationToken)
    {
        var start = Time.realtimeSinceStartupAsDouble;
        while (!untilTrue())
        {
            await UniTask.NextFrame();
            var elapsed = Time.realtimeSinceStartupAsDouble - start;
            if (elapsed > timeoutInSeconds)
                throw new TimeoutException();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public static async UniTask<T> WithTimeout<T>(Task<T> task, double timeoutInSeconds, CancellationToken cancellationToken, string label = null)
    {
        var delay = Task.Delay(TimeSpan.FromSeconds(timeoutInSeconds), cancellationToken);
        var winner = await Task.WhenAny(task, delay);
        if (winner != task)
            throw new TimeoutException($"`{label ?? "task"}` did not complete within {timeoutInSeconds}s");
        return await task;
    }

    public static async UniTask WithTimeout(Task task, double timeoutInSeconds, CancellationToken cancellationToken, string label = null)
    {
        var delay = Task.Delay(TimeSpan.FromSeconds(timeoutInSeconds), cancellationToken);
        var winner = await Task.WhenAny(task, delay);
        if (winner != task)
            throw new TimeoutException($"`{label ?? "task"}` did not complete within {timeoutInSeconds}s");
        await task;
    }
}
