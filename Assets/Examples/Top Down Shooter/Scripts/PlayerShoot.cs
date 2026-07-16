#if UNITY_PHYSICS_3D

using UnityEngine;
using UnityEngine.InputSystem;

namespace PurrNet.Examples.TopDownShooter
{
    public class PlayerShoot : NetworkIdentity
    {
        [SerializeField] private Bullet bulletPrefab;

        protected override void OnSpawned(bool asServer)
        {
            enabled = isOwner;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            var trs = transform;

            UnityProxy.Instantiate(bulletPrefab, trs.position + trs.forward * 0.5f + Vector3.up * 0.7f, trs.rotation);
        }
    }
}

#endif
