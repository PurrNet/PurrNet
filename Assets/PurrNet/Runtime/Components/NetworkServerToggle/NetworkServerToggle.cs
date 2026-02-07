using JetBrains.Annotations;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Server Toggle")]
    public sealed class NetworkServerToggle : NetworkIdentity
    {
        [Header("GameObjects to toggle when OnSpawned is called - from perspective of server")]
        [Tooltip("GameObjects to activate when the OnSpawned is called")]
        [SerializeField]
        private GameObject[] _toActivate;

        [Tooltip("GameObjects to deactivate when the OnSpawned is called")] [SerializeField]
        private GameObject[] _toDeactivate;

        [Header("Components to toggle when OnSpawned is called - from perspective of server")]
        [Tooltip("Components to enable when the OnSpawned is called")]
        [SerializeField]
        private Behaviour[] _toEnable;

        [Tooltip("Components to disable when the OnSpawned is called")] [SerializeField]
        private Behaviour[] _toDisable;

        protected override void OnSpawned()
        {
            base.OnSpawned();

            Setup(isServer);
        }

        [UsedImplicitly]
        public void Setup(bool asServer)
        {
            for (var i = 0; i < _toActivate.Length; i++)
            {
                var go = _toActivate[i];
                if (!go) continue;
                go.SetActive(asServer);
            }

            for (var i = 0; i < _toDeactivate.Length; i++)
            {
                var go = _toDeactivate[i];
                if (!go) continue;
                go.SetActive(!asServer);
            }

            for (var i = 0; i < _toEnable.Length; i++)
            {
                var comp = _toEnable[i];
                if (!comp) continue;
                comp.enabled = asServer;
            }

            for (var i = 0; i < _toDisable.Length; i++)
            {
                var comp = _toDisable[i];
                if (!comp) continue;
                comp.enabled = !asServer;
            }
        }
    }
}