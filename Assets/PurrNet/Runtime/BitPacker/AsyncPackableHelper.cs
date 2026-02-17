using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PurrNet.Modules;

namespace PurrNet.Packing
{
    /// <summary>
    /// Helper for IAsyncPackable prepare calls. Used by codegen around sync pack/unpack.
    /// Runs async prepare on thread pool to avoid deadlock when called from Unity main thread.
    /// </summary>
    /// <remarks>
    /// Important: PrepareForPackAsync and PrepareAfterUnpackAsync run on a <b>thread pool thread</b>, not the main thread.
    /// Do <b>not</b> access Unity objects (GameObject, Transform, Component, etc.) in these methods.
    /// Use IAsyncPackable only for non-Unity async work (Addressables string keys, DB lookups, network resolution, etc.).
    /// For resolving Unity objects, prepare the wire data before calling the RPC (e.g. resolve to a string/int on main thread, then pass).
    /// </remarks>
    public static class AsyncPackableHelper
    {
        /// <summary>
        /// Calls PrepareForPackAsync if the value implements IAsyncPackable. Blocks until complete.
        /// Runs on thread pool to avoid deadlock on Unity main thread.
        /// </summary>
        [UsedByIL]
        public static void PrepareForPack<T>(ref T value)
        {
            if (value is IAsyncPackable asyncPackable)
            {
                value = (T)(object)RunAsyncAndBlock(() => asyncPackable.PrepareForPackAsync(), asyncPackable);
            }
        }

        /// <summary>
        /// Calls PrepareAfterUnpackAsync if the value implements IAsyncPackable. Blocks until complete.
        /// Runs on thread pool to avoid deadlock on Unity main thread.
        /// </summary>
        [UsedByIL]
        public static void PrepareAfterUnpack<T>(ref T value)
        {
            if (value is IAsyncPackable asyncPackable)
            {
                value = (T)(object)RunAsyncAndBlock(() => asyncPackable.PrepareAfterUnpackAsync(), asyncPackable);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static IAsyncPackable RunAsyncAndBlock(Func<Task> prepare, IAsyncPackable instance)
        {
            return Task.Run(async () =>
            {
                await prepare().ConfigureAwait(false);
                return instance;
            }).GetAwaiter().GetResult();
        }
    }
}
