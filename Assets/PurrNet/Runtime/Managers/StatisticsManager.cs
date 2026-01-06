using System;
using System.Collections.Generic;
using System.Text;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    public partial class StatisticsManager : MonoBehaviour
    {
        [Range(0.05f, 1f)] public float checkInterval = 0.33f;
        [SerializeField] private StatisticsPlacement placement = StatisticsPlacement.None;
        [SerializeField] private StatisticsDisplayType _displayType = StatisticsDisplayType.Ping | StatisticsDisplayType.Usage;
        [SerializeField] private StatisticsDisplayTarget _displayTarget = StatisticsDisplayTarget.Editor | StatisticsDisplayTarget.Build;
        [SerializeField] private float fontSize = 13f;
        [SerializeField] private Color textColor = Color.white;

        public int ping { get; private set; }
        public int jitter { get; private set; }
        public int packetLoss { get; private set; }
        public float upload { get; private set; }
        public float download { get; private set; }

        private NetworkManager _networkManager;
        private PlayersBroadcaster _playersClientBroadcaster;
        private PlayersBroadcaster _playersServerBroadcaster;
        private TickManager _tickManager;
        private GUIStyle _labelStyle;
        private const int PADDING = 10;
        private float LineHeight => fontSize * 1.25f;

        public bool connectedServer { get; private set; }
        public bool connectedClient { get; private set; }

        private const float PING_HISTORY_TIME = 2.5f; // Seconds
        private const int PACKET_HISTORY_SECONDS = 5;
        private const int MAX_PACKET_HISTORY = 200;
        private const float JITTER_SAMPLE_TIME = 2.5f;


        private int[] _pingStats;
        private readonly float[] _sentPacketTimes = new float[MAX_PACKET_HISTORY];
        private readonly float[] _receivedPacketTimes = new float[MAX_PACKET_HISTORY];
        private readonly Queue<(float time, int value)> _pingVisibleHistory = new();

        private int _pingHistorySize;
        private int _pingIndex;
        private int _pingCount;
        private int _sentPacketIndex;
        private int _receivedPacketIndex;
        private int _sentPacketCount;
        private int _receivedPacketCount;
        private uint _lastPingSendTick;

        private int _packetsToSendPerSec = 20;
        private uint _lastPacketSendTick;
        private uint _packetSequence;

        private float _totalDataReceived;
        private float _totalDataSent;
        private float _lastDataCheckTime;

        private string _cachedPingText = "Ping: 0ms";
        private string _cachedJitterText = "Jitter: 0ms";
        private string _cachedPacketLossText = "Packet Loss: 0%";
        private string _cachedUploadText = "Upload: 0.000KB/s";
        private string _cachedDownloadText = "Download: 0.000KB/s";
        private float _lastGuiUpdateTime;
        private const float GUI_UPDATE_INTERVAL = 0.1f;

        private readonly StringBuilder _stringBuilder = new();

        private void Awake()
        {
            _networkManager = NetworkManager.main;
            _networkManager.onServerConnectionState += OnServerConnectionState;
            _networkManager.onClientConnectionState += OnClientConnectionState;
        }

        private void Start()
        {
            if (!_networkManager)
            {
                PurrLogger.LogError($"StatisticsManager failed to find a NetworkManager in the scene. Disabling...");
                enabled = false;
                return;
            }

            UpdateLabelStyle();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UpdateLabelStyle();
#endif
        }

        private void UpdateLabelStyle()
        {
            _labelStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(fontSize),
                normal = { textColor = textColor },
                alignment = (placement == StatisticsPlacement.TopRight || placement == StatisticsPlacement.BottomRight)
                    ? TextAnchor.UpperRight
                    : TextAnchor.UpperLeft
            };
        }

        private void OnDestroy()
        {
            if (_networkManager)
            {
                _networkManager.onServerConnectionState -= OnServerConnectionState;
                _networkManager.onClientConnectionState -= OnClientConnectionState;
                _networkManager.transport.transport.onDataReceived -= OnDataReceived;
                _networkManager.transport.transport.onDataSent -= OnDataSent;
                if (_networkManager.TryGetModule(out TickManager tm, false))
                    tm.onTick -= OnClientTick;
            }

            if (_playersServerBroadcaster != null)
            {
                _playersServerBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                _playersServerBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
            }

            if (_playersClientBroadcaster != null)
            {
                _playersClientBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                _playersClientBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
            }

            ServerUnsubscribe_ServerStats();
            ClientUnsubscribe_ServerStats();
        }

        private void OnGUI()
        {
#if UNITY_EDITOR
            if (!_displayTarget.HasFlag(StatisticsDisplayTarget.Editor))
                return;
#else
            if (!_displayTarget.HasFlag(StatisticsDisplayTarget.Build))
                return;
#endif
            if (placement == StatisticsPlacement.None || !connectedClient)
                return;

            UpdateCachedStrings();

            var position = GetPosition();
            float currentY = position.y;
            const float labelWidth = 200;
            Rect rect = new(position.x, currentY, labelWidth, LineHeight);
            if (_displayType.HasFlag(StatisticsDisplayType.Ping))
            {
                GUI.Label(rect, _cachedPingText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedJitterText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedPacketLossText, _labelStyle);
                rect.y += LineHeight;
            }

            if (_displayType.HasFlag(StatisticsDisplayType.Usage))
            {
                GUI.Label(rect, _cachedUploadText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedDownloadText, _labelStyle);
                rect.y += LineHeight;
            }

            if (_displayType.HasFlag(StatisticsDisplayType.ServerStats))
            {
                GUI.Label(rect, "Server Stats:", _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedServerMaxFpsText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedServerAvgFpsText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedServerMinFpsText, _labelStyle);
            }

            if (_displayType.HasFlag(StatisticsDisplayType.Version))
            {
                rect.y += LineHeight;
                GUI.Label(rect, "Version: " + NetworkManager.version, _labelStyle);
            }
        }

        private void UpdateCachedStrings()
        {
            var currentTime = Time.time;
            if (currentTime - _lastGuiUpdateTime < GUI_UPDATE_INTERVAL)
                return;

            _lastGuiUpdateTime = currentTime;

            _stringBuilder.Clear().Append("Ping: ").Append(ping).Append("ms");
            _cachedPingText = _stringBuilder.ToString();

            _stringBuilder.Clear().Append("Jitter: ").Append(jitter).Append("ms");
            _cachedJitterText = _stringBuilder.ToString();

            _stringBuilder.Clear().Append("Packet Loss: ").Append(packetLoss).Append('%');
            _cachedPacketLossText = _stringBuilder.ToString();

            _stringBuilder.Clear().Append("Upload: ").Append(upload.ToString("F3")).Append("KB/s");
            _cachedUploadText = _stringBuilder.ToString();

            _stringBuilder.Clear().Append("Download: ").Append(download.ToString("F3")).Append("KB/s");
            _cachedDownloadText = _stringBuilder.ToString();
            UpdateCachedStrings_ServerStats();
        }

        private Vector2 GetPosition()
        {
            var x = placement switch
            {
                StatisticsPlacement.TopLeft or StatisticsPlacement.BottomLeft => PADDING,
                _ => Screen.width - 200 - PADDING
            };

            var y = placement switch
            {
                StatisticsPlacement.TopLeft or StatisticsPlacement.TopRight => PADDING,
                _ => Screen.height - GetStatsHeight() - PADDING
            };

            return new Vector2(x, y);
        }

        private int GetStatsHeight()
        {
            int lines = 0;

            if (_displayType.HasFlag(StatisticsDisplayType.Ping))
                lines += 3;

            if (_displayType.HasFlag(StatisticsDisplayType.Usage))
                lines += 2;

            if (_displayType.HasFlag(StatisticsDisplayType.ServerStats))
                lines += 3;

            return Mathf.CeilToInt(LineHeight * lines);
        }

        private void Update()
        {
            if (Time.time - _lastDataCheckTime >= 1f)
            {
                download = _totalDataReceived / 1024f;
                upload = _totalDataSent / 1024f;
                _totalDataReceived = 0;
                _totalDataSent = 0;
                _lastDataCheckTime = Time.time;
            }

            if (connectedClient)
                CleanupOldPackets(Time.time);

            ServerStatsUpdate();
        }

        private void OnServerConnectionState(ConnectionState state)
        {
            _playersServerBroadcaster = _networkManager.GetModule<PlayersBroadcaster>(true);
            _pingHistorySize = Mathf.RoundToInt(_networkManager.tickModule.tickRate * PING_HISTORY_TIME);
            _pingStats = new int[_pingHistorySize];

            connectedServer = state == ConnectionState.Connected;

            if (state != ConnectionState.Connected)
            {
                _playersServerBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                _playersServerBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
                _networkManager.transport.transport.onDataReceived -= OnDataReceived;
                _networkManager.transport.transport.onDataSent -= OnDataSent;
                ServerUnsubscribe_ServerStats();
                return;
            }

            _playersServerBroadcaster.Subscribe<PingMessage>(ReceivePing);
            _playersServerBroadcaster.Subscribe<PacketMessage>(ReceivePacket);
            _networkManager.transport.transport.onDataReceived += OnDataReceived;
            _networkManager.transport.transport.onDataSent += OnDataSent;
            ServerSubscribe_ServerStats();
        }

        private void OnClientConnectionState(ConnectionState state)
        {
            if (!_networkManager.TryGetModule<TickManager>(false, out _tickManager))
                return;

            _playersClientBroadcaster = _networkManager.GetModule<PlayersBroadcaster>(false);
            _pingHistorySize = Mathf.RoundToInt(_networkManager.tickModule.tickRate * PING_HISTORY_TIME);
            _pingStats = new int[_pingHistorySize];

            connectedClient = state == ConnectionState.Connected;

            if (state != ConnectionState.Connected)
            {
                _playersClientBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                _playersClientBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
                _tickManager.onTick -= OnClientTick;
                if (!connectedServer)
                {
                    _networkManager.transport.transport.onDataReceived -= OnDataReceived;
                    _networkManager.transport.transport.onDataSent -= OnDataSent;
                }

                ClientUnsubscribe_ServerStats();
                ResetStatistics();
                return;
            }

            _playersClientBroadcaster.Subscribe<PingMessage>(ReceivePing);
            _playersClientBroadcaster.Subscribe<PacketMessage>(ReceivePacket);
            _tickManager.onTick += OnClientTick;

            if (!connectedServer)
            {
                _networkManager.transport.transport.onDataReceived += OnDataReceived;
                _networkManager.transport.transport.onDataSent += OnDataSent;
            }

            if (_tickManager.tickRate < _packetsToSendPerSec)
                _packetsToSendPerSec = Mathf.Max(5, _tickManager.tickRate / 2);

            ClientSubscribe_ServerStats();
            ResetStatistics();
        }

        private void ResetStatistics()
        {
            ping = 0;
            jitter = 0;
            packetLoss = 0;
            _pingIndex = 0;
            _pingCount = 0;
            _sentPacketIndex = 0;
            _receivedPacketIndex = 0;
            _sentPacketCount = 0;
            _receivedPacketCount = 0;
            _packetSequence = 0;

            for (int i = 0; i < MAX_PACKET_HISTORY; i++)
            {
                _sentPacketTimes[i] = 0;
                _receivedPacketTimes[i] = 0;
            }

            ResetStatistics_ServerStats();
        }

        private void OnClientTick()
        {
            if (!gameObject.activeInHierarchy)
                return;

            HandlePingCheck();
            HandlePacketCheck();
        }

        private void HandlePingCheck()
        {
            if (_lastPingSendTick + _tickManager.TimeToTick(checkInterval) > _tickManager.localTick)
                return;

            SendPingCheck();
        }

        private void SendPingCheck()
        {
            _playersClientBroadcaster.SendToServer(
                new PingMessage {
                    sendTime = _tickManager.localTick,
                    realSendTime = Time.time
                },
                Channel.ReliableUnordered);
            _lastPingSendTick = _tickManager.localTick;
        }

        private void ReceivePing(PlayerID sender, PingMessage msg, bool asServer)
        {
            if (asServer)
            {
                _playersServerBroadcaster.Send(sender,
                    new PingMessage {
                        sendTime = msg.sendTime,
                        realSendTime = msg.realSendTime
                    },
                    Channel.ReliableUnordered);
                return;
            }

            float sentTime = msg.realSendTime;
            int currentPing = Mathf.Max(0, Mathf.FloorToInt((Time.time - sentTime) * 1000));
            var multiplier = 2f;
            if (_networkManager.isServer)
                multiplier = 3f;
            currentPing -= Mathf.Min(currentPing, Mathf.RoundToInt((_tickManager.tickDelta * multiplier) * 1000));

            _pingStats[_pingIndex] = currentPing;
            _pingIndex = (_pingIndex + 1) % _pingHistorySize;
            if (_pingCount < _pingHistorySize)
                _pingCount++;

            CalculatePingStats();
        }

        private void CalculatePingStats()
        {
            if (_pingCount == 0)
            {
                ping = 0;
                jitter = 0;
                return;
            }

            int sum = 0;
            for (int i = 0; i < _pingCount; i++)
                sum += _pingStats[i];

            ping = sum / _pingCount;

            float now = Time.time;
            _pingVisibleHistory.Enqueue((now, ping));

            while (_pingVisibleHistory.Count > 0 && now - _pingVisibleHistory.Peek().time > JITTER_SAMPLE_TIME)
                _pingVisibleHistory.Dequeue();

            if (_pingVisibleHistory.Count > 1)
            {
                int min = int.MaxValue;
                int max = int.MinValue;

                using (var enumerator = _pingVisibleHistory.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        var item = enumerator.Current;
                        if (item.value < min) min = item.value;
                        if (item.value > max) max = item.value;
                    }
                }

                jitter = max - min;
            }
            else
            {
                jitter = 0;
            }
        }

        private void HandlePacketCheck()
        {
            if (_lastPacketSendTick + _tickManager.TimeToTick(1f / _packetsToSendPerSec) > _tickManager.localTick)
                return;

            _lastPacketSendTick = _tickManager.localTick;

            _sentPacketTimes[_sentPacketIndex] = Time.time;
            _sentPacketIndex = (_sentPacketIndex + 1) % MAX_PACKET_HISTORY;
            if (_sentPacketCount < MAX_PACKET_HISTORY)
                _sentPacketCount++;

            _playersClientBroadcaster.SendToServer(new PacketMessage { sequenceId = _packetSequence++ }, Channel.Unreliable);

            CalculatePacketLoss();
        }

        private void CalculatePacketLoss()
        {
            float currentTime = Time.time;
            float cutoffTime = currentTime - PACKET_HISTORY_SECONDS;

            int validSentPackets = 0;
            int validReceivedPackets = 0;

            for (int i = 0; i < _sentPacketCount; i++)
            {
                if (_sentPacketTimes[i] > 0 && _sentPacketTimes[i] >= cutoffTime)
                    validSentPackets++;
            }

            for (int i = 0; i < _receivedPacketCount; i++)
            {
                if (_receivedPacketTimes[i] > 0 && _receivedPacketTimes[i] >= cutoffTime)
                    validReceivedPackets++;
            }

            if (validSentPackets > 0)
            {
                int lossPercentage = 100 - (validReceivedPackets * 100 / validSentPackets);
                packetLoss = Mathf.Clamp(lossPercentage, 0, 100);

                if (_tickManager.localTick < 3 * _tickManager.tickRate)
                    packetLoss = 0;
            }
            else
            {
                packetLoss = 0;
            }
        }

        private void CleanupOldPackets(float currentTime)
        {
            float cutoffTime = currentTime - PACKET_HISTORY_SECONDS - 1f;

            for (int i = 0; i < MAX_PACKET_HISTORY; i++)
            {
                if (_sentPacketTimes[i] > 0 && _sentPacketTimes[i] < cutoffTime)
                {
                    _sentPacketTimes[i] = 0;
                }

                if (_receivedPacketTimes[i] > 0 && _receivedPacketTimes[i] < cutoffTime)
                {
                    _receivedPacketTimes[i] = 0;
                }
            }
        }

        private void ReceivePacket(PlayerID sender, PacketMessage msg, bool asServer)
        {
            if (asServer)
            {
                _playersServerBroadcaster.Send(sender, new PacketMessage { sequenceId = msg.sequenceId }, Channel.Unreliable);
                return;
            }

            _receivedPacketTimes[_receivedPacketIndex] = Time.time;
            _receivedPacketIndex = (_receivedPacketIndex + 1) % MAX_PACKET_HISTORY;
            if (_receivedPacketCount < MAX_PACKET_HISTORY)
                _receivedPacketCount++;
        }

        private void OnDataReceived(Connection conn, ByteData data, bool asServer)
        {
            _totalDataReceived += data.length;
        }

        private void OnDataSent(Connection conn, ByteData data, bool asServer)
        {
            _totalDataSent += data.length;
        }

        public struct PingMessage : Packing.IPackedAuto
        {
            public uint sendTime;
            public float realSendTime;
        }

        public struct PacketMessage : Packing.IPackedAuto
        {
            public uint sequenceId;
        }

        public enum StatisticsPlacement
        {
            None,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        [Flags]
        public enum StatisticsDisplayType
        {
            Ping = 1 << 0,
            Usage = 1 << 1,
            ServerStats = 1 << 2,
            Version = 1 << 3,
        }

        [Flags]
        public enum StatisticsDisplayTarget
        {
            Editor = 1 << 1,
            Build = 1 << 2,
        }
    }
}
