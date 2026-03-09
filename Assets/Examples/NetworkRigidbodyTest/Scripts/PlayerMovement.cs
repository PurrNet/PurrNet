using PurrNet;
using UnityEngine;

namespace NetworkRigidbodyTest
{
    public class PlayerMovement : NetworkIdentity
    {
        [SerializeField] private float _moveForce = 5;
        [SerializeField] private float _jumpForce = 5;
        [SerializeField] private float _acceleration = 50f;
        [SerializeField] private bool _velocityChangeBased;
        [SerializeField] private NetworkRigidbody _rb;

        protected override void OnSpawned()
        {
            base.OnSpawned();
            enabled = isOwner;

            if (isOwner)
            {
                var renderer = GetComponentInChildren<Renderer>();
                renderer.material.color = Color.green;
            }
        }

        private void FixedUpdate()
        {
            var direction = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;

            if (_velocityChangeBased)
            {
                if (direction.magnitude < 0.1f)
                    return;

                var currentHoriz = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                var velocityDelta = direction * _moveForce - currentHoriz;
                var maxDelta = _acceleration * Time.fixedDeltaTime;
                _rb.AddForce(Vector3.ClampMagnitude(velocityDelta, maxDelta), ForceMode.VelocityChange);
            }
            else
            {
                _rb.AddForce(direction * (_moveForce * _rb.mass));
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                _rb.AddForce(Vector3.up * (_rb.mass * _jumpForce), ForceMode.Impulse);
        }
    }
}
