using System;
using JetBrains.Annotations;
using UnityEngine;

namespace PurrNet.Transports
{
    [Serializable]
    public struct NetworkSimulation
    {
        [UsedImplicitly, Tooltip("When enabled, simulation settings will also apply in builds. Otherwise they are editor-only.")]
        public bool includeInBuild;

        [Tooltip("Simulate latency by holding packets for a random duration between min and max.")]
        public bool simulateLatency;

        [Tooltip("Minimum simulated latency in milliseconds.")]
        public int minLatency;

        [Tooltip("Maximum simulated latency in milliseconds.")]
        public int maxLatency;

        [Tooltip("Simulate packet loss by dropping a random percentage of packets.")]
        public bool simulatePacketLoss;

        [Tooltip("Chance of packet loss (1-100%).")]
        [Range(1, 100)]
        public int packetLossChance;

        public static NetworkSimulation @default => new()
        {
            includeInBuild = false,
            simulateLatency = false,
            minLatency = 30,
            maxLatency = 100,
            simulatePacketLoss = false,
            packetLossChance = 10
        };

        public bool isActive => simulateLatency || simulatePacketLoss;

        public bool ShouldApply()
        {
#if UNITY_EDITOR
            return true;
#else
            return includeInBuild;
#endif
        }
    }
}
