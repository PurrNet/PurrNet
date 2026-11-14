using System;
using System.Threading.Tasks;

namespace PurrNet.Essentials.Relay
{
    public sealed class RelayClient : RelaySession
    {
        public event Action<ArraySegment<byte>> onData;

        public RelayClient(RelayOptions options) : base(options)
        {
        }

        protected override async Task<(string host, RelayJoinDetails details)> Resolve(Func<bool> cancelled)
        {
            var join = await Retry(
                () => RelayApi.Join(options.masterServer, options.roomName, options.projectKey), cancelled);
            return (join.host, join);
        }

        public void Send(ArraySegment<byte> data, RelayDelivery delivery = RelayDelivery.ReliableOrdered)
        {
            if (state != RelayState.Connected || data.Array == null)
                return;

            if (!isUdp)
            {
                link.Send(data.Array, data.Offset, data.Count, delivery);
                return;
            }

            var buffer = SendBuffer(1 + data.Count);
            buffer[0] = (byte)delivery;
            Buffer.BlockCopy(data.Array, data.Offset, buffer, 1, data.Count);
            link.Send(buffer, 0, 1 + data.Count, delivery);
        }

        protected override void OnData(ArraySegment<byte> data)
        {
            if (data.Array == null || data.Count == 0 || state == RelayState.Disconnected)
                return;

            if (state == RelayState.Connected)
            {
                onData?.Invoke(data);
                return;
            }

            switch (data.Array[data.Offset])
            {
                case ServerAuthenticated:
                    MarkConnected();
                    break;
                case ServerAuthenticationFailed:
                    Close(RelayCloseReason.AuthenticationFailed, "The relay rejected the room secret.");
                    break;
            }
        }
    }
}
