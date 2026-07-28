namespace PurrNet
{
    /// <summary>
    /// Combines multiple strategies: gaps are reconstructed by the first strategy able to.
    /// The composite's own maxSendInterval applies; the children's are ignored.
    /// </summary>
    public class NetworkTransformCompositeStrategy : NetworkTransformSyncStrategy
    {
        public NetworkTransformSyncStrategy[] strategies;

        private static int _depth;

        protected override bool TryReconstruct(in NetworkTransformSample prev, in NetworkTransformSample from,
            in NetworkTransformSample to, float t, ref NetworkTransformSample result)
        {
            if (strategies == null || strategies.Length == 0)
                return false;

            if (_depth > 4)
                return false;

            _depth++;

            try
            {
                var prefill = result;

                for (int i = 0; i < strategies.Length; i++)
                {
                    var strategy = strategies[i];
                    if (strategy == null || strategy == this)
                        continue;

                    var attempt = prefill;
                    if (strategy.TryReconstructSample(prev, from, to, t, ref attempt))
                    {
                        result = attempt;
                        return true;
                    }
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
