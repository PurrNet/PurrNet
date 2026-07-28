using PurrNet;
using PurrNet.Logging;
using UnityEngine;

namespace NetworkRigidbodyTest
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkRigidbody))]
    public class FirstPersonRigidbodyController : NetworkIdentity
    {
        public enum YawApplyMode
        {
            MoveRotation,
            AngularVelocity,
            DirectRotation
        }

        [Header("Look")]
        [SerializeField] private float _lookSensitivity = 2f;
        [SerializeField] private float _minPitch = -85f;
        [SerializeField] private float _maxPitch = 85f;
        [Tooltip("Optional eye anchor. When empty, an offset above the body is used.")]
        [SerializeField] private Transform _head;
        [SerializeField] private float _headHeight = 0.6f;

        [Tooltip("How body yaw is written to the rigidbody. This is the value NetworkRigidbody syncs, so it directly affects how observers see rotation.")]
        [SerializeField] private YawApplyMode _yawApplyMode = YawApplyMode.MoveRotation;
        [Tooltip("Max turn rate in degrees per second when using the AngularVelocity yaw mode.")]
        [SerializeField] private float _yawTurnSpeed = 900f;

        [Header("Move")]
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _acceleration = 60f;
        [SerializeField] private float _airAccelerationScale = 0.25f;
        [SerializeField] private float _jumpHeight = 1.2f;
        [SerializeField] private float _groundDrag = 6f;
        [SerializeField] private float _airDrag = 0f;

        [Header("Ground Check")]
        [SerializeField] private float _groundCheckRadius = 0.3f;
        [SerializeField] private float _groundCheckDistance = 0.4f;
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Setup")]
        [SerializeField] private bool _lockCursor = true;
        [Tooltip("Sets Rigidbody.interpolation to Interpolate on every peer. Without it, observers see the body move at the physics step rate.")]
        [SerializeField] private bool _forceRigidbodyInterpolation = true;

        private NetworkRigidbody _networkRigidbody;
        private Rigidbody _rigidbody;
        private readonly RaycastHit[] _groundCastHits = new RaycastHit[8];

        private Camera _camera;
        private Transform _cameraTransform;
        private Transform _cameraOriginalParent;
        private Vector3 _cameraOriginalLocalPosition;
        private Quaternion _cameraOriginalLocalRotation;
        private bool _hasCamera;

        private float _yaw;
        private float _pitch;
        private Vector2 _moveInput;
        private bool _jumpQueued;
        private bool _isGrounded;
        private bool _wasGrounded;
        private bool _cursorLocked;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _networkRigidbody = GetComponent<NetworkRigidbody>();
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            if (_forceRigidbodyInterpolation && _rigidbody.interpolation == RigidbodyInterpolation.None)
                _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            enabled = isOwner;

            if (!isOwner)
                return;

            if (!_networkRigidbody.ownerAuth)
                PurrLogger.LogWarning($"{nameof(FirstPersonRigidbodyController)} on {gameObject.name} drives the rigidbody locally but its NetworkRigidbody is not owner authoritative. Enable Owner Auth or move input to the server.", this);

            _yaw = transform.eulerAngles.y;
            _pitch = 0f;

            AcquireCamera();
            SetCursorLocked(_lockCursor);
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();

            if (!isOwner)
                return;

            ReleaseCamera();
            SetCursorLocked(false);
        }

        private void OnDisable()
        {
            if (_cursorLocked)
                SetCursorLocked(false);
        }

        private void Update()
        {
            ReadLookInput();
            ReadMoveInput();
        }

        private void FixedUpdate()
        {
            _isGrounded = CheckGrounded();

            if (_isGrounded != _wasGrounded)
            {
                _networkRigidbody.drag = _isGrounded ? _groundDrag : _airDrag;
                _wasGrounded = _isGrounded;
            }

            ApplyYaw();
            ApplyMove();
            ApplyJump();
        }

        private void LateUpdate()
        {
            if (!_hasCamera)
                return;

            var headPosition = _head ? _head.position : transform.position + Vector3.up * _headHeight;
            _cameraTransform.SetPositionAndRotation(headPosition, Quaternion.Euler(_pitch, _yaw, 0f));
        }

        private void ReadLookInput()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SetCursorLocked(false);
            else if (Input.GetMouseButtonDown(0) && !_cursorLocked && _lockCursor)
                SetCursorLocked(true);

            if (!_cursorLocked && _lockCursor)
                return;

            _yaw += Input.GetAxisRaw("Mouse X") * _lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * _lookSensitivity, _minPitch, _maxPitch);

            if (_yaw > 360f) _yaw -= 360f;
            else if (_yaw < -360f) _yaw += 360f;
        }

        private void ReadMoveInput()
        {
            _moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (_moveInput.sqrMagnitude > 1f)
                _moveInput.Normalize();

            if (Input.GetKeyDown(KeyCode.Space))
                _jumpQueued = true;
        }

        private void ApplyYaw()
        {
            var target = Quaternion.Euler(0f, _yaw, 0f);

            switch (_yawApplyMode)
            {
                case YawApplyMode.MoveRotation:
                    _networkRigidbody.MoveRotation(target);
                    break;

                case YawApplyMode.AngularVelocity:
                    var delta = Mathf.DeltaAngle(_rigidbody.rotation.eulerAngles.y, _yaw);
                    var rate = Mathf.Clamp(delta / Time.fixedDeltaTime, -_yawTurnSpeed, _yawTurnSpeed);
                    _networkRigidbody.angularVelocity = new Vector3(0f, rate * Mathf.Deg2Rad, 0f);
                    break;

                case YawApplyMode.DirectRotation:
                    _networkRigidbody.rotation = target;
                    break;
            }
        }

        private void ApplyMove()
        {
            var wish = Quaternion.Euler(0f, _yaw, 0f) * new Vector3(_moveInput.x, 0f, _moveInput.y);
            var velocity = _networkRigidbody.linearVelocity;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var targetHorizontal = wish * _moveSpeed;

            var acceleration = _isGrounded ? _acceleration : _acceleration * _airAccelerationScale;
            var maxDelta = acceleration * Time.fixedDeltaTime;
            var change = Vector3.ClampMagnitude(targetHorizontal - horizontal, maxDelta);

            if (change.sqrMagnitude > 0f)
                _networkRigidbody.AddForce(change, ForceMode.VelocityChange);
        }

        private void ApplyJump()
        {
            if (!_jumpQueued)
                return;

            _jumpQueued = false;

            if (!_isGrounded)
                return;

            var gravity = Mathf.Abs(Physics.gravity.y);
            var speed = Mathf.Sqrt(2f * gravity * Mathf.Max(0.01f, _jumpHeight));
            var velocity = _networkRigidbody.linearVelocity;

            _networkRigidbody.AddForce(new Vector3(0f, speed - velocity.y, 0f), ForceMode.VelocityChange);
        }

        private bool CheckGrounded()
        {
            var origin = _rigidbody.position + Vector3.up * (_groundCheckRadius + 0.05f);
            var count = Physics.SphereCastNonAlloc(
                origin,
                _groundCheckRadius,
                Vector3.down,
                _groundCastHits,
                _groundCheckDistance + 0.05f,
                _groundMask,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var hit = _groundCastHits[i];
                if (hit.rigidbody == _rigidbody)
                    continue;
                if (hit.collider && hit.collider.transform.IsChildOf(transform))
                    continue;

                return true;
            }

            return false;
        }

        private void AcquireCamera()
        {
            _camera = Camera.main;

            if (!_camera)
            {
                PurrLogger.LogWarning($"{nameof(FirstPersonRigidbodyController)} on {gameObject.name} found no camera tagged MainCamera. Look input still runs, but nothing follows it.", this);
                return;
            }

            _cameraTransform = _camera.transform;
            _cameraOriginalParent = _cameraTransform.parent;
            _cameraOriginalLocalPosition = _cameraTransform.localPosition;
            _cameraOriginalLocalRotation = _cameraTransform.localRotation;
            _cameraTransform.SetParent(null, true);
            _hasCamera = true;
        }

        private void ReleaseCamera()
        {
            if (!_hasCamera)
                return;

            _hasCamera = false;

            if (!_cameraTransform)
                return;

            _cameraTransform.SetParent(_cameraOriginalParent, false);
            _cameraTransform.localPosition = _cameraOriginalLocalPosition;
            _cameraTransform.localRotation = _cameraOriginalLocalRotation;
        }

        private void SetCursorLocked(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
