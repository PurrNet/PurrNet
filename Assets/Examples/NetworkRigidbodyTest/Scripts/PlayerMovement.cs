using PurrNet;
using UnityEngine;

namespace NetworkRigidbodyTest
{
    public class PlayerMovement : NetworkIdentity
    {
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _acceleration = 50f;
        [SerializeField] private float _jumpForce = 5f;
        [SerializeField] private float _groundDrag = 8f;
        [SerializeField] private float _airDrag = 0.5f;
        [SerializeField] private float _groundCheckDistance = 0.35f;
        [SerializeField] private bool _velocityChangeBased;
        [SerializeField] private bool _routeThroughNetworkRigidbody = true;
        [SerializeField] private NetworkRigidbody _rb;

        private Rigidbody _rigidbody;
        private bool _isGrounded;
        private bool _wasGrounded;

        protected override void OnSpawned()
        {
            base.OnSpawned();
            _rigidbody = GetComponent<Rigidbody>();
            enabled = isOwner;

            if (isOwner)
            {
                var renderer = GetComponentInChildren<Renderer>();
                renderer.material.color = Color.green;
            }
        }

        private void Update()
        {
            _isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, _groundCheckDistance);

            if (_isGrounded != _wasGrounded)
            {
                if(_velocityChangeBased)
                    SetDrag(_isGrounded ? _groundDrag : _airDrag);
                _wasGrounded = _isGrounded;
            }

            if (Input.GetKeyDown(KeyCode.Space) && _isGrounded)
                AddForce(Vector3.up * (GetMass() * _jumpForce), ForceMode.Impulse);
        }

        private void FixedUpdate()
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;

            if (_velocityChangeBased)
            {
                if (input.magnitude < 0.1f)
                    return;

                var velocity = GetLinearVelocity();
                var currentHoriz = new Vector3(velocity.x, 0f, velocity.z);
                var velocityDelta = input * _moveSpeed - currentHoriz;
                var maxDelta = _acceleration * Time.fixedDeltaTime;
                AddForce(Vector3.ClampMagnitude(velocityDelta, maxDelta), ForceMode.VelocityChange);
            }
            else
            {
                AddForce(input * (_moveSpeed * GetMass()));
            }
        }

        private float GetMass()
        {
            return _routeThroughNetworkRigidbody ? _rb.mass : _rigidbody.mass;
        }

        private Vector3 GetLinearVelocity()
        {
            if (_routeThroughNetworkRigidbody)
                return _rb.linearVelocity;

#if UNITY_6000_0_OR_NEWER
            return _rigidbody.linearVelocity;
#else
            return _rigidbody.velocity;
#endif
        }

        private void SetDrag(float value)
        {
            if (_routeThroughNetworkRigidbody)
            {
                _rb.drag = value;
                return;
            }

#if UNITY_6000_0_OR_NEWER
            _rigidbody.linearDamping = value;
#else
            _rigidbody.drag = value;
#endif
        }

        private void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (_routeThroughNetworkRigidbody)
                _rb.AddForce(force, mode);
            else
                _rigidbody.AddForce(force, mode);
        }
    }
}
