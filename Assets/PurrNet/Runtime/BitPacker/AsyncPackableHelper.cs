using System;
using System.Threading;
using System.Threading.Tasks;
using PurrNet.Logging;
using PurrNet.Modules;

namespace PurrNet.Packing
{
    /// <summary>
    /// Helper for IAsyncPackable prepare calls. Used by codegen around sync pack/unpack.
    /// When IAsyncPackable params are present, codegen uses the async overloads so prepares run on main thread without deadlock.
    /// </summary>
    public static class AsyncPackableHelper
    {
        /// <summary>
        /// Static generic cache to check IAsyncPackable at the type level, avoiding boxing value types.
        /// </summary>
        private static class Cache<T>
        {
            public static readonly bool isAsyncPackable = typeof(IAsyncPackable).IsAssignableFrom(typeof(T));
        }

        /// <summary>
        /// Sync version: blocks on prepare. Only used when no IAsyncPackable params (no-op for non-implementers).
        /// </summary>
        [UsedByIL]
        public static void PrepareForPack<T>(ref T value)
        {
            if (!Cache<T>.isAsyncPackable) return;

            if (value is IAsyncPackable asyncPackable)
            {
                var vt = asyncPackable.PrepareForPackAsync();
                if (vt.IsCompletedSuccessfully)
                    value = (T)(object)vt.Result;
                else
                    value = (T)(object)vt.AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Sync version: blocks on prepare. Only used when no IAsyncPackable params.
        /// </summary>
        [UsedByIL]
        public static void PrepareAfterUnpack<T>(ref T value)
        {
            if (!Cache<T>.isAsyncPackable) return;

            if (value is IAsyncPackable asyncPackable)
            {
                var vt = asyncPackable.PrepareAfterUnpackAsync();
                if (vt.IsCompletedSuccessfully)
                    value = (T)(object)vt.Result;
                else
                    value = (T)(object)vt.AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Async version: awaits prepare, returns prepared value. Used by codegen when IAsyncPackable params present.
        /// Returns Task&lt;T&gt; instead of ref to satisfy "async method cannot have ref parameters".
        /// </summary>
        [UsedByIL]
        public static Task<T> PrepareForPackAsync<T>(T value)
        {
            if (!Cache<T>.isAsyncPackable) return Task.FromResult(value);

            if (value is IAsyncPackable asyncPackable)
            {
                var vt = asyncPackable.PrepareForPackAsync();
                if (vt.IsCompletedSuccessfully)
                    return Task.FromResult((T)(object)vt.Result);
                return AwaitAndCast<T>(vt);
            }
            return Task.FromResult(value);
        }

        /// <summary>
        /// Async version: awaits prepare, returns prepared value. Used by codegen when IAsyncPackable params present.
        /// </summary>
        [UsedByIL]
        public static Task<T> PrepareAfterUnpackAsync<T>(T value)
        {
            if (!Cache<T>.isAsyncPackable) return Task.FromResult(value);

            if (value is IAsyncPackable asyncPackable)
            {
                var vt = asyncPackable.PrepareAfterUnpackAsync();
                if (vt.IsCompletedSuccessfully)
                    return Task.FromResult((T)(object)vt.Result);
                return AwaitAndCast<T>(vt);
            }
            return Task.FromResult(value);
        }
        
        private static async Task<T> AwaitAndCast<T>(ValueTask<IAsyncPackable> vt)
        {
            var result = await vt;
            return (T)(object)result;
        }

        /// <summary>
        /// Gets Task.Result without IL-level Task&lt;T&gt; reference. Codegen uses this to avoid
        /// MissingMethodException when Task comes from a different assembly than netstandard.
        /// </summary>
        [UsedByIL]
        public static T GetTaskResult<T>(Task<T> task)
        {
            return task.Result;
        }

        /// <summary>
        /// Runs storeResultsAndSend(tasks) after all tasks complete. Continuation runs on main thread
        /// (Unity's main thread or SynchronizationContext) so RunLocal and SendRPC work correctly.
        /// </summary>
        [UsedByIL]
        public static async Task ExecuteAfterPrepareAsync(Task[] prepareTasks, Action<Task[]> storeResultsAndSend)
        {
            bool allComplete = true;
            for (int i = 0; i < prepareTasks.Length; i++)
            {
                if (!prepareTasks[i].IsCompleted)
                {
                    allComplete = false;
                    break;
                }
            }

            if (allComplete)
            {
                InvokeOnMain(prepareTasks, storeResultsAndSend);
                return;
            }

            try { await Task.WhenAll(prepareTasks); } catch { }

            if (SynchronizationContext.Current != null)
                InvokeOnMain(prepareTasks, storeResultsAndSend);
            else
                UnityLatestUpdate.ExecuteAsap(() => InvokeOnMain(prepareTasks, storeResultsAndSend));
        }

        /// <summary>
        /// Single-task overload. Runs sendAction after prepareTask completes. Used when no IAsyncPackable params.
        /// </summary>
        [UsedByIL]
        public static async Task ExecuteAfterPrepareAsync(Task prepareTask, Action sendAction)
        {
            if (prepareTask.IsCompleted)
            {
                InvokeOnMain(sendAction);
                return;
            }

            try { await prepareTask; } catch { }

            if (SynchronizationContext.Current != null)
                InvokeOnMain(sendAction);
            else
                UnityLatestUpdate.ExecuteAsap(() => InvokeOnMain(sendAction));
        }

        private static void InvokeOnMain(Task[] tasks, Action<Task[]> callback)
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                if (!tasks[i].IsFaulted) continue;

                PurrLogger.LogError(
                    $"IAsyncPackable prepare failed for argument {i}; " +
                    "RPC will not execute. Check your PrepareForPackAsync/PrepareAfterUnpackAsync implementation.");

                if (tasks[i].Exception != null)
                    PurrLogger.LogException(tasks[i].Exception);

                return;
            }

            try
            {
                callback(tasks);
            }
            catch (Exception ex)
            {
                PurrLogger.LogException(ex);
            }
        }

        private static void InvokeOnMain(Action callback)
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                PurrLogger.LogException(ex);
            }
        }
    }
}
