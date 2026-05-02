using System;
using System.Threading;
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
}
