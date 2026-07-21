#if UNITY_PHYSICS_2D
using UnityEngine;

namespace PurrNet.Modules
{
    public struct Collider2DState
    {
        public Vector2 position;
        public float rotation;
        public Vector2 scale;
        public bool enabled;


        public Collider2DState(Collider2D collider)
        {
            var trs = collider.transform;
            trs.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);
            position = pos;
            rotation = rot.eulerAngles.z;
            scale = trs.localScale;
            enabled = collider.enabled;
        }

        public Collider2DState Interpolate(Collider2DState stateB, float tickFraction)
        {
            return new Collider2DState
            {
                position = Vector2.Lerp(position, stateB.position, tickFraction),
                rotation = Mathf.LerpAngle(rotation, stateB.rotation, tickFraction),
                scale = Vector2.Lerp(scale, stateB.scale, tickFraction),
                enabled = tickFraction > 0.5f ? stateB.enabled : enabled
            };
        }
    }
}
#endif
