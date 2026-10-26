#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PurrNet.Transports;
using Steamworks;

namespace PurrNet.Steam
{
    // A false Send/Pump result requires disconnecting to avoid losing reliable data.
    internal sealed class SteamSendQueue
    {
        internal const int MaxQueuedBytes = 1024 * 1024;
        internal const int MaxQueuedMessages = 1024;
        internal const double MaxQueueAgeSeconds = 10;
        internal const double MaxCountedCheckGapSeconds = 0.5;

        internal delegate EResult NativeSend(IntPtr data, uint length, int flags);

        private readonly NativeSend _send;
        private readonly Func<double> _clock;
        private readonly Queue<PendingMessage> _pending = new Queue<PendingMessage>();
        private SteamSendStatistics _statistics;
        private double _blockedSeconds;
        private double _longestCheckGap;
        private double _lastCheckAt;

        private readonly struct PendingMessage
        {
            public readonly ByteData data;
            public readonly double queuedAt;

            public PendingMessage(ByteData data, double queuedAt)
            {
                this.data = data;
                this.queuedAt = queuedAt;
            }
        }

        public string failureReason { get; private set; }

        public SteamSendStatistics statistics => _statistics;

        /// <summary>
        /// True when the last failed send was refused because Steam had already closed the connection.
        /// </summary>
        public bool peerClosed => _statistics.lastSendResult == (int)EResult.k_EResultNoConnection;

        public SteamSendQueue(NativeSend send, Func<double> clock)
        {
            _send = send;
            _clock = clock;
        }

        public bool Send(ByteData data, Channel channel, out bool accepted)
        {
            accepted = false;
            if (!CheckAge())
                return false;

            bool reliable = channel != Channel.Unreliable;
            if (reliable && _pending.Count > 0)
                return accepted = Enqueue(data);

            // Reserve capacity for reliable retries before accepting unreliable traffic.
            if (!reliable && _pending.Count > 0)
            {
                if (!Pump())
                    return false;
                if (_pending.Count > 0)
                {
                    _statistics.droppedUnreliableMessages++;
                    return true;
                }
            }

            var flags = reliable
                ? Constants.k_nSteamNetworkingSend_Reliable
                : Constants.k_nSteamNetworkingSend_Unreliable;
            var result = SendNative(data, flags);
            if (result == EResult.k_EResultOK)
                return accepted = true;

            if (reliable && result == EResult.k_EResultLimitExceeded)
                return accepted = Enqueue(data);

            if (!reliable && (result == EResult.k_EResultIgnored || result == EResult.k_EResultLimitExceeded))
            {
                _statistics.droppedUnreliableMessages++;
                return true;
            }

            return Fail($"Steam rejected a send: {result}.");
        }

        public bool Pump()
        {
            if (!CheckAge())
                return false;

            while (_pending.Count > 0)
            {
                var next = _pending.Peek();
                var result = SendNative(next.data, Constants.k_nSteamNetworkingSend_Reliable);
                if (result == EResult.k_EResultLimitExceeded)
                    return true;
                if (result != EResult.k_EResultOK)
                    return Fail($"Steam rejected a reliable retry: {result}.");

                _pending.Dequeue();
                _statistics.queuedReliableBytes -= next.data.length;
                _statistics.queuedReliableMessages = _pending.Count;
            }

            return true;
        }

        private EResult SendNative(ByteData data, int flags)
        {
            var pin = GCHandle.Alloc(data.data, GCHandleType.Pinned);
            try
            {
                var result = _send(pin.AddrOfPinnedObject() + data.offset, (uint)data.length, flags);
                _statistics.lastSendResult = (int)result;
                if (result == EResult.k_EResultOK)
                    _statistics.acceptedMessages++;
                else
                    _statistics.rejectedSendAttempts++;
                return result;
            }
            finally
            {
                pin.Free();
            }
        }

        private bool Enqueue(ByteData data)
        {
            if (_pending.Count >= MaxQueuedMessages || data.length > MaxQueuedBytes - _statistics.queuedReliableBytes)
                return Fail("Steam reliable retry queue exceeded its 1 MiB / 1024 message limit.");

            // The caller may reuse the source buffer after Send.
            var copy = new byte[data.length];
            Array.Copy(data.data, data.offset, copy, 0, data.length);
            _pending.Enqueue(new PendingMessage(new ByteData(copy, 0, copy.Length), _clock()));
            _statistics.queuedReliableBytes += copy.Length;
            _statistics.queuedReliableMessages = _pending.Count;
            _statistics.deferredReliableMessages++;
            return true;
        }

        private bool CheckAge()
        {
            if (failureReason != null)
                return false;

            double now = _clock();
            if (_pending.Count == 0)
            {
                _blockedSeconds = 0;
                _longestCheckGap = 0;
                _lastCheckAt = now;
                return true;
            }

            double gap = now - _lastCheckAt;
            _lastCheckAt = now;
            if (gap > _longestCheckGap)
                _longestCheckGap = gap;
            _blockedSeconds += Math.Min(gap, MaxCountedCheckGapSeconds);

            if (_blockedSeconds >= MaxQueueAgeSeconds)
                return Fail($"Steam did not accept a queued reliable message within {MaxQueueAgeSeconds:0} seconds of sending " +
                            $"(oldest queued {now - _pending.Peek().queuedAt:0.#} s ago, longest gap between sends {_longestCheckGap:0.##} s).");
            return true;
        }

        private bool Fail(string reason)
        {
            failureReason = reason;
            Clear();
            return false;
        }

        public void Clear()
        {
            _pending.Clear();
            _statistics.queuedReliableBytes = 0;
            _statistics.queuedReliableMessages = 0;
        }
    }

    internal static class SteamConnectionStatus
    {
        internal static string Describe(HSteamNetConnection conn, bool dedicated, out bool peerGone)
        {
            peerGone = false;
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            EResult result;
            try
            {
                result = dedicated
                    ? SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes)
                    : SteamNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes);
            }
            catch (Exception e)
            {
                return $"status unavailable ({e.Message})";
            }

            if (result != EResult.k_EResultOK)
            {
                peerGone = true;
                return $"status unavailable ({result})";
            }

            peerGone = status.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
            return $"state {status.m_eState}, ping {status.m_nPing} ms, quality local {status.m_flConnectionQualityLocal:0.##} remote {status.m_flConnectionQualityRemote:0.##}, " +
                   $"out {status.m_flOutBytesPerSec / 1024f:0.#} KiB/s, in {status.m_flInBytesPerSec / 1024f:0.#} KiB/s, rate {status.m_nSendRateBytesPerSecond / 1024} KiB/s, " +
                   $"pending reliable {status.m_cbPendingReliable} B, unacked reliable {status.m_cbSentUnackedReliable} B";
        }
    }
}
#endif
