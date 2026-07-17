using UnityEngine;

namespace NetworkTransformFilterTest
{
    public class LinearMotion : FilterTestMotion
    {
        [SerializeField] private Vector3 _direction = Vector3.right;
        [SerializeField, Min(0f)] private float _distance = 10f;
        [SerializeField, Min(0f)] private float _speed = 2f;

        [Tooltip("Degrees per second around each axis. Zero keeps the initial rotation.")]
        [SerializeField] private Vector3 _angularVelocity;

        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Vector3 _normalizedDirection;

        protected override void CaptureInitialState()
        {
            _startPosition = transform.position;
            _startRotation = transform.rotation;
            _normalizedDirection = _direction.sqrMagnitude > 0f ? _direction.normalized : Vector3.right;
        }

        protected override void Apply(float time)
        {
            float travelled = _distance > 0f ? Mathf.PingPong(time * _speed, _distance) : 0f;
            transform.position = _startPosition + _normalizedDirection * travelled;

            if (_angularVelocity != Vector3.zero)
                transform.rotation = Quaternion.Euler(_angularVelocity * time) * _startRotation;
        }

        [ContextMenu("Randomize speed")]
        private void RandomizeSpeed()
        {
            _speed = Random.Range(0.2f, 5f);
        }
    }
}
