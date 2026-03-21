using UnityEngine;

namespace PurrNet.Demo
{

    public class LODDemoObject : NetworkIdentity, ITick
    {
        [SerializeField] private DemoLODModule _demoLODModule = new();

        public int counter;

        private int _receivedCounter;
        private float _lastDeltaTime;
        private Renderer _renderer;
        private Camera _camera;

        private static readonly Color[] _tierColors =
        {
            Color.green,
            Color.yellow,
            Color.orange,
            Color.red,
        };

        private void Awake()
        {
            _camera = Camera.main;
            _renderer = GetComponent<Renderer>();
        }

        protected override void OnSpawned(bool asServer)
        {
            if (asServer)
                _demoLODModule.target = this;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="target"></param>
        /// <param name="deltaTime"></param>
        /// <param name="serverCounter">Our LOD-capped state</param>
        [TargetRpc]
        public void TargetReceiveState(PlayerID target, float deltaTime, int serverCounter)
        {
            _receivedCounter = serverCounter;
            _lastDeltaTime = deltaTime;

            if (_renderer)
            {
                int tier = 0;
                for (int i = 0; i < _demoLODModule.tiers.Length; i++)
                {
                    var lodTier = _demoLODModule.tiers[i];
                    // small adjustment to account for latency and imperfect delta timing
                    if (deltaTime - 0.15f < lodTier.sendInterval)
                    {
                        tier = i;
                        break;
                    }
                }

                _renderer.material.color = _tierColors[tier];
            }
        }

        public void OnTick(float delta)
        {
            if (isServer)
                counter++;
        }


#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!isSpawned) return;

            Vector3 screenPoint = UnityEditor.HandleUtility.WorldToGUIPointWithDepth(_camera,
                transform.position + Vector3.up * 1.5f);
            GUI.Label(new Rect(screenPoint, new Vector2(150, 50)),
                $"Counter: {(isServer ? counter : _receivedCounter)}");
            GUI.Label(new Rect(screenPoint + new Vector3(0, 20), new Vector2(100, 50)),
                $"Delta: {_lastDeltaTime:F2}");
        }

        private void OnDrawGizmos()
        {
            var tiers = _demoLODModule.tiers;
            for (int i = 0; i < tiers.Length; i++)
            {
                var tier = tiers[i];
                var color = _tierColors[i];
                Gizmos.color = color;
                Gizmos.DrawWireSphere(transform.position, tier.maxDistance);
            }
        }
#endif
    }

}