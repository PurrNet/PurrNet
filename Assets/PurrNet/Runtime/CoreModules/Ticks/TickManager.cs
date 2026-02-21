using System;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet.Modules
{
    public class TickManager : INetworkModule, IUpdate
    {
        /// <summary>
        /// Tracks local ticks starting from client connection to the server for synchronization.
        /// </summary>
        public uint localTick { get; private set; }

        /// <summary>
        /// Tracks the ticks aligned with the servers ticks for synchronization.
        /// </summary>
        public uint syncedTick
        {
            get
            {
                if (_asServer)
                    return localTick;
                return _syncedTick;
            }
            private set => _syncedTick = value;
        }

        /// <summary>
        /// This is the round trip time. Local time it takes for the client to get a response from the server.
        /// This includes 1 tick for packing and 1 tick for unpacking, meaning you'll have 2 ticks delay calculated into the rtt.
        /// For actual ping, utilize the statistics manager, or make up for these 2 ticks delay manually.
        /// </summary>
        public double rtt { get; private set; }

        /// <summary>
        /// Uses floating point values for ticks to allow fractional updates, allowing to get precise tick timing within update
        /// </summary>
        public double floatingPoint { get; private set; }

        public double syncedPreciseTick => syncedTick + floatingPoint;

        public double rollbackTick
        {
            get
            {
                var halfRttInTicks = TimeToPreciseTick(rtt) / 2;
                return syncedPreciseTick - halfRttInTicks;
            }
        }

        /// <summary>
        /// Gives the exact step of the tick, including the floating point.
        /// </summary>
        public double localPreciseTick => localTick + floatingPoint;

        /// <summary>
        /// The amount of ticks per second
        /// </summary>
        public int tickRate { get; private set; }

        /// <summary>
        /// Time between each tick as a float
        /// </summary>
        public readonly float tickDelta;

        /// <summary>
        /// Time between each tick as a double
        /// </summary>
        public readonly double tickDeltaDouble;

        public event Action onPreTick, onTick, onPostTick;
        public event Action onReliablePreTick, onReliableTick, onReliablePostTick;

        private readonly INetworkManager _networkManager;
        private readonly bool _asServer;

        private uint _syncedTick;
        private float _lastSyncTime = -99;
        private double _lastTickTime;
        private const int MaxTickPerFrame = 5;

        private readonly BroadcastModule _broadcaster;

        public TickManager(int tickRate, INetworkManager nm, BroadcastModule broadcaster, bool asServer)
        {
            _asServer = asServer;
            _lastTickTime = Time.unscaledTimeAsDouble;
            _networkManager = nm;
            tickDelta = 1f / tickRate;
            tickDeltaDouble = 1d / tickRate;
            _broadcaster = broadcaster;
            this.tickRate = tickRate;
        }

        public void Enable(bool asServer)
        {
            if (asServer)
            {
                _broadcaster.Subscribe<TickManagerRequestLocalTick>(OnClientRequestedPing);
            }
            else
            {
                _broadcaster.Subscribe<TickManagerResponseLocalTick>(OnServerRespondedPing);
            }
        }

        public void Disable(bool asServer)
        {
            if (asServer)
            {
                _broadcaster.Unsubscribe<TickManagerRequestLocalTick>(OnClientRequestedPing);
            }
            else
            {
                _broadcaster.Unsubscribe<TickManagerResponseLocalTick>(OnServerRespondedPing);
            }
        }

        public void Update()
        {
            HandleTick();
            floatingPoint = (Time.unscaledTimeAsDouble - _lastTickTime) * tickRate;

            if (_networkManager.isServer || !_networkManager.isClient)
                return;

            var rules = _networkManager.networkRules;

            if (!rules)
                return;

            if (_lastSyncTime + rules.GetSyncedTickUpdateInterval() < Time.unscaledTime)
            {
                _lastSyncTime = Time.unscaledTime;
                HandleTickSync();
            }
        }

        private void HandleTick()
        {
            int ticksHandled = 0;

            while (_lastTickTime + tickDeltaDouble <= Time.unscaledTimeAsDouble)
            {
                _lastTickTime += tickDeltaDouble;
                _syncedTick++;
                localTick++;
                floatingPoint = 0;

                bool triggerNormalTicks = ticksHandled < MaxTickPerFrame;

                if (triggerNormalTicks)
                    onPreTick?.Invoke();
                onReliablePreTick?.Invoke();

                if (triggerNormalTicks)
                    onTick?.Invoke();
                onReliableTick?.Invoke();

                if (triggerNormalTicks)
                    onPostTick?.Invoke();
                onReliablePostTick?.Invoke();

                ticksHandled++;
            }
        }

        /// <summary>
        /// Converts the input tick to float time
        /// </summary>
        /// <param name="tick">The amount of ticks to convert to time</param>
        public float TickToTime(uint tick)
        {
            return tick / (float)tickRate;
        }

        /// <summary>
        /// Converts the precise input tick to float time
        /// </summary>
        /// <param name="preciseTick">The precise tick to convert</param>
        public float PreciseTickToTime(double preciseTick)
        {
            return (float)(preciseTick / tickRate);
        }

        /// <summary>
        /// Converts the input float time to ticks
        /// </summary>
        /// <param name="time">The amount of time to convert</param>
        public uint TimeToTick(float time)
        {
            return (uint)Math.Round(time * tickRate);
        }

        /// <summary>
        /// Converts the input float time to precise ticks (double)
        /// </summary>
        /// <param name="time">And amount of time to convert</param>
        public double TimeToPreciseTick(float time)
        {
            return time * tickRate;
        }

        /// <summary>
        /// Converts the input float time to precise ticks (double)
        /// </summary>
        /// <param name="time">And amount of time to convert</param>
        public double TimeToPreciseTick(double time)
        {
            return time * tickRate;
        }

        private void HandleTickSync()
        {
            try
            {
                float requestSendTime = Time.unscaledTime;

                _broadcaster.SendToServer(new TickManagerRequestLocalTick
                {
                    requestTime = requestSendTime
                });
            }
            catch
            {
                //PurrLogger.LogError($"Failed to sync tick: {e}");
            }
        }

        private void OnServerRespondedPing(Connection conn, TickManagerResponseLocalTick data, bool asServer)
        {
            rtt = Time.unscaledTime - data.requestTime;
            float halfRTT = (float)rtt / 2;
            syncedTick = data.tick + TimeToTick(halfRTT);
        }

        private void OnClientRequestedPing(Connection conn, TickManagerRequestLocalTick data, bool asServer)
        {
            var packet = new TickManagerResponseLocalTick
            {
                requestTime = data.requestTime,
                tick = localTick
            };

            _broadcaster.Send(conn, packet);
        }
    }

    public struct TickManagerRequestLocalTick : IPackedAuto
    {
        public float requestTime;
    }

    public struct TickManagerResponseLocalTick : IPackedAuto
    {
        public float requestTime;
        public uint tick;
    }
}
