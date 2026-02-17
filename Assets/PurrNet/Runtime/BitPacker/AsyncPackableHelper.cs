using System;
using System.Threading.Tasks;
using PurrNet.Modules;
using UnityEngine;

namespace PurrNet.Packing
{
    /// <summary>
    /// Helper for IAsyncPackable prepare calls. Used by codegen around sync pack/unpack.
    /// When IAsyncPackable params are present, codegen uses the async overloads so prepares run on main thread without deadlock.
    /// </summary>
    public static class AsyncPackableHelper
    {
        /// <summary>
        /// Sync version: blocks on prepare. Only used when no IAsyncPackable params (no-op for non-implementers).
        /// </summary>
        [UsedByIL]
        public static void PrepareForPack<T>(ref T value)
        {
            if (value is IAsyncPackable asyncPackable)
            {
                asyncPackable.PrepareForPackAsync().GetAwaiter().GetResult();
                value = (T)(object)asyncPackable;
            }
        }

        /// <summary>
        /// Sync version: blocks on prepare. Only used when no IAsyncPackable params.
        /// </summary>
        [UsedByIL]
        public static void PrepareAfterUnpack<T>(ref T value)
        {
            if (value is IAsyncPackable asyncPackable)
            {
                asyncPackable.PrepareAfterUnpackAsync().GetAwaiter().GetResult();
                value = (T)(object)asyncPackable;
            }
        }

        /// <summary>
        /// Async version: awaits prepare, returns prepared value. Used by codegen when IAsyncPackable params present.
        /// Returns Task&lt;T&gt; instead of ref to satisfy "async method cannot have ref parameters".
        /// </summary>
        [UsedByIL]
        public static async Task<T> PrepareForPackAsync<T>(T value)
        {
            if (value is IAsyncPackable asyncPackable)
            {
                await asyncPackable.PrepareForPackAsync();
                return (T)(object)asyncPackable;
            }
            return value;
        }

        /// <summary>
        /// Async version: awaits prepare, returns prepared value. Used by codegen when IAsyncPackable params present.
        /// </summary>
        [UsedByIL]
        public static async Task<T> PrepareAfterUnpackAsync<T>(T value)
        {
            if (value is IAsyncPackable asyncPackable)
            {
                await asyncPackable.PrepareAfterUnpackAsync();
                return (T)(object)asyncPackable;
            }
            return value;
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
        public static Task ExecuteAfterPrepareAsync(Task[] prepareTasks, Action<Task[]> storeResultsAndSend)
        {
            var executed = false;
            void OnComplete()
            {
                void Invoke()
                {
                    if (executed) return;
                    executed = true;
                    try
                    {
                        storeResultsAndSend(prepareTasks);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"IAsyncPackable RPC dropped: {ex.Message}");
                    }
                }
                if (System.Threading.SynchronizationContext.Current != null)
                    Invoke();
                else
                    UnityLatestUpdate.ExecuteAsap(Invoke);
            }
            return Task.WhenAll(prepareTasks).ContinueWith(_ => OnComplete());
        }

        /// <summary>
        /// Single-task overload. Runs sendAction after prepareTask completes. Used when no IAsyncPackable params.
        /// </summary>
        [UsedByIL]
        public static Task ExecuteAfterPrepareAsync(Task prepareTask, Action sendAction)
        {
            var executed = false;
            void OnComplete()
            {
                void Invoke()
                {
                    if (executed) return;
                    executed = true;
                    try
                    {
                        sendAction();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"IAsyncPackable RPC dropped: {ex.Message}");
                    }
                }
                if (System.Threading.SynchronizationContext.Current != null)
                    Invoke();
                else
                    UnityLatestUpdate.ExecuteAsap(Invoke);
            }
            return prepareTask.ContinueWith(_ => OnComplete());
        }
    }
}
