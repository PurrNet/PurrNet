#if UNITY_ANIMATION
using System;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet
{
    internal struct NetAnimatorActionBatch : IPackedAuto, IDisposable
    {
        public DisposableList<NetAnimatorRPC> actions;

        public void Dispose()
        {
            actions.Dispose();
        }

        public static NetAnimatorActionBatch CreateTimeReconcile(HashSet<int> ignoreHashes, Animator animator)
        {
            var actions = DisposableList<NetAnimatorRPC>.Create();
            SyncParameters(ignoreHashes, animator, actions);
            return new NetAnimatorActionBatch
            {
                actions = actions
            };
        }

        public static NetAnimatorActionBatch CreateReconcile(HashSet<int> ignoreHashes, Animator animator, bool ik)
        {
            var actions = DisposableList<NetAnimatorRPC>.Create();

            if (ik)
            {
                SyncIK(AvatarIKGoal.LeftFoot, animator, actions);
                SyncIK(AvatarIKGoal.RightFoot, animator, actions);
                SyncIK(AvatarIKGoal.LeftHand, animator, actions);
                SyncIK(AvatarIKGoal.RightHand, animator, actions);

                SyncIKHint(AvatarIKHint.LeftKnee, animator, actions);
                SyncIKHint(AvatarIKHint.RightKnee, animator, actions);
                SyncIKHint(AvatarIKHint.LeftElbow, animator, actions);
                SyncIKHint(AvatarIKHint.RightElbow, animator, actions);
            }
            else
            {
                SyncParameters(ignoreHashes, animator, actions);
                SyncAnimationState(animator, actions);

                actions.Add(new NetAnimatorRPC(new SetApplyRootMotion
                {
                    value = animator.applyRootMotion
                }));

                actions.Add(new NetAnimatorRPC(new SetAnimatePhysics
                {
                    value = animator.animatePhysics
                }));

                actions.Add(new NetAnimatorRPC(new SetUpdateMode
                {
                    value = animator.updateMode
                }));

                actions.Add(new NetAnimatorRPC(new SetCullingMode
                {
                    value = animator.cullingMode
                }));
            }

            return new NetAnimatorActionBatch
            {
                actions = actions
            };
        }

        private static void SyncIK(AvatarIKGoal goal, Animator animator, DisposableList<NetAnimatorRPC> actions)
        {
            if (animator.GetIKPositionWeight(goal) > 0)
            {
                actions.Add(new NetAnimatorRPC(new SetIKPositionWeight
                {
                    goal = goal,
                    value = animator.GetIKPositionWeight(goal)
                }));
            }

            if (animator.GetIKRotationWeight(goal) > 0)
            {
                actions.Add(new NetAnimatorRPC(new SetIKRotationWeight
                {
                    goal = goal,
                    value = animator.GetIKRotationWeight(goal)
                }));
            }

            if (animator.GetIKPosition(goal) != default)
            {
                actions.Add(new NetAnimatorRPC(new SetIKPosition
                {
                    goal = goal,
                    position = animator.GetIKPosition(goal)
                }));
            }

            if (animator.GetIKRotation(goal) != Quaternion.identity)
            {
                actions.Add(new NetAnimatorRPC(new SetIKRotation
                {
                    goal = goal,
                    rotation = animator.GetIKRotation(goal)
                }));
            }
        }

        private static void SyncIKHint(AvatarIKHint hint, Animator animator, DisposableList<NetAnimatorRPC> actions)
        {
            if (animator.GetIKHintPositionWeight(hint) > 0)
            {
                actions.Add(new NetAnimatorRPC(new SetIKHintPositionWeight
                {
                    hint = hint,
                    value = animator.GetIKHintPositionWeight(hint)
                }));
            }

            if (animator.GetIKHintPosition(hint) != default)
            {
                actions.Add(new NetAnimatorRPC(new SetIKHintPosition
                {
                    hint = hint,
                    position = animator.GetIKHintPosition(hint)
                }));
            }
        }

        private static void SyncAnimationState(Animator animator, DisposableList<NetAnimatorRPC> actions)
        {
            if (!animator.runtimeAnimatorController)
                return;

            for (var i = 0; i < animator.layerCount; i++)
            {
                var info = animator.GetCurrentAnimatorStateInfo(i);
                actions.Add(new NetAnimatorRPC(new Play_STATEHASH_LAYER_NORMALIZEDTIME
                {
                    stateHash = info.fullPathHash,
                    layer = i,
                    normalizedTime = info.normalizedTime
                }));
            }
        }

        private static void SyncParameters(HashSet<int> ignoreHashes, Animator animator, DisposableList<NetAnimatorRPC> actions)
        {
            if (!animator.runtimeAnimatorController)
                return;

            int paramCount = animator.parameterCount;

            for (var i = 0; i < paramCount; i++)
            {
                var param = animator.parameters[i];

                if (ignoreHashes.Contains(param.nameHash))
                    continue;

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    {
                        var setBool = new SetBool
                        {
                            value = animator.GetBool(param.name),
                            nameHash = param.nameHash
                        };

                        actions.Add(new NetAnimatorRPC(setBool));
                        break;
                    }
                    case AnimatorControllerParameterType.Float:
                    {
                        var setFloat = new SetFloat
                        {
                            value = animator.GetFloat(param.name),
                            nameHash = param.nameHash
                        };

                        actions.Add(new NetAnimatorRPC(setFloat));
                        break;
                    }
                    case AnimatorControllerParameterType.Int:
                    {
                        var setInteger = new SetInteger
                        {
                            value = animator.GetInteger(param.name),
                            nameHash = param.nameHash
                        };

                        actions.Add(new NetAnimatorRPC(setInteger));
                        break;
                    }
                }
            }
        }
    }
}
#endif
