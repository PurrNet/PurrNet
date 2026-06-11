#if UNITY_ANIMATION
using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Pooling;

namespace PurrNet
{
    public sealed partial class NetworkAnimator : ITick
    {
        private DisposableList<NetAnimatorRPC> _dirty = DisposableList<NetAnimatorRPC>.Create();
        readonly List<NetAnimatorRPC> _ikActions = new List<NetAnimatorRPC>();

        readonly List<PlayerID> _reconcilePlayers = new List<PlayerID>();

        protected override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            if (isSpawner)
                return;

            using var data = NetAnimatorActionBatch.CreateReconcile(_dontSyncHashes, _animator, false);
            ReconcileState(player, data);
            _reconcilePlayers.Add(player);
        }

        public void OnTick(float delta)
        {
            if (!IsController(_ownerAuth))
            {
                if (_dirty.Count > 0)
                    _dirty.Clear();
                return;
            }

            if (_autoSyncParameters)
                CheckForParameterChanges();
            SendDirtyActions();
        }

        /// <summary>
        /// Sends the current state of the animator to the observers.
        /// This is useful when you need to ensure that the observers are in sync with the controller.
        /// </summary>
        public void Reconcile(bool isIk = false)
        {
            if (!IsController(_ownerAuth))
                return;

            using var data = NetAnimatorActionBatch.CreateReconcile(_dontSyncHashes, _animator, isIk);

            if (isServer)
            {
                ApplyActionsOnObservers(data);
            }
            else
            {
                ForwardThroughServer(data);
            }
        }

        /// <summary>
        /// Sends the current state of the animator to the target player.
        /// This is useful when a new player joins the scene.
        /// Or when you need to ensure that the player is in sync with the controller.
        /// </summary>
        /// <param name="target">The target player to reconcile the state with.</param>
        /// <param name="isIk">Whether to reconcile the IK state or the regular state.</param>
        public void Reconcile(PlayerID target, bool isIk = false)
        {
            if (!IsController(_ownerAuth))
                return;

            using var data = NetAnimatorActionBatch.CreateReconcile(_dontSyncHashes, _animator, isIk);

            if (isServer)
            {
                ReconcileState(target, data);
            }
            else
            {
                ForwardThroughServerToTarget(target, data);
            }
        }

        public void ReconcileAnimationTime()
        {
            if (!IsController(_ownerAuth))
                return;

            using var data = NetAnimatorActionBatch.CreateTimeReconcile(_dontSyncHashes, _animator);

            if (isServer)
            {
                ApplyActionsOnObservers(data);
            }
            else
            {
                ForwardThroughServer(data);
            }
        }

        private void OptimizeBatch()
        {
            if (_dirty.Count <= 0)
                return;

            for (var i = _dirty.Count - 1; i >= 0; i--)
            {
                var action = _dirty[i];
                switch (action.type)
                {
                    case NetAnimatorAction.SetBool:
                    {
                        i -= RemovePast(i, action, (a, b)
                            => a._bool.nameHash == b._bool.nameHash);
                        break;
                    }
                    case NetAnimatorAction.SetFloat:
                    {
                        i -= RemovePast(i, action, (a, b)
                            => a._float.nameHash == b._float.nameHash);
                        break;
                    }
                    case NetAnimatorAction.SetInteger:
                    {
                        i -= RemovePast(i, action, (a, b)
                            => a._integer.nameHash == b._integer.nameHash);
                        break;
                    }
                    case NetAnimatorAction.SetTrigger:
                    {
                        i -= RemovePast(i, action, (a, b)
                            => a._trigger.nameHash == b._trigger.nameHash);
                        break;
                    }
                    case NetAnimatorAction.SetSpeed:
                    {
                        i -= RemovePast(i, action, (_, _) => true);
                        break;
                    }
                }
            }
        }

        private int RemovePast(int i, NetAnimatorRPC action, Func<NetAnimatorRPC, NetAnimatorRPC, bool> match)
        {
            int removed = 0;
            for (var j = i - 1; j >= 0; j--)
            {
                if (_dirty[j].type == action.type && match(_dirty[j], action))
                {
                    _dirty.RemoveAt(j);
                    removed++;
                }
            }

            return removed;
        }

