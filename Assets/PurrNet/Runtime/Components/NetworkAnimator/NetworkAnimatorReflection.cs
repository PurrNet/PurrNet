#if UNITY_ANIMATION
using System.Collections.Generic;
using UnityEngine;

namespace PurrNet
{
    public sealed partial class NetworkAnimator
    {
        readonly Dictionary<int, int> _intValues = new Dictionary<int, int>();
        readonly Dictionary<int, float> _floatValues = new Dictionary<int, float>();
        readonly Dictionary<int, bool> _boolValues = new Dictionary<int, bool>();

        private bool _wasController;

        protected override void OnSpawned()
        {
            _wasController = IsController(_ownerAuth);
        }

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            bool isControlling = IsController(_ownerAuth);

            bool shouldReconcile = (hasConnectedOwner && isOwner && !asServer) || (asServer && isControlling);

            if (shouldReconcile)
                Reconcile();

            if (_wasController != isControlling)
            {
                _wasController = isControlling;
                if (_autoSyncParameters)
                    UpdateParamerCache();
            }
        }

        private void UpdateParamerCache()
        {
            if (!_animator || !_animator.runtimeAnimatorController)
                return;

            int paramCount = _animator.parameterCount;

            for (var i = 0; i < paramCount; i++)
            {
                var param = _animator.parameters[i];

                if (_dontSyncHashes.Contains(param.nameHash))
                    continue;

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        _boolValues[param.nameHash] = _animator.GetBool(param.name);
                        break;
                    case AnimatorControllerParameterType.Float:
                        _floatValues[param.nameHash] = _animator.GetFloat(param.name);
                        break;
                    case AnimatorControllerParameterType.Int:
                        _intValues[param.nameHash] = _animator.GetInteger(param.name);
                        break;
                }
            }
        }

        private void CheckForParameterChanges()
        {
            if (!_animator || !_animator.runtimeAnimatorController)
                return;

            int paramCount = _animator.parameterCount;

            for (var i = 0; i < paramCount; i++)
            {
                var param = _animator.parameters[i];

                if (_dontSyncHashes.Contains(param.nameHash))
                    continue;

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    {
                        if (_boolValues.TryGetValue(param.nameHash, out var v) && v == _animator.GetBool(param.name))
                            continue;

                        var setBool = new SetBool
                        {
                            value = _animator.GetBool(param.name),
                            nameHash = param.nameHash
                        };

                        _boolValues[param.nameHash] = setBool.value;

                        IfSameReplace(new NetAnimatorRPC(setBool),
                            (a, b) => a._bool.nameHash == b._bool.nameHash);
                        break;
                    }
                    case AnimatorControllerParameterType.Float:
                    {
                        if (_floatValues.TryGetValue(param.nameHash, out var v) &&
                            Mathf.Approximately(v, _animator.GetFloat(param.name)))
                            continue;

                        var setFloat = new SetFloat
                        {
                            value = _animator.GetFloat(param.name),
                            nameHash = param.nameHash
                        };

                        _floatValues[param.nameHash] = setFloat.value;

                        IfSameReplace(new NetAnimatorRPC(setFloat),
                            (a, b) => a._float.nameHash == b._float.nameHash);
                        break;
                    }
                    case AnimatorControllerParameterType.Int:
                    {
                        if (_intValues.TryGetValue(param.nameHash, out var v) && v == _animator.GetInteger(param.name))
                            continue;

                        var setInteger = new SetInteger
                        {
                            value = _animator.GetInteger(param.name),
                            nameHash = param.nameHash
                        };

                        _intValues[param.nameHash] = setInteger.value;

                        IfSameReplace(new NetAnimatorRPC(setInteger),
                            (a, b) => a._integer.nameHash == b._integer.nameHash);
                        break;
                    }
                }
            }
        }
    }
}
#endif
