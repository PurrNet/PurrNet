using System.ComponentModel;
using System.Reflection;
using PurrNet.Logging;

namespace PurrNet
{
    public partial class NetworkReflection
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static bool __bypassMethodDispatch;

        private ReflectedMethod[] _reflectedMethods;
        private FieldInfo _backReferenceField;

        void InitMethodTracking()
        {
            if (_trackedMethods == null || _trackedMethods.Count == 0)
                return;

            _reflectedMethods = new ReflectedMethod[_trackedMethods.Count];

            for (var i = 0; i < _trackedMethods.Count; i++)
                _reflectedMethods[i] = new ReflectedMethod(_trackedBehaviour, _trackedMethods[i]);

            SetReflectionBackReference(this);
        }

        void SetReflectionBackReference(NetworkReflection value)
        {
            if (_trackedBehaviour == null)
                return;

            var type = _trackedBehaviour.GetType();
            _backReferenceField ??= type.GetField("__purrnet_reflection",
                BindingFlags.Public | BindingFlags.Instance);

            _backReferenceField?.SetValue(_trackedBehaviour, value);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SetReflectionBackReference(null);
        }

        /// <summary>
        /// Called by IL-injected dispatch code on target methods.
        /// Returns true if the call was dispatched as an RPC (caller should return).
        /// Returns false if the method should execute locally.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool TryDispatchMethod(string methodName, object[] args)
        {
            if (!isSpawned || _reflectedMethods == null)
                return false;

            for (int i = 0; i < _reflectedMethods.Length; i++)
            {
                if (_reflectedMethods[i].name != methodName)
                    continue;

                var method = _reflectedMethods[i];

                switch (method.rpcType)
                {
                    case ReflectionRPCType.ServerRpc:
                        if (isServer)
                            return false;
                        MethodForwardToServer(i, args);
                        return true;

                    case ReflectionRPCType.ObserversRpc:
                        SendMethodToObservers(i, args);
                        return true;
                }
            }

            return false;
        }

        private void SendMethodToObservers(int methodIndex, object[] args)
        {
            if (isServer)
                MethodBroadcastToObservers(methodIndex, args);
            else
                MethodForwardToServerThenBroadcast(methodIndex, args);
        }

        [ServerRpc(requireOwnership: false)]
        private void MethodForwardToServer(int methodIndex, object[] args)
        {
            ExecuteMethodLocally(methodIndex, args);
        }

        [ServerRpc(requireOwnership: false)]
        private void MethodForwardToServerThenBroadcast(int methodIndex, object[] args)
        {
            MethodBroadcastToObservers(methodIndex, args);
        }

        [ObserversRpc(runLocally: true)]
        private void MethodBroadcastToObservers(int methodIndex, object[] args)
        {
            ExecuteMethodLocally(methodIndex, args);
        }

        private void ExecuteMethodLocally(int methodIndex, object[] args)
        {
            if (methodIndex < 0 || _reflectedMethods == null || methodIndex >= _reflectedMethods.Length)
            {
                PurrLogger.LogError($"Invalid method index {methodIndex} on {name}", this);
                return;
            }

            __bypassMethodDispatch = true;

            try
            {
                _reflectedMethods[methodIndex].Invoke(_trackedBehaviour, args);
            }
            finally
            {
                __bypassMethodDispatch = false;
            }
        }
    }
}
