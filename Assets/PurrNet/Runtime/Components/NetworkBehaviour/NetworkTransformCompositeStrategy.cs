using UnityEngine;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkTransformCompositeStrategy",
        menuName = "PurrNet/Network Transform Composite Strategy")]
    public class NetworkTransformCompositeStrategy : NetworkTransformStrategySettings
    {
        [Tooltip("Strategies evaluated in order. A send can be skipped when any strategy allows it, " +
                 "and gaps are reconstructed by the first strategy able to. " +
                 "The composite's own interval and extrapolation settings apply; the children's are ignored.")]
        public NetworkTransformStrategySettings[] strategies;

        private static int _depth;

        internal override bool CanSkip(NetworkTransform nt, in NetworkTransformState from, ushort fromTick,
            ushort currentTick, in NetworkTransformState current)
        {
            if (strategies == null || strategies.Length == 0)
                return base.CanSkip(nt, from, fromTick, currentTick, current);

            if (_depth > 4)
                return false;

            _depth++;

            try
            {
                for (int i = 0; i < strategies.Length; i++)
                {
                    var strategy = strategies[i];
                    if (strategy && strategy != this &&
                        strategy.CanSkip(nt, from, fromTick, currentTick, current))
                        return true;
                }

                return false;
            }
            finally
            {
                _depth--;
            }
        }

        internal override bool TryReconstruct(in NetworkTransformState prev, in NetworkTransformState from,
            in NetworkTransformState to, float t, out NetworkTransformState result)
        {
            result = default;

            if (strategies == null || strategies.Length == 0)
                return false;

            if (_depth > 4)
                return false;

            _depth++;

            try
            {
                for (int i = 0; i < strategies.Length; i++)
                {
                    var strategy = strategies[i];
                    if (strategy && strategy != this &&
                        strategy.TryReconstruct(prev, from, to, t, out result))
                        return true;
                }

                return false;
            }
            finally
            {
                _depth--;
            }
        }
    }
}
