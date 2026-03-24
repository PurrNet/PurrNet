using UnityEngine;

namespace PurrNet
{
    [CreateAssetMenu(menuName = "PurrNet/Network Rigidbody Settings")]
    public class NetworkRigidbodySettings : ScriptableObject
    {
        [Header("Position Correction")]
        [Tooltip("Per-axis correction strength (natural frequency). Higher = more aggressive correction on that axis.")]
        public Vector3 positionStrength = Vector3.one * 5f;

        [Tooltip("Per-axis distance over which position correction ramps from zero to full strength. Larger values give softer correction on that axis.")]
        public Vector3 positionCorrectionRange = Vector3.one * 2f;

        [Tooltip("Per-axis position error below which correction stops. 0 = always correct that axis.")]
        public Vector3 positionAcceptableError = Vector3.zero;

        [Header("Rotation Correction")]
        [Tooltip("Per-axis rotation correction strength (natural frequency). Higher = more aggressive rotation correction on that axis.")]
        public Vector3 rotationStrength = Vector3.one * 3f;

        [Tooltip("Per-axis rotation error (degrees) over which correction ramps from zero to full strength. Similar to position correction range but for rotation.")]
        public Vector3 rotationCorrectionRange = Vector3.one * 90f;

        [Tooltip("Per-axis rotation error (degrees) below which rotation correction stops on that axis. Negative to disable correction on that axis.")]
        public Vector3 rotationAcceptableError = Vector3.one;
    }
}
