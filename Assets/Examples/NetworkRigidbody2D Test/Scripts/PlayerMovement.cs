using System;
using PurrNet;
using UnityEngine;

namespace NetworkRigidbody2DTest
{
    public class PlayerMovement : PlayerIdentity<NetworkRigidbody2DTest.PlayerMovement>
    {
        [SerializeField] private float _moveSpeed = 5;
        
        [SerializeField] private Rigidbody2D _rb;

        private void OnValidate()
        {
            TryGetComponent(out _rb);
        }

        private void FixedUpdate()
        {
            if (!isOwner)
                return;
            
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"), 0f).normalized;

            _rb.AddForce(input * (_moveSpeed * _rb.mass));
        }
    }
}
