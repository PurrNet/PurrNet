using UnityEngine;

namespace NetworkTransformFilterTest
{
    public class SlerpPulseMotion : FilterTestMotion
    {
        [Tooltip("Rotation offset from the initial rotation that the object slerps to and from.")]
        [SerializeField] private Vector3 _rotationOffsetEuler = new Vector3(0f, 180f, 45f);
        [SerializeField, Min(0.01f)] private float _rotationCycleSeconds = 4f;

        [SerializeField] private Vector3 _scaleAmplitude = new Vector3(0.5f, 0.5f, 0.5f);
        [SerializeField, Min(0.01f)] private float _scaleCycleSeconds = 2f;

        [SerializeField] private Vector3 _bobAmplitude = new Vector3(0f, 1f, 0f);
        [SerializeField, Min(0.01f)] private float _bobCycleSeconds = 3f;

        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Quaternion _targetRotation;
        private Vector3 _startScale;

        protected override void CaptureInitialState()
        {
            _startPosition = transform.position;
            _startRotation = transform.rotation;
            _targetRotation = _startRotation * Quaternion.Euler(_rotationOffsetEuler);
            _startScale = transform.localScale;
        }

        protected override void Apply(float time)
        {
            float rotT = Mathf.PingPong(time * 2f / _rotationCycleSeconds, 1f);
            transform.rotation = Quaternion.Slerp(_startRotation, _targetRotation, rotT);

            float scaleWave = Mathf.Sin(time * 2f * Mathf.PI / _scaleCycleSeconds);
            transform.localScale = _startScale + _scaleAmplitude * scaleWave;

            float bobWave = Mathf.Sin(time * 2f * Mathf.PI / _bobCycleSeconds);
            transform.position = _startPosition + _bobAmplitude * bobWave;
        }
    }
}
