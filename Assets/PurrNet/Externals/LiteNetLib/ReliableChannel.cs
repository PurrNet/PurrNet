using System;
using System.Collections.Generic;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    internal sealed class MergedPacketUserData
    {
        public readonly object[] Items;

        public MergedPacketUserData(object[] items)
        {
            Items = items;
        }
    }

    internal sealed class ReliableChannel : BaseChannel
    {
        [ThreadStatic]
        private static List<object> _mergedPacketUserDataList;
        private const int MergeHeaderSize = 2;
        private const int MergeSizeThreshold = 20;
        private NetPacket _mergeTail;

        private struct PendingPacket
        {
            private NetPacket _packet;
            private long _timeStamp;
            private bool _isSent;
            private bool _lost;

            public override string ToString() => _packet == null ? "Empty" : _packet.Sequence.ToString();

            public void Init(NetPacket packet)
            {
                _packet = packet;
                _isSent = false;
                _lost = false;
            }

            //Returns true if there is a pending packet inside
            public bool TrySend(long currentTime, LiteNetPeer peer, ReliableChannel channel)
            {
                if (_packet == null)
                    return false;

                if (_isSent && !_lost) //check send time
                {
                    double resendDelay = peer.ResendDelay * TimeSpan.TicksPerMillisecond;
                    double packetHoldTime = currentTime - _timeStamp;
                    if (packetHoldTime < resendDelay)
                        return true;
                    NetDebug.Write($"[RC]Resend: {packetHoldTime} > {resendDelay}");
                }
                bool resend = _isSent;
                if (resend)
                    channel.RecordResend(_packet.Size);
                else
                    channel.RecordOriginal(_packet.Size);
                _timeStamp = currentTime;
                _isSent = true;
                _lost = false;
                // Sequences use 15 bits; the spare top bit tells the receiver this is not the original.
                bool marked = resend && peer.UsesRepairs;
                if (marked)
                    _packet.Sequence |= ResendFlag;
                peer.SendUserData(_packet);
                if (marked)
                    _packet.Sequence &= SequenceMask;
                return true;
            }

            public bool IsEmpty => _packet == null;
            public bool IsSent => _isSent;
            public bool IsInFlight => _packet != null && _isSent && !_lost;
            public long TimeStamp => _timeStamp;
            public NetPacket Packet => _packet;

            // An ack proved a later transmission arrived without this one: resend on the next pass.
            public void MarkLost() => _lost = true;

            public bool Clear(LiteNetPeer peer)
            {
                if (_packet != null)
                {
                    peer.RecycleAndDeliver(_packet);
                    _packet = null;
                    return true;
                }
                return false;
            }
        }

        // The top bit of a data packet's sequence marks a resend, so the receiver can count lost originals.
        private const ushort ResendFlag = 0x8000;
        private const ushort SequenceMask = 0x7FFF;

        private readonly NetPacket _outgoingAcks;            //for send acks
        private readonly PendingPacket[] _pendingPackets;    //for unacked packets and duplicates
        private readonly NetPacket[] _receivedPackets;       //for order
        private readonly bool[] _earlyReceived;              //for unordered

        private int _localSeqence;
        private int _remoteSequence;
        private int _localWindowStart;
        private int _remoteWindowStart;

        private bool _mustSendAcks;

        private readonly DeliveryMethod _deliveryMethod;
        private readonly bool _ordered;
        private readonly int _windowSize;
        private const int BitsInByte = 8;
        private readonly byte _id;

        public ReliableChannel(LiteNetPeer peer, bool ordered, byte id) : base(peer)
        {
            _id = id;
            _windowSize = NetConstants.DefaultWindowSize;
            _ordered = ordered;
            _pendingPackets = new PendingPacket[_windowSize];
            for (int i = 0; i < _pendingPackets.Length; i++)
                _pendingPackets[i] = new PendingPacket();

            if (_ordered)
            {
                _deliveryMethod = DeliveryMethod.ReliableOrdered;
                _receivedPackets = new NetPacket[_windowSize];
            }
            else
            {
                _deliveryMethod = DeliveryMethod.ReliableUnordered;
                _earlyReceived = new bool[_windowSize];
            }

            _localWindowStart = 0;
            _localSeqence = 0;
            _remoteSequence = 0;
            _remoteWindowStart = 0;
            _outgoingAcks = new NetPacket(PacketProperty.Ack, (_windowSize - 1) / BitsInByte + 2) {ChannelId = id};
            // The byte after the ack bitmap counts, modulo 256, the originals this side had to recover.
            _lossReportIndex = _outgoingAcks.Size - 1;
        }

        public override void AddToQueue(NetPacket packet)
        {
            lock (OutgoingQueue)
            {
                _mergeTail = null;
                OutgoingQueue.Enqueue(packet);
            }
            AddToPeerChannelSendQueue();
        }

        internal void AddToQueue(ReadOnlySpan<byte> data, int mtu)
        {
            lock (OutgoingQueue)
            {
                if (!TryAppendToTail(data, mtu))
                {
                    var packet = Peer.NetManager.PoolGetPacket(mtu);
                    packet.Property = PacketProperty.Channeled;
                    packet.Size = NetConstants.ChanneledHeaderSize + data.Length;
                    packet.UserData = null;
                    data.CopyTo(new Span<byte>(packet.RawData, NetConstants.ChanneledHeaderSize, data.Length));
                    OutgoingQueue.Enqueue(packet);
                    _mergeTail = packet;
                }
            }
            AddToPeerChannelSendQueue();
        }

        private bool TryAppendToTail(ReadOnlySpan<byte> data, int mtu)
        {
            var packet = _mergeTail;
            if (packet == null || OutgoingQueue.Count == 0 || data.Overlaps(new ReadOnlySpan<byte>(packet.RawData)))
                return false;

            int headerSize = NetConstants.ChanneledHeaderSize;
            bool merged = packet.Property == PacketProperty.ReliableMerged;
            int firstSize = packet.Size - headerSize;
            int size = packet.Size + data.Length + MergeHeaderSize + (merged ? 0 : MergeHeaderSize);
            if (size + MergeSizeThreshold > mtu || size > packet.RawData.Length)
                return false;

            if (!merged)
            {
                Buffer.BlockCopy(packet.RawData, headerSize, packet.RawData, headerSize + MergeHeaderSize, firstSize);
                FastBitConverter.GetBytes(packet.RawData, headerSize, (ushort)firstSize);
                packet.Size += MergeHeaderSize;
                packet.Property = PacketProperty.ReliableMerged;
            }

            FastBitConverter.GetBytes(packet.RawData, packet.Size, (ushort)data.Length);
            data.CopyTo(new Span<byte>(packet.RawData, packet.Size + MergeHeaderSize, data.Length));
            packet.Size = size;
            return true;
        }

        private NetPacket DequeueOutgoingPacket()
        {
            var packet = OutgoingQueue.Dequeue();
            if (ReferenceEquals(packet, _mergeTail))
                _mergeTail = null;
            return packet;
        }

        private NetPacket TakeMergedPacket(NetPacket packet)
        {
            int headerSize = NetConstants.ChanneledHeaderSize;
            if (packet.Size + MergeSizeThreshold <= Peer.Mtu &&
                headerSize + MergeHeaderSize + BitConverter.ToUInt16(packet.RawData, headerSize) < packet.Size)
                return DequeueOutgoingPacket();

            int position = headerSize;
            int count = 0;
            int maxSize = Peer.Mtu - MergeSizeThreshold;
            while (position + MergeHeaderSize <= packet.Size)
            {
                int length = BitConverter.ToUInt16(packet.RawData, position);
                int next = position + MergeHeaderSize + length;
                if (count > 0 && next > maxSize)
                    break;
                position = next;
                ++count;
            }

            if (position == packet.Size)
            {
                DequeueOutgoingPacket();
                if (count == 1)
                {
                    int length = packet.Size - headerSize - MergeHeaderSize;
                    Buffer.BlockCopy(packet.RawData, headerSize + MergeHeaderSize, packet.RawData, headerSize, length);
                    packet.Size = headerSize + length;
                    packet.Property = PacketProperty.Channeled;
                }
                return packet;
            }

            int payloadSize = position - headerSize;
            var result = Peer.NetManager.PoolGetPacket(position - (count == 1 ? MergeHeaderSize : 0));
            result.Property = count == 1 ? PacketProperty.Channeled : PacketProperty.ReliableMerged;
            result.UserData = null;
            int offset = count == 1 ? MergeHeaderSize : 0;
            Buffer.BlockCopy(packet.RawData, headerSize + offset, result.RawData, headerSize, payloadSize - offset);
            int remaining = packet.Size - position;
            Buffer.BlockCopy(packet.RawData, position, packet.RawData, headerSize, remaining);
            packet.Size = headerSize + remaining;
            return result;
        }

        private NetPacket GetNextOutgoingPacket()
        {
            var packet = OutgoingQueue.Peek();
            if (packet.Property == PacketProperty.ReliableMerged)
                return TakeMergedPacket(packet);

            packet = DequeueOutgoingPacket();
            if (OutgoingQueue.Count == 0 || packet.IsFragmented)
                return packet;

            int maxPayloadSize = Peer.Mtu - NetConstants.ChanneledHeaderSize;

            // The caller holds the queue lock. Only allocate a merge packet if
            // the first two messages can actually be sent together.
            var second = OutgoingQueue.Peek();
            int firstTwoSize = packet.Size + second.Size - 2 * NetConstants.ChanneledHeaderSize + 2 * MergeHeaderSize;
            if (second.IsFragmented || second.Property == PacketProperty.ReliableMerged ||
                firstTwoSize + MergeSizeThreshold > maxPayloadSize)
                return packet;

            var mergedPacket = Peer.NetManager.PoolGetPacket(Peer.Mtu);
            mergedPacket.Property = PacketProperty.ReliableMerged;
            int mergePos = 0;

            var userDataList = _mergedPacketUserDataList;
            if (userDataList == null)
            {
                userDataList = new List<object>();
                _mergedPacketUserDataList = userDataList;
            }
            else
            {
                userDataList.Clear();
            }

            while (true)
            {
                int payloadSize = packet.Size - NetConstants.ChanneledHeaderSize;
                FastBitConverter.GetBytes(mergedPacket.RawData, NetConstants.ChanneledHeaderSize + mergePos, (ushort)payloadSize);
                Buffer.BlockCopy(packet.RawData, NetConstants.ChanneledHeaderSize, mergedPacket.RawData, NetConstants.ChanneledHeaderSize + mergePos + MergeHeaderSize, payloadSize);
                mergePos += payloadSize + MergeHeaderSize;

                if (packet.UserData != null)
                {
                    userDataList.Add(packet.UserData);
                    packet.UserData = null;
                }

                Peer.NetManager.PoolRecycle(packet);
                if (OutgoingQueue.Count == 0)
                    break;

                packet = OutgoingQueue.Peek();
                int newSize = mergePos + MergeHeaderSize + packet.Size - NetConstants.ChanneledHeaderSize;
                if (packet.IsFragmented || packet.Property == PacketProperty.ReliableMerged ||
                    newSize + MergeSizeThreshold > maxPayloadSize)
                    break;

                DequeueOutgoingPacket();
            }

            mergedPacket.Size = NetConstants.ChanneledHeaderSize + mergePos;
            if (userDataList.Count > 0)
                mergedPacket.UserData = new MergedPacketUserData(userDataList.ToArray());

            return mergedPacket;
        }

        private void ProcessIncomingPacket(NetPacket packet)
        {
            if (packet.Property == PacketProperty.ReliableMerged)
            {
                //ProcessMerged
                int pos = NetConstants.ChanneledHeaderSize;
                while (pos + MergeHeaderSize <= packet.Size)
                {
                    ushort size = BitConverter.ToUInt16(packet.RawData, pos);
                    pos += MergeHeaderSize;
                    if (size == 0 || pos + size > packet.Size)
                    {
                        NetDebug.Write("[RR]Merged packet corrupted");
                        break;
                    }

                    NetPacket mergedPacket = Peer.NetManager.PoolGetPacket(NetConstants.ChanneledHeaderSize + size);
                    mergedPacket.Property = PacketProperty.Channeled;
                    mergedPacket.ChannelId = packet.ChannelId;
                    Buffer.BlockCopy(packet.RawData, pos, mergedPacket.RawData, NetConstants.ChanneledHeaderSize, size);
                    pos += size;

                    Peer.AddReliablePacket(_deliveryMethod, mergedPacket);
                }
                Peer.NetManager.PoolRecycle(packet);
            }
            else
            {
                Peer.AddReliablePacket(_deliveryMethod, packet);
            }
        }

        //ProcessAck in packet
        private void ProcessAck(NetPacket packet)
        {
            if (packet.Size != _outgoingAcks.Size)
            {
                NetDebug.Write("[PA]Invalid acks packet size");
                return;
            }

            ushort ackWindowStart = packet.Sequence;
            int windowRel = NetUtils.RelativeSequenceNumber(_localWindowStart, ackWindowStart);
            if (ackWindowStart >= NetConstants.MaxSequence || windowRel < 0)
            {
                NetDebug.Write("[PA]Bad window start");
                return;
            }

            //check relevance
            if (windowRel >= _windowSize)
            {
                NetDebug.Write("[PA]Old acks");
                return;
            }

            byte[] acksData = packet.RawData;
            bool anyLost = false;
            int newlyAcked = 0;
            lock (_pendingPackets)
            {
                // Newest to oldest: a packet still unacked while this ack delivers a later-sequence packet
                // sent after it was lost; waiting for the resend timer would stall every later packet.
                long laterAckedSend = long.MinValue;
                int laterAckedSeq = -1;
                for (int offset = NetUtils.RelativeSequenceNumber(_localSeqence, _localWindowStart) - 1; offset >= 0; offset--)
                {
                    int pendingSeq = (_localWindowStart + offset) % NetConstants.MaxSequence;
                    int idx = pendingSeq % _windowSize;
                    if (NetUtils.RelativeSequenceNumber(pendingSeq, ackWindowStart) >= _windowSize ||
                        !_pendingPackets[idx].IsInFlight)
                        continue;
                    long sentAt = _pendingPackets[idx].TimeStamp;
                    if ((acksData[NetConstants.ChanneledHeaderSize + idx / BitsInByte] & (1 << (idx % BitsInByte))) != 0)
                    {
                        if (laterAckedSeq < 0)
                            laterAckedSeq = pendingSeq;
                        laterAckedSend = Math.Max(laterAckedSend, sentAt);
                    }
                    else if (laterAckedSeq >= 0 && IsProvenLost(pendingSeq, sentAt, laterAckedSeq, laterAckedSend))
                    {
                        _pendingPackets[idx].MarkLost();
                        anyLost = true;
                        if (Peer.NetManager.EnableStatistics)
                        {
                            Peer.Statistics.IncrementPacketLoss();
                            Peer.NetManager.Statistics.IncrementPacketLoss();
                        }
                    }
                }

                for (int pendingSeq = _localWindowStart;
                    pendingSeq != _localSeqence;
                    pendingSeq = (pendingSeq + 1) % NetConstants.MaxSequence)
                {
                    int rel = NetUtils.RelativeSequenceNumber(pendingSeq, ackWindowStart);
                    if (rel >= _windowSize)
                    {
                        //NetDebug.Write($"[PA]REL: {rel}");
                        break;
                    }

                    int pendingIdx = pendingSeq % _windowSize;
                    int currentByte = NetConstants.ChanneledHeaderSize + pendingIdx / BitsInByte;
                    int currentBit = pendingIdx % BitsInByte;
                    if ((acksData[currentByte] & (1 << currentBit)) == 0)
                    {
                        //Skip false ack
                        //NetDebug.Write($"[PA]False ack: {pendingSeq}");
                        continue;
                    }

                    if (pendingSeq == _localWindowStart)
                    {
                        //Move window
                        _localWindowStart = (_localWindowStart + 1) % NetConstants.MaxSequence;
                    }

                    //clear packet
                    if (_pendingPackets[pendingIdx].Clear(Peer))
                    {
                        newlyAcked++;
                        NetDebug.Write($"[PA]Removing reliableInOrder ack: {pendingSeq} - true");
                    }
                }

                SampleLoss(newlyAcked, ReadLossReport(acksData));
            }

            if (anyLost)
                AddToPeerChannelSendQueue();
        }

        // Loss of original transmissions as the receiver counts it. A healthy path, where every original
        // arrives, reads zero and sends nothing extra.
        private const double FastLossGain = 1.0 / 16;
        private const double SlowLossGain = 1.0 / 512;
        private double _fastLoss;
        private double _slowLoss;
        private int _lossSamples;
        private byte _lastLossReport;
        private readonly int _lossReportIndex;

        // The fast average only has to show that loss started; the slow one measures how much.
        internal double lossEstimate => Math.Max(_slowLoss, _fastLoss / 2);

        // The running count only moves forward; a smaller one came from an older, reordered ack.
        private int ReadLossReport(byte[] acksData)
        {
            byte report = acksData[_lossReportIndex];
            int lost = (byte)(report - _lastLossReport);
            if (lost >= 128)
                return 0;
            _lastLossReport = report;
            return lost;
        }

        private void SampleLoss(int delivered, int lost)
        {
            int samples = Math.Max(delivered, lost);
            if (samples == 0)
                return;
            double fraction = (double)lost / samples;
            _fastLoss += (1 - Math.Pow(1 - FastLossGain, samples)) * (fraction - _fastLoss);
            // A plain mean until the slow window fills, so the first losses count at full weight.
            _lossSamples = Math.Min(_lossSamples + samples, 1 << 20);
            double slowWeight = Math.Max(1 - Math.Pow(1 - SlowLossGain, samples), (double)samples / _lossSamples);
            _slowLoss += slowWeight * (fraction - _slowLoss);
        }

        // Tolerate a little reordering: a delivered packet must have been sent a fraction of an RTT
        // later, or come several sequence numbers later for packets sent in one burst (fragments).
        private const int ReorderPacketThreshold = 3;

        private bool IsProvenLost(int seq, long sentAt, int highestLaterAckedSeq, long latestLaterAckedSend)
        {
            // Deliveries sent before this packet's latest transmission say nothing about it.
            if (latestLaterAckedSend < sentAt)
                return false;
            if (NetUtils.RelativeSequenceNumber(highestLaterAckedSeq, seq) >= ReorderPacketThreshold)
                return true;
            long reorderWindow = Math.Max(1, Peer.RoundTripTime / 8) * TimeSpan.TicksPerMillisecond;
            return latestLaterAckedSend - sentAt >= reorderWindow;
        }

        public override bool SendNextPackets()
        {
            if (_mustSendAcks)
            {
                _mustSendAcks = false;
                NetDebug.Write("[RR]SendAcks");
                lock(_outgoingAcks)
                    Peer.SendUserData(_outgoingAcks);
            }

            long currentTime = DateTime.UtcNow.Ticks;
            bool hasPendingPackets = false;

            lock (_pendingPackets)
            {
                //get packets from queue
                lock (OutgoingQueue)
                {
                    while (OutgoingQueue.Count > 0)
                    {
                        int relate = NetUtils.RelativeSequenceNumber(_localSeqence, _localWindowStart);
                        if (relate >= _windowSize)
                            break;

                        var netPacket = GetNextOutgoingPacket();
                        netPacket.Sequence = (ushort) _localSeqence;
                        netPacket.ChannelId = _id;
                        _pendingPackets[_localSeqence % _windowSize].Init(netPacket);
                        _localSeqence = (_localSeqence + 1) % NetConstants.MaxSequence;
                    }
                }

                if (Peer.UsesRepairs)
                    SendRepairs(currentTime);

                //send
                Array.Clear(_originalsBySize, 0, _originalsBySize.Length);
                for (int pendingSeq = _localWindowStart; pendingSeq != _localSeqence; pendingSeq = (pendingSeq + 1) % NetConstants.MaxSequence)
                {
                    // Please note: TrySend is invoked on a mutable struct, it's important to not extract it into a variable here
                    if (_pendingPackets[pendingSeq % _windowSize].TrySend(currentTime, Peer, this))
                        hasPendingPackets = true;
                }
                AddRepairCredit(currentTime);
            }

            return hasPendingPackets || _mustSendAcks || OutgoingQueue.Count > 0;
        }

        //Process incoming packet
        public override bool ProcessPacket(NetPacket packet)
        {
            if (packet.Property == PacketProperty.Ack)
            {
                ProcessAck(packet);
                return false;
            }
            if (packet.Property == PacketProperty.Repair)
            {
                ProcessRepair(packet);
                RecoverSolvedPackets();
                return false;
            }
            bool keep = ProcessDataPacket(packet);
            RecoverSolvedPackets();
            return keep;
        }

        private bool ProcessDataPacket(NetPacket packet)
        {
            bool resent = (packet.Sequence & ResendFlag) != 0;
            int seq = packet.Sequence & SequenceMask;
            packet.Sequence = (ushort)seq;

            int relate = NetUtils.RelativeSequenceNumber(seq, _remoteWindowStart);
            int relateSeq = NetUtils.RelativeSequenceNumber(seq, _remoteSequence);

            if (relateSeq > _windowSize)
            {
                NetDebug.Write("[RR]Bad sequence");
                return false;
            }

            //Drop bad packets
            if (relate < 0)
            {
                //Too old packet doesn't ack
                NetDebug.Write("[RR]ReliableInOrder too old");
                return false;
            }
            if (relate >= _windowSize * 2)
            {
                //Some very new packet
                NetDebug.Write("[RR]ReliableInOrder too new");
                return false;
            }

            //If very new - move window
            int ackIdx;
            int ackByte;
            int ackBit;
            lock (_outgoingAcks)
            {
                if (relate >= _windowSize)
                {
                    //New window position
                    int newWindowStart = (_remoteWindowStart + relate - _windowSize + 1) % NetConstants.MaxSequence;
                    _outgoingAcks.Sequence = (ushort) newWindowStart;

                    //Clean old data
                    while (_remoteWindowStart != newWindowStart)
                    {
                        ackIdx = _remoteWindowStart % _windowSize;
                        ackByte = NetConstants.ChanneledHeaderSize + ackIdx / BitsInByte;
                        ackBit = ackIdx % BitsInByte;
                        _outgoingAcks.RawData[ackByte] &= (byte) ~(1 << ackBit);
                        _remoteWindowStart = (_remoteWindowStart + 1) % NetConstants.MaxSequence;
                    }
                }

                //Final stage - process valid packet
                //trigger acks send
                _mustSendAcks = true;

                ackIdx = seq % _windowSize;
                ackByte = NetConstants.ChanneledHeaderSize + ackIdx / BitsInByte;
                ackBit = ackIdx % BitsInByte;
                if ((_outgoingAcks.RawData[ackByte] & (1 << ackBit)) != 0)
                {
                    NetDebug.Write("[RR]ReliableInOrder duplicate");
                    //because _mustSendAcks == true
                    AddToPeerChannelSendQueue();
                    return false;
                }

                //save ack
                _outgoingAcks.RawData[ackByte] |= (byte) (1 << ackBit);
                if (resent)
                    _outgoingAcks.RawData[_lossReportIndex]++;
            }

            if (Peer.UsesRepairs)
                CacheReceived(packet, seq);

            AddToPeerChannelSendQueue();

            //detailed check
            if (seq == _remoteSequence)
            {
                NetDebug.Write("[RR]ReliableInOrder packet succes");
                ProcessIncomingPacket(packet);
                _remoteSequence = (_remoteSequence + 1) % NetConstants.MaxSequence;

                if (_ordered)
                {
                    NetPacket p;
                    while ((p = _receivedPackets[_remoteSequence % _windowSize]) != null)
                    {
                        //process holden packet
                        _receivedPackets[_remoteSequence % _windowSize] = null;
                        ProcessIncomingPacket(p);
                        _remoteSequence = (_remoteSequence + 1) % NetConstants.MaxSequence;
                    }
                }
                else
                {
                    while (_earlyReceived[_remoteSequence % _windowSize])
                    {
                        //process early packet
                        _earlyReceived[_remoteSequence % _windowSize] = false;
                        _remoteSequence = (_remoteSequence + 1) % NetConstants.MaxSequence;
                    }
                }
                return true;
            }

            //holden packet
            if (_ordered)
            {
                _receivedPackets[ackIdx] = packet;
            }
            else
            {
                _earlyReceived[ackIdx] = true;
                ProcessIncomingPacket(packet);
            }
            return true;
        }

        // ---- Repairs ----
        // Instead of copying packets, the sender adds repairs: random GF(256) combinations of the packets
        // still in flight. A receiver missing k of them rebuilds them from any k repairs, so one repair
        // replaces whichever packet went missing, and at high send rates the rate can be a fraction of
        // a repair per packet.
        // After the channeled header, whose sequence is the oldest one covered: a 64-bit coverage mask,
        // the coefficient seed, the symbol length and the symbol. Each covered packet enters the symbol as
        // its size and its bytes, with the connection number and the resend flag cleared.
        private const int RepairHeaderSize = NetConstants.ChanneledHeaderSize + 8 + 2 + 2;
        private const int RepairEntryHeaderSize = 2;
        internal const int RepairOverhead = RepairHeaderSize + RepairEntryHeaderSize;
        private const int MaxRepairCoverage = 32;
        private const int MaxRepairRows = 32;
        // A repair never covers a packet sent this recently, so it cannot share that packet's datagram
        // when the peer flushes twice in one frame.
        private const long RepairSpacingTicks = 5 * TimeSpan.TicksPerMillisecond;
        // Caps one pass so credit built up during an outage leaves over several passes, not in one burst.
        private const int MaxRepairsPerPass = 4;

        // Repair rate: a loss burst longer than the repairs cover must be rarer than the stall target,
        // and the repairs for a covered burst must arrive within the recovery time of good datagrams.
        private const double RepairMinLoss = 0.005;
        private const double RepairStallsPerSecond = 0.003;
        private const double RepairRecoveryMs = 33;
        private const double MaxRepairsPerPacket = 4;

        // Repairs are as large as the largest packet they cover, so unusually large packets (over twice
        // the typical size) get their own stream: ordinary updates never pay for them, and a rare large
        // packet is protected at the rate its own interval calls for.
        private const int RepairSizeClasses = 2;
        private double _typicalLogSize = -1;

        private int RepairSizeClass(int size) =>
            _typicalLogSize < 0 || size <= Math.Max(256, 2 * Math.Exp(_typicalLogSize)) ? 0 : 1;

        // A resend proves the path is losing packets, even before the estimate says so: protect it with
        // at least one repair on the next pass, so losing it again does not cost another round trip.
        private void RecordResend(int size)
        {
            ref var stream = ref _repairStreams[RepairSizeClass(size)];
            stream.Credit = Math.Min(MaxRepairCoverage / 2, Math.Max(stream.Credit, 0) + 1);
        }

        // A geometric mean barely moves for a rare outlier but follows a lasting change in size.
        private void RecordOriginal(int size)
        {
            _originalsBySize[RepairSizeClass(size)]++;
            double logSize = Math.Log(Math.Max(1, size));
            _typicalLogSize = _typicalLogSize < 0 ? logSize : _typicalLogSize + (logSize - _typicalLogSize) / 32;
        }

        private struct RepairStream
        {
            public double IntervalMs;
            public long LastOriginalTime;
            public double Credit;
        }

        private readonly RepairStream[] _repairStreams = CreateRepairStreams();
        private readonly int[] _originalsBySize = new int[RepairSizeClasses];
        private long _lastOriginalTime;
        private double _passIntervalMs = 33;
        private ushort _repairSeed;
        private int _repairsThisPass;
        private NetPacket _repairPacket;
        private byte[] _repairScratch;
        private readonly int[] _repairCandidates = new int[NetConstants.DefaultWindowSize];
        private readonly int[] _repairCoverage = new int[MaxRepairCoverage];

        private static RepairStream[] CreateRepairStreams()
        {
            var streams = new RepairStream[RepairSizeClasses];
            for (int i = 0; i < streams.Length; i++)
                streams[i].IntervalMs = 33;
            return streams;
        }

        private double RepairsPerPacket(double intervalMs)
        {
            double loss = Math.Min(0.9, lossEstimate);
            if (loss < RepairMinLoss)
                return 0;
            int burst = (int)Math.Ceiling(Math.Log(RepairStallsPerSecond * intervalMs / 1000) / Math.Log(loss)) - 1;
            double spread = Math.Max(1, RepairRecoveryMs / intervalMs);
            return Math.Min(MaxRepairsPerPacket, Math.Max(1, burst) / spread);
        }

        private void AddRepairCredit(long currentTime)
        {
            bool sent = false;
            for (int sizeClass = 0; sizeClass < RepairSizeClasses; sizeClass++)
            {
                int originals = _originalsBySize[sizeClass];
                if (originals == 0)
                    continue;
                ref var stream = ref _repairStreams[sizeClass];
                if (stream.LastOriginalTime != 0)
                {
                    double interval = (double)(currentTime - stream.LastOriginalTime) / TimeSpan.TicksPerMillisecond;
                    stream.IntervalMs += (Math.Min(1000, Math.Max(1, interval)) - stream.IntervalMs) / 8;
                }
                stream.LastOriginalTime = currentTime;
                stream.Credit = Math.Min(MaxRepairCoverage / 2, stream.Credit + originals * RepairsPerPacket(stream.IntervalMs));
                sent = true;
            }
            if (!sent)
                return;
            if (_lastOriginalTime != 0)
            {
                double interval = (double)(currentTime - _lastOriginalTime) / TimeSpan.TicksPerMillisecond;
                _passIntervalMs += (Math.Min(1000, Math.Max(1, interval)) - _passIntervalMs) / 8;
            }
            _lastOriginalTime = currentTime;
        }

        private void SendRepairs(long currentTime)
        {
            _repairsThisPass = 0;
            for (int sizeClass = 0; sizeClass < RepairSizeClasses; sizeClass++)
            {
                ref var stream = ref _repairStreams[sizeClass];
                // A class that sends less often than the channel spreads its repairs over the following
                // passes, one each, so a single lost datagram cannot take all of them.
                int budget = stream.IntervalMs > 1.5 * _passIntervalMs ? 1 : MaxRepairsPerPass;
                while (stream.Credit >= 1 && budget-- > 0)
                {
                    stream.Credit -= 1;
                    if (!SendRepair(sizeClass, currentTime))
                    {
                        stream.Credit = 0;
                        break;
                    }
                }
            }
        }

        // Covers the packets of one size class sent within the last round trip, most recently sent
        // first, so a resend is protected like an original even though its sequence is old.
        private bool SendRepair(int sizeClass, long currentTime)
        {
            int maxPacketSize = Peer.Mtu - RepairOverhead;
            long horizon = (Math.Max(Peer.RoundTripTime, 100) + 50) * TimeSpan.TicksPerMillisecond;
            int candidates = 0;
            for (int seq = _localWindowStart; seq != _localSeqence; seq = (seq + 1) % NetConstants.MaxSequence)
            {
                int idx = seq % _windowSize;
                var packet = _pendingPackets[idx].Packet;
                if (packet == null || !_pendingPackets[idx].IsSent || packet.Size > maxPacketSize ||
                    RepairSizeClass(packet.Size) != sizeClass || currentTime - _pendingPackets[idx].TimeStamp > horizon ||
                    currentTime - _pendingPackets[idx].TimeStamp < RepairSpacingTicks)
                    continue;
                _repairCandidates[candidates++] = seq;
            }
            if (candidates == 0)
                return false;

            // Keep the most recently sent candidates.
            int count = Math.Min(candidates, MaxRepairCoverage);
            for (int i = 0; i < count; i++)
            {
                int best = i;
                for (int j = i + 1; j < candidates; j++)
                {
                    if (_pendingPackets[_repairCandidates[j] % _windowSize].TimeStamp >
                        _pendingPackets[_repairCandidates[best] % _windowSize].TimeStamp)
                        best = j;
                }
                (_repairCandidates[i], _repairCandidates[best]) = (_repairCandidates[best], _repairCandidates[i]);
                _repairCoverage[i] = _repairCandidates[i];
            }

            int baseSeq = _repairCoverage[0];
            int length = 0;
            for (int i = 0; i < count; i++)
            {
                int seq = _repairCoverage[i];
                if (NetUtils.RelativeSequenceNumber(seq, baseSeq) < 0)
                    baseSeq = seq;
                length = Math.Max(length, RepairEntryHeaderSize + _pendingPackets[seq % _windowSize].Packet.Size);
            }
            ulong mask = 0;
            for (int i = 0; i < count; i++)
                mask |= 1UL << NetUtils.RelativeSequenceNumber(_repairCoverage[i], baseSeq);

            int size = RepairHeaderSize + length;
            if (_repairPacket == null || _repairPacket.RawData.Length < size)
                _repairPacket = new NetPacket(PacketProperty.Repair, Math.Max(size, Peer.Mtu) - NetConstants.ChanneledHeaderSize);
            if (_repairScratch == null || _repairScratch.Length < length)
                _repairScratch = new byte[Math.Max(length, Peer.Mtu)];

            var repair = _repairPacket;
            ushort seed = _repairSeed++;
            repair.Sequence = (ushort)baseSeq;
            repair.ChannelId = _id;
            FastBitConverter.GetBytes(repair.RawData, NetConstants.ChanneledHeaderSize, mask);
            FastBitConverter.GetBytes(repair.RawData, NetConstants.ChanneledHeaderSize + 8, seed);
            FastBitConverter.GetBytes(repair.RawData, NetConstants.ChanneledHeaderSize + 10, (ushort)length);
            Array.Clear(repair.RawData, RepairHeaderSize, length);
            for (int i = 0; i < count; i++)
            {
                int seq = _repairCoverage[i];
                var packet = _pendingPackets[seq % _windowSize].Packet;
                int entryLength = WriteRepairEntry(_repairScratch, packet.RawData, packet.Size, seq);
                Gf256.MulAdd(repair.RawData, RepairHeaderSize, _repairScratch, 0, entryLength, Gf256.Coefficient(seed, seq));
            }
            repair.Size = size;
            // Repairs of one pass never share a datagram: losing one must not take the others with it.
            if (_repairsThisPass++ > 0)
                Peer.FlushMerged();
            Peer.SendUserData(repair);
            if (Peer.NetManager.EnableStatistics)
            {
                Peer.Statistics.IncrementRepairsSent();
                Peer.NetManager.Statistics.IncrementRepairsSent();
            }
            return true;
        }

        private static int WriteRepairEntry(byte[] entry, byte[] raw, int size, int seq)
        {
            entry[0] = (byte)size;
            entry[1] = (byte)(size >> 8);
            Buffer.BlockCopy(raw, 0, entry, RepairEntryHeaderSize, size);
            entry[RepairEntryHeaderSize] &= 0x9F;
            entry[RepairEntryHeaderSize + 1] = (byte)seq;
            entry[RepairEntryHeaderSize + 2] = (byte)(seq >> 8);
            return RepairEntryHeaderSize + size;
        }

        // Receiver: recent packets, to cancel them out of repairs, and the repairs reduced to row echelon
        // form over the packets still missing. Kept from the first packet when repairs are on, so the first
        // repairs after a loss can already be used.
        private sealed class RepairRow
        {
            public readonly byte[] Coefficients = new byte[NetConstants.DefaultWindowSize];
            public byte[] Symbol;
            public int Length;
            public int Pivot;
        }

        private byte[][] _entries;
        private int[] _entrySeq;
        private RepairRow[] _rows;
        private int _rowCount;
        private readonly Stack<RepairRow> _freeRows = new Stack<RepairRow>();
        private bool _recovering;

        private void CacheReceived(NetPacket packet, int seq)
        {
            if (_entries == null)
            {
                _entries = new byte[_windowSize][];
                _entrySeq = new int[_windowSize];
                for (int i = 0; i < _windowSize; i++)
                    _entrySeq[i] = -1;
                _rows = new RepairRow[MaxRepairRows];
            }
            int idx = seq % _windowSize;
            int length = RepairEntryHeaderSize + packet.Size;
            var entry = _entries[idx];
            if (entry == null || entry.Length < length)
                entry = _entries[idx] = new byte[(length + 63) & ~63];
            WriteRepairEntry(entry, packet.RawData, packet.Size, seq);
            _entrySeq[idx] = seq;
            if (_rowCount > 0)
                CancelFromRows(seq, entry, length);
        }

        // Received packets are acked; anything older than the ack window arrived long ago.
        private bool Received(int seq)
        {
            int relate = NetUtils.RelativeSequenceNumber(seq, _remoteWindowStart);
            if (relate < 0)
                return true;
            if (relate >= _windowSize)
                return false;
            int idx = seq % _windowSize;
            return (_outgoingAcks.RawData[NetConstants.ChanneledHeaderSize + idx / BitsInByte] & (1 << (idx % BitsInByte))) != 0;
        }

        private void ProcessRepair(NetPacket packet)
        {
            if (packet.Size < RepairHeaderSize)
                return;
            int baseSeq = packet.Sequence & SequenceMask;
            ulong mask = BitConverter.ToUInt64(packet.RawData, NetConstants.ChanneledHeaderSize);
            ushort seed = BitConverter.ToUInt16(packet.RawData, NetConstants.ChanneledHeaderSize + 8);
            int length = BitConverter.ToUInt16(packet.RawData, NetConstants.ChanneledHeaderSize + 10);
            if (mask == 0 || length <= RepairEntryHeaderSize + NetConstants.ChanneledHeaderSize ||
                RepairHeaderSize + length > packet.Size)
                return;

            if (_entries == null)
                return;

            bool anyMissing = false;
            for (int offset = 0; offset < 64 && !anyMissing; offset++)
                anyMissing = (mask & (1UL << offset)) != 0 && !Received((baseSeq + offset) % NetConstants.MaxSequence);
            if (!anyMissing)
                return;

            var row = RentRow(length);
            Buffer.BlockCopy(packet.RawData, RepairHeaderSize, row.Symbol, 0, length);
            row.Length = length;
            for (int offset = 0; offset < 64; offset++)
            {
                if ((mask & (1UL << offset)) == 0)
                    continue;
                int seq = (baseSeq + offset) % NetConstants.MaxSequence;
                byte coefficient = Gf256.Coefficient(seed, seq);
                int idx = seq % _windowSize;
                if (!Received(seq))
                {
                    int relate = NetUtils.RelativeSequenceNumber(seq, _remoteSequence);
                    if (relate < 0 || relate >= _windowSize)
                    {
                        ReturnRow(row);
                        return;
                    }
                    row.Coefficients[idx] = coefficient;
                }
                else if (_entrySeq[idx] == seq)
                {
                    var entry = _entries[idx];
                    int entryLength = RepairEntryHeaderSize + (entry[0] | (entry[1] << 8));
                    if (entryLength > length)
                    {
                        ReturnRow(row);
                        return;
                    }
                    Gf256.MulAdd(row.Symbol, 0, entry, 0, entryLength, coefficient);
                }
                else
                {
                    // Arrived before the cache started or was overwritten: it cannot be cancelled out.
                    ReturnRow(row);
                    return;
                }
            }
            AddRow(row);
        }

        private RepairRow RentRow(int length)
        {
            var row = _freeRows.Count > 0 ? _freeRows.Pop() : new RepairRow();
            if (row.Symbol == null || row.Symbol.Length < length)
                row.Symbol = new byte[Math.Max(length, Peer.Mtu + RepairEntryHeaderSize)];
            else
                Array.Clear(row.Symbol, 0, row.Symbol.Length);
            Array.Clear(row.Coefficients, 0, row.Coefficients.Length);
            row.Length = 0;
            return row;
        }

        private void ReturnRow(RepairRow row) => _freeRows.Push(row);

        // target -= factor * source, over coefficients and symbol.
        private static void Subtract(RepairRow target, RepairRow source, byte factor)
        {
            Gf256.MulAdd(target.Coefficients, 0, source.Coefficients, 0, source.Coefficients.Length, factor);
            if (target.Symbol.Length < source.Length)
            {
                var grown = new byte[source.Symbol.Length];
                Buffer.BlockCopy(target.Symbol, 0, grown, 0, target.Length);
                target.Symbol = grown;
            }
            Gf256.MulAdd(target.Symbol, 0, source.Symbol, 0, source.Length, factor);
            target.Length = Math.Max(target.Length, source.Length);
        }

        // Pivots on the oldest missing packet the row still involves and scales that coefficient to one.
        private bool ChoosePivot(RepairRow row)
        {
            for (int offset = 0; offset < _windowSize; offset++)
            {
                int seq = (_remoteSequence + offset) % NetConstants.MaxSequence;
                byte coefficient = row.Coefficients[seq % _windowSize];
                if (coefficient == 0)
                    continue;
                byte inverse = Gf256.Inverse(coefficient);
                Gf256.Scale(row.Coefficients, 0, row.Coefficients.Length, inverse);
                Gf256.Scale(row.Symbol, 0, row.Length, inverse);
                row.Pivot = seq;
                return true;
            }
            return false;
        }

        private void AddRow(RepairRow row)
        {
            for (int r = 0; r < _rowCount; r++)
            {
                byte factor = row.Coefficients[_rows[r].Pivot % _windowSize];
                if (factor != 0)
                    Subtract(row, _rows[r], factor);
            }
            InsertReduced(row);
        }

        // Adds a row already free of every other pivot and removes its own pivot from the others.
        private void InsertReduced(RepairRow row)
        {
            if (!ChoosePivot(row))
            {
                ReturnRow(row);
                return;
            }
            if (_rowCount == MaxRepairRows)
            {
                // Full: drop whichever row pivots on the newest missing packet, the last one delivery needs.
                int newest = 0;
                for (int r = 1; r < _rowCount; r++)
                {
                    if (NetUtils.RelativeSequenceNumber(_rows[r].Pivot, _rows[newest].Pivot) > 0)
                        newest = r;
                }
                if (NetUtils.RelativeSequenceNumber(row.Pivot, _rows[newest].Pivot) > 0)
                {
                    ReturnRow(row);
                    return;
                }
                ReturnRow(RemoveRowAt(newest));
            }
            int pivot = row.Pivot % _windowSize;
            for (int r = 0; r < _rowCount; r++)
            {
                byte factor = _rows[r].Coefficients[pivot];
                if (factor != 0)
                    Subtract(_rows[r], row, factor);
            }
            _rows[_rowCount++] = row;
        }

        private RepairRow RemoveRowAt(int index)
        {
            var row = _rows[index];
            _rowCount--;
            Array.Copy(_rows, index + 1, _rows, index, _rowCount - index);
            _rows[_rowCount] = null;
            return row;
        }

        // A packet arrived: cancel it out of every row, and let the row that pivoted on it pick another.
        private void CancelFromRows(int seq, byte[] entry, int entryLength)
        {
            int idx = seq % _windowSize;
            RepairRow orphan = null;
            for (int r = _rowCount - 1; r >= 0; r--)
            {
                var row = _rows[r];
                byte coefficient = row.Coefficients[idx];
                if (coefficient == 0)
                    continue;
                if (entryLength > row.Length)
                {
                    ReturnRow(RemoveRowAt(r));
                    continue;
                }
                Gf256.MulAdd(row.Symbol, 0, entry, 0, entryLength, coefficient);
                row.Coefficients[idx] = 0;
                if (row.Pivot == seq)
                    orphan = RemoveRowAt(r);
            }
            if (orphan != null)
                InsertReduced(orphan);
        }

        private bool IsSolved(RepairRow row)
        {
            int pivot = row.Pivot % _windowSize;
            for (int i = 0; i < row.Coefficients.Length; i++)
            {
                if (i != pivot && row.Coefficients[i] != 0)
                    return false;
            }
            return true;
        }

        private void RecoverSolvedPackets()
        {
            if (_recovering || _rowCount == 0)
                return;
            _recovering = true;
            try
            {
                for (int r = 0; r < _rowCount;)
                {
                    if (!IsSolved(_rows[r]))
                    {
                        r++;
                        continue;
                    }
                    var row = RemoveRowAt(r);
                    var packet = RebuildPacket(row);
                    ReturnRow(row);
                    if (packet != null)
                    {
                        if (Peer.NetManager.EnableStatistics)
                        {
                            Peer.Statistics.IncrementPacketsRepaired();
                            Peer.NetManager.Statistics.IncrementPacketsRepaired();
                        }
                        if (!ProcessDataPacket(packet))
                            Peer.NetManager.PoolRecycle(packet);
                    }
                    // The arrival cancelled itself out of the other rows; look at all of them again.
                    r = 0;
                }
            }
            finally
            {
                _recovering = false;
            }
        }

        private NetPacket RebuildPacket(RepairRow row)
        {
            var symbol = row.Symbol;
            int size = symbol[0] | (symbol[1] << 8);
            if (size < NetConstants.ChanneledHeaderSize || RepairEntryHeaderSize + size > row.Length ||
                (symbol[RepairEntryHeaderSize + 1] | (symbol[RepairEntryHeaderSize + 2] << 8)) != row.Pivot ||
                Received(row.Pivot))
                return null;
            var packet = Peer.NetManager.PoolGetPacket(size);
            Buffer.BlockCopy(symbol, RepairEntryHeaderSize, packet.RawData, 0, size);
            var property = packet.Property;
            if ((property != PacketProperty.Channeled && property != PacketProperty.ReliableMerged) ||
                packet.ChannelId != _id || !packet.Verify())
            {
                Peer.NetManager.PoolRecycle(packet);
                return null;
            }
            // Counted like a resend: the original never arrived.
            packet.Sequence = (ushort)(row.Pivot | ResendFlag);
            return packet;
        }
    }
}
