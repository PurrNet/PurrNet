using UnityEngine;

namespace NetworkTransformFilterTest
{
    public class OrbitalMotion : FilterTestMotion
    {
        [Tooltip("Radius along the first axis of the orbit plane. Equal radii give a circle, different radii an ellipse.")]
        [SerializeField, Min(0f)] private float _radiusA = 5f;

        [Tooltip("Radius along the second axis of the orbit plane.")]
        [SerializeField, Min(0f)] private float _radiusB = 5f;

        [SerializeField] private float _degreesPerSecond = 45f;
        [SerializeField] private Vector3 _planeNormal = Vector3.up;

        [Tooltip("Aligns forward with the direction of travel. Overrides angular velocity.")]
        [SerializeField] private bool _faceVelocity = true;

        [Tooltip("Degrees per second around each axis when not facing velocity. Zero keeps the initial rotation.")]
        [SerializeField] private Vector3 _angularVelocity;

        private Vector3 _center;
        private Vector3 _axisA;
        private Vector3 _axisB;
        private Vector3 _normal;
        private Quaternion _startRotation;

        protected override void CaptureInitialState()
        {
            _startRotation = transform.rotation;
            _normal = _planeNormal.sqrMagnitude > 0f ? _planeNormal.normalized : Vector3.up;

            _axisA = Vector3.Cross(_normal, Vector3.forward);
            if (_axisA.sqrMagnitude < 0.001f)
                _axisA = Vector3.Cross(_normal, Vector3.right);
            _axisA.Normalize();
            _axisB = Vector3.Cross(_normal, _axisA).normalized;

            _center = transform.position - _axisA * _radiusA;
        }

        protected override void Apply(float time)
        {
            float angle = time * _degreesPerSecond * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            transform.position = _center + _axisA * (cos * _radiusA) + _axisB * (sin * _radiusB);

            if (_faceVelocity)
            {
                var tangent = _axisB * (cos * _radiusB) - _axisA * (sin * _radiusA);
                if (tangent.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(tangent.normalized, _normal);
            }
            else if (_angularVelocity != Vector3.zero)
            {
                transform.rotation = Quaternion.Euler(_angularVelocity * time) * _startRotation;
            }
        }
    }
}
