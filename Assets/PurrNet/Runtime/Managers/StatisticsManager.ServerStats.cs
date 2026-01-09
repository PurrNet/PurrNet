using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    public partial class StatisticsManager
    {
        private const float SEND_INTERVAL = 1f;
        private const float STATS_HISTORY_TIME = 2.5f;
        private const int MAX_FPS_SAMPLES = 256;
        
        private string _cachedServerAvgFpsText = "Avg FPS: 0";
        private string _cachedServerMaxFpsText = "Max FPS: 0";
        private string _cachedServerMinFpsText = "Min FPS: 0";
        
        private readonly float[] _fpsTimes = new float[MAX_FPS_SAMPLES];
        private readonly float[] _fpsValues = new float[MAX_FPS_SAMPLES];
        private int _fpsHead;
        private int _fpsCount;
        
        private float _lastStatsSendTime;
        
        private int _cachedServerAvg = -1;
        private int _cachedServerMax = -1;
        private int _cachedServerMin = -1;
        
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
            
            _fpsTimes[_fpsHead] = now;
            _fpsValues[_fpsHead] = fps;
            _fpsHead = (_fpsHead + 1) % MAX_FPS_SAMPLES;
            if (_fpsCount < MAX_FPS_SAMPLES)
                _fpsCount++;

            if (now - _lastStatsSendTime < SEND_INTERVAL || _fpsCount == 0)
                return;

            _lastStatsSendTime = now;

            float cutoff = now - STATS_HISTORY_TIME;
            float sum = 0f;
            float min = float.MaxValue;
            float max = float.MinValue;
            int validCount = 0;

            for (int i = 0; i < _fpsCount; i++)
            {
                if (_fpsTimes[i] >= cutoff)
                {
                    float val = _fpsValues[i];
                    sum += val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                    validCount++;
                }
            }

            if (validCount == 0)
                return;

            int avg = Mathf.RoundToInt(sum / validCount);
            int maxInt = Mathf.RoundToInt(max);
            int minInt = Mathf.RoundToInt(min);

            _playersServerBroadcaster?.SendToAll(new ServerStatsPacket { avgFps = avg, maxFps = maxInt, minFpx = minInt }, Channel.Unreliable);
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

            if (_lastServerStats.max != _cachedServerMax)
            {
                _cachedServerMax = _lastServerStats.max;
                _cachedServerMaxFpsText = FormatStat("Max FPS: ", _lastServerStats.max, "");
            }

            if (_lastServerStats.avg != _cachedServerAvg)
            {
                _cachedServerAvg = _lastServerStats.avg;
                _cachedServerAvgFpsText = FormatStat("Avg FPS: ", _lastServerStats.avg, "");
            }

            if (_lastServerStats.min != _cachedServerMin)
            {
                _cachedServerMin = _lastServerStats.min;
                _cachedServerMinFpsText = FormatStat("Min FPS: ", _lastServerStats.min, "");
            }
        }

        private void ResetStatistics_ServerStats()
        {
            _fpsHead = 0;
            _fpsCount = 0;
            
            for (int i = 0; i < MAX_FPS_SAMPLES; i++)
            {
                _fpsTimes[i] = 0;
                _fpsValues[i] = 0;
            }
            
            _cachedServerAvg = -1;
            _cachedServerMax = -1;
            _cachedServerMin = -1;
        }

        private struct ServerStatsPacket : Packing.IPackedAuto
        {
            public int avgFps;
            public int maxFps;
            public int minFpx;
        }
    }
}