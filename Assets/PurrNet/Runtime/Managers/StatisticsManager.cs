using System;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Statistics Manager")]
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

        private const float PING_HISTORY_TIME = 2.5f;
        private const int PACKET_HISTORY_SECONDS = 5;
        private const int MAX_PACKET_HISTORY = 200;
        private const float JITTER_SAMPLE_TIME = 2.5f;
        private const int MAX_JITTER_SAMPLES = 128;

        private int[] _pingStats;
        private readonly float[] _sentPacketTimes = new float[MAX_PACKET_HISTORY];
        private readonly float[] _receivedPacketTimes = new float[MAX_PACKET_HISTORY];

        private readonly float[] _jitterTimes = new float[MAX_JITTER_SAMPLES];
        private readonly int[] _jitterValues = new int[MAX_JITTER_SAMPLES];
        private int _jitterHead;
        private int _jitterCount;

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

        private int _cachedPing = -1;
        private int _cachedJitter = -1;
        private int _cachedPacketLoss = -1;
        private float _cachedUpload = -1f;
        private float _cachedDownload = -1f;

        private readonly char[] _charBuffer = new char[64];
        private string _cachedPingText = "Ping: 0ms";
        private string _cachedJitterText = "Jitter: 0ms";
        private string _cachedPacketLossText = "Packet Loss: 0%";
        private string _cachedUploadText = "Upload: 0.000KB/s";
        private string _cachedDownloadText = "Download: 0.000KB/s";

        private bool _labelStyleInitialized;

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

            EnsureLabelStyle();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                _labelStyleInitialized = false;
                EnsureLabelStyle();
            }
