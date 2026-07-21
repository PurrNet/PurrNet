#if UNITY_PHYSICS_3D
using UnityEngine;

namespace PurrNet.Modules
{
    public struct Collider3DState
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public bool enabled;

        public Collider3DState(Collider collider)
        {
            var trs = collider.transform;
            trs.GetPositionAndRotation(out position, out rotation);
            scale = trs.localScale;
            enabled = collider.enabled;
        }

        public Collider3DState Interpolate(Collider3DState stateB, float tickFraction)
        {
            return new Collider3DState
            {
                position = Vector3.Lerp(position, stateB.position, tickFraction),
                rotation = Quaternion.Slerp(rotation, stateB.rotation, tickFraction),
                scale = Vector3.Lerp(scale, stateB.scale, tickFraction),
                enabled = tickFraction < 0.5f ? enabled : stateB.enabled
            };
        }
    }
}
#endif
