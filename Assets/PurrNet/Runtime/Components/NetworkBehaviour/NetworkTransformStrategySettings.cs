using UnityEngine;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkTransformStrategySettings",
        menuName = "PurrNet/Network Transform Strategy Settings")]
    public class NetworkTransformStrategySettings : ScriptableObject
    {
        [Tooltip("Maximum time between sends while motion stays reconstructible by receivers. " +
                 "Higher values save more bandwidth but add more interpolation delay at low " +
                 "extrapolation values and larger corrections at high ones.")]
        [Range(0.05f, 1f)]
        public float maxSendInterval = 0.2f;

        [Tooltip("How far receivers project received motion toward real time. " +
                 "0 renders only received data, adding delay but never showing a guess. " +
                 "1 projects fully to real time, causing rubberbanding when motion changes.")]
        [Range(0f, 1f)]
        public float extrapolation;
    }
}
