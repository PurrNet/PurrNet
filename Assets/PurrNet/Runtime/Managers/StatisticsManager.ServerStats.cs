using System.Collections.Generic;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    public partial class StatisticsManager
    {
        private const float SEND_INTERVAL = 1f; //1 second
        private const float STATS_HISTORY_TIME = 2.5f; //2.5 seconds

        private string _cachedServerAvgFpsText = "Avg FPS: 0";
        private string _cachedServerMaxFpsText = "Max FPS: 0";
        private string _cachedServerMinFpsText = "Min FPS: 0";

        private readonly Queue<(float t, float fps)> _fpsHistory = new();
        private float _lastStatsSendTime;

        private (int min, int avg, int max) _lastServerStats;
        private bool _dirtyServerStats;

        private void ServerSubscribe_ServerStats()
        {

        }

        private void ClientSubscribe_ServerStats()
        {
            _playersClientBroadcaster?.Subscribe<ServerStatsPacket>(ReceiveServerStats);
        }

        private void ServerUnsubscribe_ServerStats()
        {

        }

        private void ServerStatsUpdate()
        {
            if (!_networkManager.isServer)
                return;

            float now = Time.unscaledTime;
            float fps = 1f / Time.unscaledDeltaTime;
            _fpsHistory.Enqueue((now, fps));

            while (_fpsHistory.Count > 0 && now - _fpsHistory.Peek().t > STATS_HISTORY_TIME)
                _fpsHistory.Dequeue();

            if (now - _lastStatsSendTime < SEND_INTERVAL || _fpsHistory.Count == 0)
                return;

            _lastStatsSendTime = now;

            float sum = 0;
            float maxValue = float.MinValue;
            float minValue = float.MaxValue;

            foreach (var entry in _fpsHistory)
            {
                float entryFps = entry.fps;
                sum += entryFps;
                if (entryFps > maxValue) maxValue = entryFps;
                if (entryFps < minValue) minValue = entryFps;
            }

            int avg = Mathf.RoundToInt(sum / _fpsHistory.Count);
            int max = Mathf.RoundToInt(maxValue);
            int min = Mathf.RoundToInt(minValue);

            _playersServerBroadcaster?.SendToAll(new ServerStatsPacket { avgFps = avg, maxFps = max, minFpx = min }, Channel.Unreliable);
        }

        private void ClientUnsubscribe_ServerStats()
        {
            _playersClientBroadcaster?.Unsubscribe<ServerStatsPacket>(ReceiveServerStats);
        }

        private void ReceiveServerStats(PlayerID player, ServerStatsPacket data, bool asServer)
        {
            _lastServerStats.avg = data.avgFps;
            _lastServerStats.max = data.maxFps;
            _lastServerStats.min = data.minFpx;
            _dirtyServerStats = true;
        }

        private void UpdateCachedStrings_ServerStats()
        {
            if (!_dirtyServerStats) return;
            _dirtyServerStats = false;

            _stringBuilder.Clear().Append("Max FPS: ").Append(_lastServerStats.max);
            _cachedServerMaxFpsText = _stringBuilder.ToString();

            _stringBuilder.Clear().Append("Avg FPS: ").Append(_lastServerStats.avg);
            _cachedServerAvgFpsText = _stringBuilder.ToString();

            _stringBuilder.Clear().Append("Min FPS: ").Append(_lastServerStats.min);
            _cachedServerMinFpsText = _stringBuilder.ToString();
        }

        private void ResetStatistics_ServerStats()
        {

        }

        private struct ServerStatsPacket
        {
            public int avgFps;
            public int maxFps;
            public int minFpx;
        }
    }
}
