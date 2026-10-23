using System;

namespace LiteNetLib
{
    public partial class LiteNetManager
    {
        private NetPacket _poolHead;
        private int _poolCount;
        private readonly object _poolLock = new object();
        private readonly NetPacket[] _largePoolHeads = new NetPacket[6];
        private int _largePoolCount;
        private int _largePoolBytes;
        private const int LargePoolByteLimit = 256 * 1024;

        /// <summary>
        /// Maximum packet pool size (increase if you have tons of packets sending)
        /// </summary>
        public int PacketPoolSize = 1000;

        public int PoolCount => _poolCount + _largePoolCount;

        private static int LargePacketBucket(int size, out int capacity)
        {
            capacity = 2048;
            for (int bucket = 0; bucket < 6; bucket++, capacity *= 2)
                if (size <= capacity)
                    return bucket;
            return -1;
        }
        
        private NetPacket PoolGetWithData(PacketProperty property, byte[] data, int start, int length)
        {
            int headerSize = NetPacket.GetHeaderSize(property);
            NetPacket packet = PoolGetPacket(length + headerSize);
            packet.Property = property;
            Buffer.BlockCopy(data, start, packet.RawData, headerSize, length);
            return packet;
        }

        //Get packet with size
        private NetPacket PoolGetWithProperty(PacketProperty property, int size)
        {
            NetPacket packet = PoolGetPacket(size + NetPacket.GetHeaderSize(property));
            packet.Property = property;
            return packet;
        }

        private NetPacket PoolGetWithProperty(PacketProperty property)
        {
            NetPacket packet = PoolGetPacket(NetPacket.GetHeaderSize(property));
            packet.Property = property;
            return packet;
        }

        internal NetPacket PoolGetPacket(int size)
        {
            if (size > NetConstants.MaxPacketSize)
            {
                int bucket = LargePacketBucket(size, out int capacity);
                if (bucket < 0 || PacketPoolSize <= 0)
                    return new NetPacket(size);
                lock (_poolLock)
                {
                    var large = _largePoolHeads[bucket];
                    if (large != null)
                    {
                        _largePoolHeads[bucket] = large.Next;
                        large.Next = null;
                        _largePoolCount--;
                        _largePoolBytes -= large.RawData.Length;
                        large.Size = size;
                        return large;
                    }
                }
                return new NetPacket(capacity) { Size = size };
            }

            NetPacket packet;
            lock (_poolLock)
            {
                packet = _poolHead;
                if (packet == null)
                    return new NetPacket(size);
                
                _poolHead = _poolHead.Next;
                _poolCount--;
            }
            
            packet.Size = size;
            if (packet.RawData.Length < size)
                packet.RawData = new byte[size];
            return packet;
        }

        internal void PoolRecycle(NetPacket packet)
        {
            if (packet.RawData.Length > NetConstants.MaxPacketSize)
            {
                int bucket = LargePacketBucket(packet.RawData.Length, out int capacity);
                // Keep only size-class buffers. Arbitrary and very large messages stay unpooled.
                if (bucket < 0 || capacity != packet.RawData.Length)
                    return;
                lock (_poolLock)
                {
                    if (_poolCount + _largePoolCount >= PacketPoolSize ||
                        _largePoolBytes + capacity > LargePoolByteLimit)
                        return;
                    packet.RawData[0] = 0;
                    packet.UserData = null;
                    packet.Next = _largePoolHeads[bucket];
                    _largePoolHeads[bucket] = packet;
                    _largePoolCount++;
                    _largePoolBytes += capacity;
                }
                return;
            }
            
            //Clean fragmented flag
            packet.RawData[0] = 0;
            lock (_poolLock)
            {
                if (_poolCount + _largePoolCount >= PacketPoolSize)
                    return;
                packet.Next = _poolHead;
                _poolHead = packet;
                _poolCount++;
            }
        }
    }
}
