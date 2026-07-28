using UnityEngine;

namespace NetworkTransformFilterTest
{
    public class RacetrackMotion : FilterTestMotion
    {
        [SerializeField, Min(1f)] private float _straightLength = 20f;
        [SerializeField, Min(0.5f)] private float _turnRadius = 5f;
        [SerializeField, Min(0f)] private float _speed = 5f;
        [SerializeField] private Vector3 _direction = Vector3.right;
        [SerializeField] private Vector3 _planeNormal = Vector3.up;
        [SerializeField] private bool _faceDirection = true;

        private Vector3 _start;
        private Vector3 _forward;
        private Vector3 _side;
        private Vector3 _normal;
        private Quaternion _startRotation;

        protected override void CaptureInitialState()
        {
            _start = transform.position;
            _startRotation = transform.rotation;

            _normal = _planeNormal.sqrMagnitude > 0f ? _planeNormal.normalized : Vector3.up;
            _forward = Vector3.ProjectOnPlane(
                _direction.sqrMagnitude > 0f ? _direction : Vector3.right, _normal).normalized;
            if (_forward.sqrMagnitude < 0.001f)
                _forward = Vector3.ProjectOnPlane(Vector3.forward, _normal).normalized;
            _side = Vector3.Cross(_normal, _forward).normalized;
        }

        protected override void Apply(float time)
        {
            float halfTurn = Mathf.PI * _turnRadius;
            float perimeter = 2f * _straightLength + 2f * halfTurn;
            float s = Mathf.Repeat(time * _speed, perimeter);

            Vector3 position;
            Vector3 tangent;

            if (s < _straightLength)
            {
                position = _start + _forward * s;
                tangent = _forward;
            }
            else if (s < _straightLength + halfTurn)
            {
                float angle = (s - _straightLength) / _turnRadius;
                var center = _start + _forward * _straightLength + _side * _turnRadius;
                position = center - _side * (_turnRadius * Mathf.Cos(angle)) +
                           _forward * (_turnRadius * Mathf.Sin(angle));
                tangent = _side * Mathf.Sin(angle) + _forward * Mathf.Cos(angle);
            }
            else if (s < 2f * _straightLength + halfTurn)
            {
                float back = s - _straightLength - halfTurn;
                position = _start + _forward * _straightLength + _side * (2f * _turnRadius) - _forward * back;
                tangent = -_forward;
            }
            else
            {
                float angle = (s - 2f * _straightLength - halfTurn) / _turnRadius;
                var center = _start + _side * _turnRadius;
                position = center + _side * (_turnRadius * Mathf.Cos(angle)) -
                           _forward * (_turnRadius * Mathf.Sin(angle));
                tangent = -_side * Mathf.Sin(angle) - _forward * Mathf.Cos(angle);
            }

            transform.position = position;

            if (_faceDirection && tangent.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(tangent.normalized, _normal);
            else if (!_faceDirection)
                transform.rotation = _startRotation;
        }

        [ContextMenu("Randomize")]
        private void Randomize()
        {
            _straightLength = Random.Range(5f, 20f);
            _turnRadius = Random.Range(2f, 7f);
            _speed = Random.Range(2f, 10f);
        }
    }
}
