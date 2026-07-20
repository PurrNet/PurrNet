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

        private AnimatorControllerParameter[] _cachedParameters;
        private RuntimeAnimatorController _cachedController;
        private readonly Dictionary<int, int> _paramIndexByHash = new Dictionary<int, int>();

        private AnimatorControllerParameter[] GetCachedParameters()
        {
            var controller = _animator.runtimeAnimatorController;
            if (_cachedParameters == null || _cachedController != controller ||
                _cachedParameters.Length != _animator.parameterCount)
            {
                _cachedController = controller;
                _cachedParameters = _animator.parameters;

                _paramIndexByHash.Clear();
                for (var i = 0; i < _cachedParameters.Length; i++)
                    _paramIndexByHash[_cachedParameters[i].nameHash] = i;
            }

            return _cachedParameters;
        }

        private int GetParamIndexPlusOne(int nameHash)
        {
            if (!_animator || !_animator.runtimeAnimatorController)
                return 0;

            GetCachedParameters();
            return _paramIndexByHash.TryGetValue(nameHash, out var index) ? index + 1 : 0;
        }

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

            var parameters = GetCachedParameters();

            for (var i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];

                if (_dontSyncHashes.Contains(param.nameHash))
                    continue;

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        _boolValues[param.nameHash] = _animator.GetBool(param.nameHash);
                        break;
                    case AnimatorControllerParameterType.Float:
                        _floatValues[param.nameHash] = _animator.GetFloat(param.nameHash);
                        break;
                    case AnimatorControllerParameterType.Int:
                        _intValues[param.nameHash] = _animator.GetInteger(param.nameHash);
                        break;
                }
            }
        }

        private void CheckForParameterChanges()
        {
            if (!_animator || !_animator.runtimeAnimatorController)
                return;

            var parameters = GetCachedParameters();

            for (var i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];

                if (_dontSyncHashes.Contains(param.nameHash))
                    continue;

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    {
                        var current = _animator.GetBool(param.nameHash);

                        if (_boolValues.TryGetValue(param.nameHash, out var v) && v == current)
                            continue;

                        var setBool = new SetBool
                        {
                            value = current,
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
                        };

                        _boolValues[param.nameHash] = setBool.value;

                        IfSameReplace(new NetAnimatorRPC(setBool),
                            (a, b) => a._bool.nameHash == b._bool.nameHash);
                        break;
                    }
                    case AnimatorControllerParameterType.Float:
                    {
                        var current = _animator.GetFloat(param.nameHash);

                        if (_floatValues.TryGetValue(param.nameHash, out var v) &&
                            Mathf.Approximately(v, current))
                            continue;

                        var setFloat = new SetFloat
                        {
                            value = current,
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
                        };

                        _floatValues[param.nameHash] = setFloat.value;

                        IfSameReplace(new NetAnimatorRPC(setFloat),
                            (a, b) => a._float.nameHash == b._float.nameHash);
                        break;
                    }
                    case AnimatorControllerParameterType.Int:
                    {
                        var current = _animator.GetInteger(param.nameHash);

                        if (_intValues.TryGetValue(param.nameHash, out var v) && v == current)
                            continue;

                        var setInteger = new SetInteger
                        {
                            value = current,
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
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
