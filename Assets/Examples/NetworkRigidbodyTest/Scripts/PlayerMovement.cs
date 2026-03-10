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
                _rb.drag = _isGrounded ? _groundDrag : _airDrag;
                _wasGrounded = _isGrounded;
            }

            if (Input.GetKeyDown(KeyCode.Space) && _isGrounded)
                _rb.AddForce(Vector3.up * (_rb.mass * _jumpForce), ForceMode.Impulse);
        }

        private void FixedUpdate()
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;
            var direction = transform.TransformDirection(input);

            if (_velocityChangeBased)
            {
                if (input.magnitude < 0.1f)
                    return;

                var currentHoriz = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                var velocityDelta = direction * _moveSpeed - currentHoriz;
                var maxDelta = _acceleration * Time.fixedDeltaTime;
                _rb.AddForce(Vector3.ClampMagnitude(velocityDelta, maxDelta), ForceMode.VelocityChange);
            }
            else
            {
                _rb.AddForce(direction * (_moveSpeed * _rb.mass));
            }
        }
    }
}
