using PurrNet;
using UnityEngine;

namespace NetworkTransformFilterTest
{
    public enum MotionTiming
    {
        FixedUpdate,
        Update
    }

    public abstract class FilterTestMotion : NetworkBehaviour
    {
        [Tooltip("FixedUpdate keeps every tick sample exactly on the pattern. Update adds frame-time jitter relative to ticks.")]
        [SerializeField] private MotionTiming _timing = MotionTiming.FixedUpdate;

        [SerializeField, Min(0f)] private float _timeScale = 1f;

        private float _elapsed;
        private bool _hasInitialState;

        protected override void OnSpawned()
        {
            base.OnSpawned();
            _elapsed = 0f;
            CaptureInitialState();
            _hasInitialState = true;
        }

        protected abstract void CaptureInitialState();

        protected abstract void Apply(float time);

        private void Update()
        {
            if (_timing == MotionTiming.Update)
                Step(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (_timing == MotionTiming.FixedUpdate)
                Step(Time.fixedDeltaTime);
        }

        private void Step(float delta)
        {
            if (!_hasInitialState || !isSpawned || !isController)
                return;

            _elapsed += delta * _timeScale;
            Apply(_elapsed);
        }
    }
}