#endif
        }

        private void EnsureLabelStyle()
        {
            if (_labelStyleInitialized && _labelStyle != null)
            {
                _labelStyle.fontSize = Mathf.RoundToInt(fontSize);
                _labelStyle.normal.textColor = textColor;
                _labelStyle.alignment = (placement == StatisticsPlacement.TopRight || placement == StatisticsPlacement.BottomRight)
                    ? TextAnchor.UpperRight
                    : TextAnchor.UpperLeft;
                return;
            }

            _labelStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(fontSize),
                normal = { textColor = textColor },
                alignment = (placement == StatisticsPlacement.TopRight || placement == StatisticsPlacement.BottomRight)
                    ? TextAnchor.UpperRight
                    : TextAnchor.UpperLeft
            };
            _labelStyleInitialized = true;
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

            EnsureLabelStyle();
            UpdateCachedStrings();

            var position = GetPosition();
            const float labelWidth = 200;
            Rect rect = new(position.x, position.y, labelWidth, LineHeight);

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
            if (ping != _cachedPing)
            {
                _cachedPing = ping;
                _cachedPingText = FormatStat("Ping: ", ping, "ms");
            }

            if (jitter != _cachedJitter)
            {
                _cachedJitter = jitter;
                _cachedJitterText = FormatStat("Jitter: ", jitter, "ms");
            }

            if (packetLoss != _cachedPacketLoss)
            {
                _cachedPacketLoss = packetLoss;
                _cachedPacketLossText = FormatStat("Packet Loss: ", packetLoss, "%");
            }

            if (!Mathf.Approximately(upload, _cachedUpload))
            {
                _cachedUpload = upload;
                _cachedUploadText = FormatStatFloat("Upload: ", upload, "KB/s");
            }

            if (!Mathf.Approximately(download, _cachedDownload))
            {
                _cachedDownload = download;
                _cachedDownloadText = FormatStatFloat("Download: ", download, "KB/s");
            }

            UpdateCachedStrings_ServerStats();
        }

        private string FormatStat(string prefix, int value, string suffix)
        {
            int pos = 0;

            for (int i = 0; i < prefix.Length; i++)
                _charBuffer[pos++] = prefix[i];

            pos = WriteInt(_charBuffer, pos, value);

            for (int i = 0; i < suffix.Length; i++)
                _charBuffer[pos++] = suffix[i];

            return new string(_charBuffer, 0, pos);
        }

        private string FormatStatFloat(string prefix, float value, string suffix)
        {
            int pos = 0;

            for (int i = 0; i < prefix.Length; i++)
                _charBuffer[pos++] = prefix[i];

            int intPart = (int)value;
            int fracPart = Mathf.Abs((int)((value - intPart) * 1000));

            pos = WriteInt(_charBuffer, pos, intPart);
            _charBuffer[pos++] = '.';

            if (fracPart < 100) _charBuffer[pos++] = '0';
            if (fracPart < 10) _charBuffer[pos++] = '0';
            pos = WriteInt(_charBuffer, pos, fracPart);

            for (int i = 0; i < suffix.Length; i++)
                _charBuffer[pos++] = suffix[i];

            return new string(_charBuffer, 0, pos);
        }

        private static int WriteInt(char[] buffer, int pos, int value)
        {
            if (value < 0)
            {
                buffer[pos++] = '-';
                value = -value;
            }

            if (value == 0)
            {
                buffer[pos++] = '0';
                return pos;
            }

            int start = pos;
            while (value > 0)
            {
                buffer[pos++] = (char)('0' + value % 10);
                value /= 10;
            }

            for (int i = start, j = pos - 1; i < j; i++, j--)
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);

            return pos;
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
            connectedServer = state == ConnectionState.Connected;

            switch (state)
            {
                case ConnectionState.Disconnected:
                    if (_playersServerBroadcaster == null)
                        return;
                    _playersServerBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                    _playersServerBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
                    _playersServerBroadcaster = null;
                    _networkManager.transport.transport.onDataReceived -= OnDataReceived;
                    _networkManager.transport.transport.onDataSent -= OnDataSent;
                    ServerUnsubscribe_ServerStats();
                    return;
                case ConnectionState.Connected:
                    _pingHistorySize = Mathf.RoundToInt(_networkManager.tickModule.tickRate * PING_HISTORY_TIME);
                    _pingStats = new int[_pingHistorySize];
                    _playersServerBroadcaster = _networkManager.GetModule<PlayersBroadcaster>(true);
                    _playersServerBroadcaster.Subscribe<PingMessage>(ReceivePing);
                    _playersServerBroadcaster.Subscribe<PacketMessage>(ReceivePacket);
                    _networkManager.transport.transport.onDataReceived += OnDataReceived;
                    _networkManager.transport.transport.onDataSent += OnDataSent;
                    ServerSubscribe_ServerStats();
                    break;
                case ConnectionState.Connecting:
                case ConnectionState.Disconnecting:
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
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
            _jitterHead = 0;
            _jitterCount = 0;

            for (int i = 0; i < MAX_PACKET_HISTORY; i++)
            {
                _sentPacketTimes[i] = 0;
                _receivedPacketTimes[i] = 0;
            }

            for (int i = 0; i < MAX_JITTER_SAMPLES; i++)
            {
                _jitterTimes[i] = 0;
                _jitterValues[i] = 0;
            }

            _cachedPing = -1;
            _cachedJitter = -1;
            _cachedPacketLoss = -1;
            _cachedUpload = -1f;
            _cachedDownload = -1f;

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

            _jitterTimes[_jitterHead] = now;
            _jitterValues[_jitterHead] = ping;
            _jitterHead = (_jitterHead + 1) % MAX_JITTER_SAMPLES;
            if (_jitterCount < MAX_JITTER_SAMPLES)
                _jitterCount++;

            float cutoff = now - JITTER_SAMPLE_TIME;
            int min = int.MaxValue;
            int max = int.MinValue;
            int validCount = 0;

            for (int i = 0; i < _jitterCount; i++)
            {
                if (_jitterTimes[i] >= cutoff)
                {
                    int val = _jitterValues[i];
                    if (val < min) min = val;
                    if (val > max) max = val;
                    validCount++;
                }
            }

            jitter = validCount > 1 ? max - min : 0;
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