        private void SendDirtyActions()
        {
            if (_dirty.Count <= 0)
                return;

            OptimizeBatch();

            var batch = new NetAnimatorActionBatch
            {
                actions = _dirty
            };

            if (isServer)
            {
                ApplyActionsOnObservers(batch);
            }
            else
            {
                ForwardThroughServer(batch);
            }

            _dirty.Clear();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (IsController(_ownerAuth))
            {
                _ikActions.Clear();

                for (var i = 0; i < _reconcilePlayers.Count; i++)
                    Reconcile(_reconcilePlayers[i], true);
                _reconcilePlayers.Clear();
                return;
            }

            _reconcilePlayers.Clear();

            for (var i = 0; i < _ikActions.Count; i++)
                _ikActions[i].Apply(_animator);
        }

        [TargetRpc(compressionLevel: CompressionLevel.Best)]
        private void ReconcileState([UsedImplicitly] PlayerID player, NetAnimatorActionBatch actions)
        {
            using (actions)
            {
                if (IsController(_ownerAuth))
                    return;

                ExecuteBatch(actions);
            }
        }

        [ServerRpc(compressionLevel: CompressionLevel.Best)]
        private void ForwardThroughServerToTarget(PlayerID target, NetAnimatorActionBatch actions)
        {
            using (actions)
            {
                if (_ownerAuth)
                    ReconcileState(target, actions);
            }
        }

        [ServerRpc(compressionLevel: CompressionLevel.Best)]
        private void ForwardThroughServer(NetAnimatorActionBatch actions)
        {
            using (actions)
            {
                if (_ownerAuth)
                {
                    ExecuteBatch(actions);
                    ApplyActionsOnObservers(actions);
                }
            }
        }

        [ObserversRpc(excludeSender: true, compressionLevel: CompressionLevel.Best)]
        private void ApplyActionsOnObservers(NetAnimatorActionBatch actions)
        {
            using (actions)
            {
                if (IsController(_ownerAuth))
                    return;

                ExecuteBatch(actions);
            }
        }

        private void ExecuteBatch(NetAnimatorActionBatch actions)
        {
            if (!_animator)
                return;

            if (actions.actions.list == null)
                return;

            for (var i = 0; i < actions.actions.Count; i++)
            {
                var action = actions.actions[i];

                if (!ResolveParamRef(ref action))
                    continue;

                bool isIk = action.type is
                    NetAnimatorAction.SetIKPosition or NetAnimatorAction.SetIKRotation or
                    NetAnimatorAction.SetIKHintPosition or NetAnimatorAction.SetLookAtPosition or
                    NetAnimatorAction.SetBoneLocalRotation or NetAnimatorAction.SetIKHintPositionWeight or
                    NetAnimatorAction.SetIKPositionWeight or NetAnimatorAction.SetIKRotationWeight or
                    NetAnimatorAction.SetBodyPosition or NetAnimatorAction.SetBodyRotation or
                    NetAnimatorAction.SetLookAtWeight;

                if (!isIk)
                    action.Apply(_animator);
                else _ikActions.Add(action);
            }
        }

        private bool ResolveParamRef(ref NetAnimatorRPC action)
        {
            switch (action.type)
            {
                case NetAnimatorAction.SetBool:
                    return ResolveParamHash(ref action._bool.nameHash, action._bool.paramIndexPlusOne);
                case NetAnimatorAction.SetFloat:
                    return ResolveParamHash(ref action._float.nameHash, action._float.paramIndexPlusOne);
                case NetAnimatorAction.SetInteger:
                    return ResolveParamHash(ref action._integer.nameHash, action._integer.paramIndexPlusOne);
                case NetAnimatorAction.SetTrigger:
                    return ResolveParamHash(ref action._trigger.nameHash, action._trigger.paramIndexPlusOne);
                case NetAnimatorAction.ResetTrigger:
                    return ResolveParamHash(ref action._resetTrigger.nameHash, action._resetTrigger.paramIndexPlusOne);
                default:
                    return true;
            }
        }

        private bool ResolveParamHash(ref int nameHash, int paramIndexPlusOne)
        {
            if (paramIndexPlusOne <= 0)
                return true;

            var parameters = GetCachedParameters();
            int index = paramIndexPlusOne - 1;

            if (index >= parameters.Length)
                return false;

            nameHash = parameters[index].nameHash;
            return true;
        }
    }
}
#endif
