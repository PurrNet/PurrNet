#if UNITY_PHYSICS_3D

using PurrNet.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PurrNet.Examples.TopDownShooter
{
    public class PlayerMovement : PlayerIdentity<PlayerMovement>
    {
        [SerializeField] private Reference<Transform> _visuals;
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float acceleration = 4f;
        [SerializeField] private float jumpForce = 1.5f;
        [SerializeField] private float gravity = 9.81f;

        private Transform _lastGround;
        private Vector3 _lastGroundPos;

        private CharacterController _controller;
        private float rotationSpeed;
        private float _verticalVelocity;
        private Vector3 currentMove;

        public CharacterController controller => _controller;

        private void Awake()
        {
            if (!TryGetComponent(out _controller))
                PurrLogger.LogError($"Failed to get component '{nameof(CharacterController)}' on '{name}'.", this);
        }

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned();
            if (!asServer)
                enabled = isOwner;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_visuals.value && isSpawned)
                Destroy(_visuals.value.gameObject);
        }

        private void CheckGround()
        {
            if (Physics.Raycast(transform.position, Vector3.down, out var hit, .1f) && hit.transform.GetComponent<NetworkIdentity>())
            {
                if (_lastGround != hit.transform)
                {
                    _lastGround = hit.transform;
                    _visuals.value.SetParent(_lastGround, true);
                }
                _lastGroundPos = _lastGround.position;
            }
            else if (_lastGround)
            {
                _lastGround = null;
                _visuals.value.SetParent(transform, true);
            }
        }

        private void Update()
        {
            if (!_controller)
                return;

            var keyboard = Keyboard.current;
            Vector2 input = Vector2.zero;

            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1;
            }

            Vector3 targetMove = new Vector3(input.x, 0, input.y).normalized;

            currentMove = Vector3.Lerp(currentMove, targetMove, acceleration * Time.deltaTime);

            if (_controller.isGrounded)
                _verticalVelocity = -gravity * Time.deltaTime;
            else
                _verticalVelocity -= gravity * Time.deltaTime;

            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && _controller.isGrounded)
                _verticalVelocity = jumpForce;

            currentMove.y = _verticalVelocity;
            Vector3 extra = default;

            if (_lastGround)
            {
                extra = _lastGround.position - _lastGroundPos;
                extra.y = 0;
            }

            CheckGround();

            _controller.Move(currentMove * (moveSpeed * Time.deltaTime) + extra);

            if (input != Vector2.zero)
            {
                float targetAngle = Mathf.Atan2(currentMove.x, currentMove.z) * Mathf.Rad2Deg;
                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref rotationSpeed, 0.1f);
                transform.rotation = Quaternion.Euler(0, angle, 0);
            }
        }

        private void LateUpdate()
        {
            if (_visuals == null || _visuals.value == null)
                return;
            _visuals.value.position = transform.position;
            _visuals.value.rotation = transform.rotation;
        }
    }
}

#endif
