using System;
using PurrNet;
using UnityEngine;

namespace NetworkRigidbodyTest
{
    public class PlayerMovement : NetworkIdentity
    {
        [SerializeField] private float _moveForce = 5;
        [SerializeField] private float _jumpForce = 5;
        [SerializeField] private NetworkRigidbody _rb;
    
        protected override void OnSpawned()
        {
            base.OnSpawned();
            enabled = isOwner;
        }

        private void FixedUpdate()
        {
            var direction = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;
            _rb.AddForce(direction * (_moveForce * _rb.mass));
            
            if(Input.GetKeyDown(KeyCode.Space))
                _rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
        }
    }

}