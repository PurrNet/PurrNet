using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PurrNet.Essentials.Relay
{
    public sealed class RelayHost : RelaySession
    {
        private const byte SendKeepAliveCode = 0;
        private const byte SendOneCode = 1;
        private const byte KickPlayerCode = 2;

        private readonly List<int> _clients = new List<int>();
        private readonly byte[] _small = new byte[5];
        private RelayJoinDetails _allocation;
        private string _allocationKey;
        private float _sinceKeepAlive;

        public IReadOnlyList<int> clients => _clients;

        public event Action<int> onClientConnected;
        public event Action<int> onClientDisconnected;
        public event Action<int, ArraySegment<byte>> onData;

        public RelayHost(RelayOptions options) : base(options)
        {
        }

        protected override async Task<(string host, RelayJoinDetails details)> Resolve(Func<bool> cancelled)
        {
            string region = options.region;
            string host = options.host;

            if (string.IsNullOrEmpty(region) || string.IsNullOrEmpty(host))
            {
                var closest = await Retry(() => RelayApi.FindClosest(options.masterServer), cancelled);
                region = closest.region;
                host = closest.host;
            }

            string key = $"{host}|{options.roomName}|{options.projectKey}";

            if (_allocationKey != key || !await RoomExists())
            {
                _allocation = await Retry(
                    () => RelayApi.Allocate(options.masterServer, region, options.roomName, options.projectKey),
                    cancelled);
                _allocationKey = key;
            }

            return (host, _allocation);
        }

        private async Task<bool> RoomExists()
        {
            try
            {
                await RelayApi.Join(options.masterServer, options.roomName, options.projectKey);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Send(int client, ArraySegment<byte> data, RelayDelivery delivery = RelayDelivery.ReliableOrdered)
        {
            if (state != RelayState.Connected || data.Array == null)
                return;

            bool udp = isUdp;
            int header = udp ? 6 : 5;
            var buffer = SendBuffer(header + data.Count);

            buffer[0] = SendOneCode;
            WriteInt(buffer, 1, client);
            if (udp)
                buffer[5] = (byte)delivery;
            Buffer.BlockCopy(data.Array, data.Offset, buffer, header, data.Count);

            link.Send(buffer, 0, header + data.Count, delivery);
        }

        public void Kick(int client)
        {
            if (state != RelayState.Connected)
                return;

            _small[0] = KickPlayerCode;
            WriteInt(_small, 1, client);
            link.Send(_small, 0, 5, RelayDelivery.ReliableSequenced);
        }

        public void SendKeepAlive()
        {
            if (state != RelayState.Connected)
                return;

            _small[0] = SendKeepAliveCode;
            link.Send(_small, 0, 1, RelayDelivery.ReliableOrdered);
            _sinceKeepAlive = 0f;
        }

        public override void Update(float deltaSeconds)
        {
            if (state == RelayState.Connected && options.keepAliveSeconds > 0f)
            {
                _sinceKeepAlive += deltaSeconds;
                if (_sinceKeepAlive >= options.keepAliveSeconds)
                    SendKeepAlive();
            }

            base.Update(deltaSeconds);
        }

        protected override void OnData(ArraySegment<byte> data)
        {
            if (data.Array == null || data.Count == 0 || state == RelayState.Disconnected)
                return;

            switch (data.Array[data.Offset])
            {
                case ServerAuthenticated:
                    _sinceKeepAlive = 0f;
                    MarkConnected();
                    break;
                case ServerAuthenticationFailed:
                    Close(RelayCloseReason.AuthenticationFailed, "The relay rejected the host secret.");
                    break;
                case ServerClientConnected:
                {
                    int count = (data.Count - 1) / 4;
                    for (int i = 0; i < count; i++)
                    {
                        int client = ReadInt(data.Array, data.Offset + 1 + i * 4);
                        if (_clients.Contains(client))
                            continue;
                        _clients.Add(client);
                        onClientConnected?.Invoke(client);
                    }

                    break;
                }
                case ServerClientDisconnected:
                {
                    if (data.Count < 5)
                        break;

                    int client = ReadInt(data.Array, data.Offset + 1);
                    if (_clients.Remove(client))
                        onClientDisconnected?.Invoke(client);
                    break;
                }
                case ServerClientData:
                {
                    if (data.Count <= 5)
                        break;

                    int client = ReadInt(data.Array, data.Offset + 1);
                    onData?.Invoke(client, new ArraySegment<byte>(data.Array, data.Offset + 5, data.Count - 5));
                    break;
                }
            }
        }

        protected override void OnClosed()
        {
            _clients.Clear();
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt(byte[] buffer, int offset)
        {
            return buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24;
        }
    }
}
