using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;

public class RPCBatchTests
{
    sealed class CapturedBatch
    {
        public PlayerID target;
        public Channel channel;
        public Size count;
        public BitPacker data;
    }

    sealed class TestRPCBatchBackend : IRPCBatchBackend, IDisposable
    {
        readonly List<CapturedBatch> _sent = new List<CapturedBatch>();
        PlayerBroadcastDelegate<RPCBatchPacket> _callback;

        public int mtu = int.MaxValue;
        public PlayerID deliveringTarget { get; private set; }
        public Channel deliveringChannel { get; private set; }
        public IReadOnlyList<CapturedBatch> sent => _sent;

        public int GetMTU(PlayerID player, Channel channel, bool asServer) => mtu;

        public void Send(PlayerID player, RPCBatchPacket packet, Channel channel)
        {
            var copy = BitPackerPool.Get();
            copy.WriteBitDataWithoutConsumingIt(packet.data);
            _sent.Add(new CapturedBatch
            {
                target = player,
                channel = channel,
                count = packet.count,
                data = copy
            });
        }

        public void Subscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback)
        {
            if (_callback != null)
                throw new InvalidOperationException("Only one RPCBatch subscription is supported by this test backend.");
            _callback = callback;
        }

        public void Unsubscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback)
        {
            if (_callback == callback)
                _callback = null;
        }

        public int Count(PlayerID target, Channel channel)
        {
            int count = 0;
            for (int i = 0; i < _sent.Count; i++)
            {
                if (_sent[i].target == target && _sent[i].channel == channel)
                    count++;
            }
            return count;
        }

        public void DeliverAll()
        {
            if (_callback == null)
                throw new InvalidOperationException("RPCBatch is not subscribed.");

            for (int i = 0; i < _sent.Count; i++)
            {
                var captured = _sent[i];
                deliveringTarget = captured.target;
                deliveringChannel = captured.channel;

                try
                {
                    _callback(new PlayerID(999, false), new RPCBatchPacket
                    {
                        count = captured.count,
                        data = new BitData(captured.data)
                    }, true);
                }
                finally
                {
                    captured.data.Dispose();
                    captured.data = null;
                }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _sent.Count; i++)
            {
                _sent[i].data?.Dispose();
                _sent[i].data = null;
            }
        }
    }

    static readonly Channel[] AllChannels =
    {
        Channel.ReliableUnordered,
        Channel.UnreliableSequenced,
        Channel.ReliableOrdered,
        Channel.Unreliable
    };

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    static UnionRPCHeader MakeHeader(Channel channel, int sequence)
    {
        return new UnionRPCHeader(new NetworkIdentityRPCHeader
        {
            senderId = new PlayerID((ulong)(100 + (int)channel), false),
            networkId = new NetworkID((ulong)(200 + (int)channel)),
            sceneId = new SceneID(0),
            rpcId = new Size((uint)sequence),
            targetId = null
        });
    }

    static void WritePayload(BitPacker payload, int sequence, int byteCount)
    {
        payload.ResetPositionAndMode(false);
        for (int i = 0; i < byteCount; i++)
            payload.WriteBits((ulong)(byte)(sequence + i), 8);
    }

    static void RecordReceived(TestRPCBatchBackend backend,
        Dictionary<BatchKey, List<int>> received, UnionRPCHeader header, BitData content)
    {
        int sequence;
        using (content.AutoScope())
            sequence = (int)content.packer.ReadBits(8);

        Assert.That(header.rpcId.value, Is.EqualTo((uint)sequence));
        var key = new BatchKey
        {
            playerId = backend.deliveringTarget,
            channel = backend.deliveringChannel
        };

        if (!received.TryGetValue(key, out var values))
        {
            values = new List<int>();
            received.Add(key, values);
        }
        values.Add(sequence);
    }

    [Test]
    public void BatchIndexMapKeepsChannelsIndependent()
    {
        using var map = new BatchIndexMap(4);
        var player = new PlayerID(42, false);
        var channels = new[]
        {
            Channel.ReliableUnordered,
            Channel.UnreliableSequenced,
            Channel.ReliableOrdered,
            Channel.Unreliable
        };

        for (int i = 0; i < channels.Length; i++)
            map.Set(player.id.value, channels[i], 100 + i);

        for (int i = 0; i < channels.Length; i++)
        {
            Assert.That(map.TryGetValue(player.id.value, channels[i], out int value),
                Is.True);
            Assert.That(value, Is.EqualTo(100 + i));
        }

        var reliableOrdered = new BatchKey { playerId = player, channel = Channel.ReliableOrdered };
        Assert.That(map.Remove(reliableOrdered.playerId.id.value, reliableOrdered.channel), Is.True);
        Assert.That(map.TryGetValue(reliableOrdered.playerId.id.value, reliableOrdered.channel, out _), Is.False);
        Assert.That(map.TryGetValue(player.id.value, Channel.ReliableUnordered, out int reliableUnordered),
            Is.True);
        Assert.That(reliableUnordered, Is.EqualTo(100));
    }

    [Test]
    public void BatchIndexMapClearClearsEveryChannel()
    {
        using var map = new BatchIndexMap(4);
        var player = new PlayerID(7, false);
        var channels = new[]
        {
            Channel.ReliableUnordered,
            Channel.UnreliableSequenced,
            Channel.ReliableOrdered,
            Channel.Unreliable
        };

        for (int i = 0; i < channels.Length; i++)
            map.Set(player.id.value, channels[i], i);

        map.Clear();

        for (int i = 0; i < channels.Length; i++)
            Assert.That(map.TryGetValue(player.id.value, channels[i], out _), Is.False);
    }

    [Test]
    public void BatchIndexMapUsesEveryPlayerIdBit()
    {
        using var map = new BatchIndexMap(5);
        var ids = new[]
        {
            0UL,
            1UL,
            0x0000000100000001UL,
            0xFFFFFFFF00000001UL,
            ulong.MaxValue
        };

        for (int i = 0; i < ids.Length; i++)
            map.Set(ids[i], Channel.ReliableOrdered, 100 + i);

        for (int i = 0; i < ids.Length; i++)
        {
            Assert.That(map.TryGetValue(ids[i], Channel.ReliableOrdered, out int value), Is.True);
            Assert.That(value, Is.EqualTo(100 + i));
        }
    }

    [Test]
    public void QueueRejectsUndefinedChannelBeforeIndexLookup()
    {
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend, (_, _, _, _) => { });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batch.Queue(new PlayerID(1, false), default, default, (Channel)byte.MaxValue));
        Assert.That(backend.sent, Is.Empty);
    }

    [Test]
    public void StateVersionWrapDoesNotAliasDivergedRecipients()
    {
        var targets = new[]
        {
            new PlayerID(41, false),
            new PlayerID(42, false),
            new PlayerID(43, false)
        };
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        for (int i = 0; i < targets.Length; i++)
        {
            int sequence = i + 1;
            WritePayload(payload, sequence, 8);
            batch.Queue(targets[i], MakeHeader(Channel.ReliableOrdered, sequence), new BitData(payload),
                Channel.ReliableOrdered);
        }

        var versionField = typeof(RPCBatch).GetField("_nextStateVersion",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(versionField, Is.Not.Null);
        versionField.SetValue(batch, ulong.MaxValue);

        WritePayload(payload, 4, 8);
        batch.Queue(targets[1], MakeHeader(Channel.ReliableOrdered, 4), new BitData(payload),
            Channel.ReliableOrdered);

        WritePayload(payload, 5, 8);
        batch.Queue(targets, MakeHeader(Channel.ReliableOrdered, 5), new BitData(payload),
            Channel.ReliableOrdered);

        batch.Flush();
        backend.DeliverAll();

        Assert.That(received[new BatchKey
        {
            playerId = targets[0],
            channel = Channel.ReliableOrdered
        }], Is.EqualTo(new[] { 1, 5 }));
        Assert.That(received[new BatchKey
        {
            playerId = targets[1],
            channel = Channel.ReliableOrdered
        }], Is.EqualTo(new[] { 2, 4, 5 }));
        Assert.That(received[new BatchKey
        {
            playerId = targets[2],
            channel = Channel.ReliableOrdered
        }], Is.EqualTo(new[] { 3, 5 }));
    }

    [Test]
    public void QueueFanoutPreservesOrderAndChannelAcrossMtuSplits()
    {
        var targets = new[]
        {
            new PlayerID(11, false),
            new PlayerID(12, false),
            new PlayerID(13, false)
        };
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend { mtu = 64 };
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        const int sequenceCount = 6;
        for (int sequence = 0; sequence < sequenceCount; sequence++)
        {
            for (int channelIdx = 0; channelIdx < AllChannels.Length; channelIdx++)
            {
                var channel = AllChannels[channelIdx];
                WritePayload(payload, sequence, 32);
                batch.Queue(targets, MakeHeader(channel, sequence), new BitData(payload), channel);
            }
        }

        batch.Flush();

        for (int targetIdx = 0; targetIdx < targets.Length; targetIdx++)
        {
            for (int channelIdx = 0; channelIdx < AllChannels.Length; channelIdx++)
            {
                Assert.That(backend.Count(targets[targetIdx], AllChannels[channelIdx]), Is.GreaterThan(1),
                    $"Expected forced MTU splits for target {targets[targetIdx]} on {AllChannels[channelIdx]}.");
            }
        }

        backend.DeliverAll();

        for (int targetIdx = 0; targetIdx < targets.Length; targetIdx++)
        {
            for (int channelIdx = 0; channelIdx < AllChannels.Length; channelIdx++)
            {
                var key = new BatchKey
                {
                    playerId = targets[targetIdx],
                    channel = AllChannels[channelIdx]
                };
                Assert.That(received.TryGetValue(key, out var values), Is.True);
                Assert.That(values, Has.Count.EqualTo(sequenceCount));
                for (int sequence = 0; sequence < sequenceCount; sequence++)
                    Assert.That(values[sequence], Is.EqualTo(sequence));
            }
        }
    }

    [Test]
    public void FlushChannelSendsOnlyRequestedChannelAndKeepsOtherBatchesQueued()
    {
        var targets = new[]
        {
            new PlayerID(21, false),
            new PlayerID(22, false),
            new PlayerID(23, false)
        };
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        for (int sequence = 0; sequence < 3; sequence++)
        {
            WritePayload(payload, sequence, 8);
            batch.Queue(targets, MakeHeader(Channel.ReliableOrdered, sequence), new BitData(payload),
                Channel.ReliableOrdered);
            batch.Queue(targets, MakeHeader(Channel.Unreliable, sequence), new BitData(payload),
                Channel.Unreliable);
        }

        batch.FlushChannel(Channel.ReliableOrdered);
        Assert.That(backend.sent, Has.Count.EqualTo(targets.Length));
        for (int i = 0; i < backend.sent.Count; i++)
            Assert.That(backend.sent[i].channel, Is.EqualTo(Channel.ReliableOrdered));

        batch.Flush();
        Assert.That(backend.sent, Has.Count.EqualTo(targets.Length * 2));
        for (int i = targets.Length; i < backend.sent.Count; i++)
            Assert.That(backend.sent[i].channel, Is.EqualTo(Channel.Unreliable));

        backend.DeliverAll();
        for (int targetIdx = 0; targetIdx < targets.Length; targetIdx++)
        {
            for (int channelIdx = 0; channelIdx < 2; channelIdx++)
            {
                var channel = channelIdx == 0 ? Channel.ReliableOrdered : Channel.Unreliable;
                var key = new BatchKey { playerId = targets[targetIdx], channel = channel };
                Assert.That(received.TryGetValue(key, out var values), Is.True);
                Assert.That(values, Is.EqualTo(new[] { 0, 1, 2 }));
            }
        }
    }

    [Test]
    public void FilteredSmallFanoutSendsOnlyToIncludedTargets()
    {
        var targets = new[]
        {
            new PlayerID(31, false),
            new PlayerID(32, false),
            new PlayerID(33, false),
            new PlayerID(34, false)
        };
        var filter = new ObserverFilter(targets[0], true, targets[3], true);
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        WritePayload(payload, 0, 8);
        batch.Queue(targets, MakeHeader(Channel.ReliableOrdered, 0), new BitData(payload),
            Channel.ReliableOrdered, filter);
        batch.Flush();

        Assert.That(backend.Count(targets[0], Channel.ReliableOrdered), Is.Zero);
        Assert.That(backend.Count(targets[1], Channel.ReliableOrdered), Is.EqualTo(1));
        Assert.That(backend.Count(targets[2], Channel.ReliableOrdered), Is.EqualTo(1));
        Assert.That(backend.Count(targets[3], Channel.ReliableOrdered), Is.Zero);

        backend.DeliverAll();
        Assert.That(received.ContainsKey(new BatchKey
        {
            playerId = targets[0],
            channel = Channel.ReliableOrdered
        }), Is.False);
        Assert.That(received.ContainsKey(new BatchKey
        {
            playerId = targets[3],
            channel = Channel.ReliableOrdered
        }), Is.False);
    }
}
