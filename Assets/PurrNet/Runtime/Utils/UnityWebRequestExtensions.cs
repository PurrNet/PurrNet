#if UNITY_WEB
using System;
using System.Runtime.CompilerServices;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace PurrNet
{
    public static class UnityWebRequestExtensions
    {
        private static bool? _hasNativeAwaiter = null;

        private static bool HasNativeAwaiter
        {
            get
            {
                if (!_hasNativeAwaiter.HasValue)
                {
                    _hasNativeAwaiter = typeof(UnityWebRequestAsyncOperation)
                        .GetMethod("GetAwaiter", BindingFlags.Public | BindingFlags.Instance) != null;
                }

                return _hasNativeAwaiter.Value;
            }
        }

        public interface IAwaiter : INotifyCompletion
        {
            bool IsCompleted { get; }
            UnityWebRequestAsyncOperation GetResult();
        }

        public struct UnityWebRequestAwaiter : IAwaiter
        {
            private UnityWebRequestAsyncOperation asyncOp;
            private Action continuation;

            public UnityWebRequestAwaiter(UnityWebRequestAsyncOperation asyncOp)
            {
                this.asyncOp = asyncOp;
                this.continuation = null;
            }

            public bool IsCompleted => asyncOp.isDone;

            public void OnCompleted(Action continuation)
            {
                this.continuation = continuation;
                asyncOp.completed += OnRequestCompleted;
            }

            private void OnRequestCompleted(AsyncOperation obj)
            {
                continuation?.Invoke();
            }

            public UnityWebRequestAsyncOperation GetResult() => asyncOp;
        }

        public static IAwaiter GetAwaiter(this UnityWebRequestAsyncOperation asyncOp)
        {
            if (HasNativeAwaiter)
            {
                return (IAwaiter)typeof(UnityWebRequestAsyncOperation)
                    .GetMethod("GetAwaiter")
                    ?.Invoke(asyncOp, null);
            }

            return new UnityWebRequestAwaiter(asyncOp);
        }
    }
}

#endif
