using System;
using System.Net;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    public sealed class NetConnectRequestPacket
    {
        public const int HeaderSize = 18;
        // A peer that offers reliable repairs sets this bit of its connection time. Stock peers take the
        // time from DateTime ticks, which never reach it, and stock servers only compare and echo it.
        internal const long RepairsOffer = 1L << 62;
        public readonly long ConnectionTime;
        public byte ConnectionNumber;
        public readonly byte[] TargetAddress;
        public readonly NetDataReader Data;
        public readonly int PeerId;

        private NetConnectRequestPacket(long connectionTime, byte connectionNumber, int localId, byte[] targetAddress, NetDataReader data)
        {
            ConnectionTime = connectionTime;
            ConnectionNumber = connectionNumber;
            TargetAddress = targetAddress;
            Data = data;
            PeerId = localId;
        }

        internal bool OffersRepairs => (ConnectionTime & RepairsOffer) != 0;

        internal static int GetProtocolId(NetPacket packet) =>
            BitConverter.ToInt32(packet.RawData, 1);

        internal static NetConnectRequestPacket FromData(NetPacket packet)
        {
            if (packet.ConnectionNumber >= NetConstants.MaxConnectionNumber)
                return null;

            //Getting connection time for peer
            long connectionTime = BitConverter.ToInt64(packet.RawData, 5);

            //Get peer id
            int peerId = BitConverter.ToInt32(packet.RawData, 13);

            //Get target address
            int addrSize = packet.RawData[HeaderSize-1];
            if (addrSize != 16 && addrSize != 28)
                return null;
            byte[] addressBytes = new byte[addrSize];
            Buffer.BlockCopy(packet.RawData, HeaderSize, addressBytes, 0, addrSize);

            // Read data and create request
            var reader = new NetDataReader(null, 0, 0);
            if (packet.Size > HeaderSize+addrSize)
                reader.SetSource(packet.RawData, HeaderSize + addrSize, packet.Size);

            return new NetConnectRequestPacket(connectionTime, packet.ConnectionNumber, peerId, addressBytes, reader);
        }

        internal static NetPacket Make(ReadOnlySpan<byte> connectData, SocketAddress addressBytes, long connectTime, int localId)
        {
            //Make initial packet
            var packet = new NetPacket(PacketProperty.ConnectRequest, connectData.Length+addressBytes.Size);

            //Add data
            FastBitConverter.GetBytes(packet.RawData, 1, NetConstants.ProtocolId);
            FastBitConverter.GetBytes(packet.RawData, 5, connectTime);
            FastBitConverter.GetBytes(packet.RawData, 13, localId);
            packet.RawData[HeaderSize - 1] = (byte)addressBytes.Size;
            for (int i = 0; i < addressBytes.Size; i++)
                packet.RawData[HeaderSize + i] = addressBytes[i];
            connectData.CopyTo(packet.RawData.AsSpan(HeaderSize + addressBytes.Size));
            return packet;
        }
    }

    internal sealed class NetConnectAcceptPacket
    {
        public const int Size = 15;
        // Flags in byte 10. Stock peers reject anything but the reused flag, so the repairs flag only
        // answers a peer that offered repairs.
        private const byte ReusedFlag = 1;
        private const byte RepairsFlag = 2;
        public readonly long ConnectionTime;
        public readonly byte ConnectionNumber;
        public readonly int PeerId;
        public readonly bool PeerNetworkChanged;
        public readonly bool Repairs;

        private NetConnectAcceptPacket(long connectionTime, byte connectionNumber, int peerId, bool peerNetworkChanged, bool repairs)
        {
            ConnectionTime = connectionTime;
            ConnectionNumber = connectionNumber;
            PeerId = peerId;
            PeerNetworkChanged = peerNetworkChanged;
            Repairs = repairs;
        }

        public static NetConnectAcceptPacket FromData(NetPacket packet)
        {
            if (packet.Size != Size)
                return null;

            long connectionId = BitConverter.ToInt64(packet.RawData, 1);

            //check connect num
            byte connectionNumber = packet.RawData[9];
            if (connectionNumber >= NetConstants.MaxConnectionNumber)
                return null;

            //check reused and repairs flags
            byte flags = packet.RawData[10];
            if (flags > (ReusedFlag | RepairsFlag))
                return null;

            //get remote peer id
            int peerId = BitConverter.ToInt32(packet.RawData, 11);
            if (peerId < 0)
                return null;

            return new NetConnectAcceptPacket(connectionId, connectionNumber, peerId,
                (flags & ReusedFlag) != 0, (flags & RepairsFlag) != 0);
        }

        public static NetPacket Make(long connectTime, byte connectNum, int localPeerId, bool repairs)
        {
            var packet = new NetPacket(PacketProperty.ConnectAccept, 0);
            FastBitConverter.GetBytes(packet.RawData, 1, connectTime);
            packet.RawData[9] = connectNum;
            packet.RawData[10] = repairs ? RepairsFlag : (byte)0;
            FastBitConverter.GetBytes(packet.RawData, 11, localPeerId);
            return packet;
        }

        public static NetPacket MakeNetworkChanged(LiteNetPeer peer)
        {
            var packet = new NetPacket(PacketProperty.PeerNotFound, Size-1);
            FastBitConverter.GetBytes(packet.RawData, 1, peer.ConnectTime);
            packet.RawData[9] = peer.ConnectionNum;
            packet.RawData[10] = ReusedFlag;
            FastBitConverter.GetBytes(packet.RawData, 11, peer.RemoteId);
            return packet;
        }
    }
}
