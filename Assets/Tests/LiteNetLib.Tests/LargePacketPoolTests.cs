using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LiteNetLib;
using NUnit.Framework;

namespace PurrNet.Tests
{
    public sealed class LargePacketPoolTests
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly MethodInfo Rent = typeof(LiteNetManager).GetMethod("PoolGetPacket", Fields);
        static readonly MethodInfo Return = typeof(LiteNetManager).GetMethod("PoolRecycle", Fields);

        static object Get(LiteNetManager manager, int size) => Rent.Invoke(manager, new object[] { size });
        static void Recycle(LiteNetManager manager, object packet) => Return.Invoke(manager, new[] { packet });
        static T Field<T>(object packet, string name) => (T)packet.GetType().GetField(name).GetValue(packet);

        [Test]
        public void ReusesSizeClassWithoutChangingLogicalLengthOrReusingBorrowedPackets()
        {
            var manager = new NetManager(new EventBasedNetListener());
            var first = Get(manager, 3000);
            var bytes = Field<byte[]>(first, "RawData");
            first.GetType().GetField("UserData").SetValue(first, new object());
            bytes[0] = 255;
            Recycle(manager, first);
            var reused = Get(manager, 3500);
            Assert.That(reused, Is.SameAs(first));
            Assert.That(Field<byte[]>(reused, "RawData"), Is.SameAs(bytes));
            Assert.That(bytes.Length, Is.EqualTo(4096));
            Assert.That(Field<int>(reused, "Size"), Is.EqualTo(3500));
            Assert.That(bytes[0], Is.Zero);
            Assert.That(Field<object>(reused, "UserData"), Is.Null);
            var borrowed = Get(manager, 3000);
            Assert.That(borrowed, Is.Not.SameAs(reused));
            var small = Get(manager, 1000);
            Assert.That(small, Is.Not.SameAs(reused));
            Recycle(manager, reused); Recycle(manager, borrowed); Recycle(manager, small);
        }

        [Test]
        public void RetentionIsBoundedAndVeryLargeMessagesRemainUnpooled()
        {
            var manager = new NetManager(new EventBasedNetListener());
            var packets = new object[32];
            for (int i = 0; i < packets.Length; i++) packets[i] = Get(manager, 60000);
            foreach (var packet in packets) Recycle(manager, packet);
            Assert.That(manager.PoolCount, Is.EqualTo(4));
            Assert.That(typeof(LiteNetManager).GetField("_largePoolBytes", Fields).GetValue(manager), Is.EqualTo(256 * 1024));
            for (int i = 0; i < 4; i++) Get(manager, 60000);
            Assert.That(manager.PoolCount, Is.Zero);
            Recycle(manager, Get(manager, 70000));
            Assert.That(manager.PoolCount, Is.Zero);
        }

        [Test]
        public void DisabledPoolKeepsExactAllocationSizesAndRetainsNothing()
        {
            var manager = new NetManager(new EventBasedNetListener()) { PacketPoolSize = 0 };
            var packet = Get(manager, 3000);
            Assert.That(Field<byte[]>(packet, "RawData").Length, Is.EqualTo(3000));
            Recycle(manager, packet);
            Assert.That(manager.PoolCount, Is.Zero);
        }

        [Test]
        public void ConcurrentReturnsRespectTheSharedPacketCountLimit()
        {
            var manager = new NetManager(new EventBasedNetListener()) { PacketPoolSize = 7 };
            var packets = new object[64];
            for (int i = 0; i < packets.Length; i++) packets[i] = Get(manager, 3000);
            Parallel.ForEach(packets, packet => Recycle(manager, packet));
            Assert.That(manager.PoolCount, Is.EqualTo(7));
            Recycle(manager, Get(manager, 1000));
            Assert.That(manager.PoolCount, Is.EqualTo(7));
            var leased = new HashSet<object>();
            for (int i = 0; i < 7; i++) Assert.That(leased.Add(Get(manager, 3000)), Is.True);
            Assert.That(manager.PoolCount, Is.Zero);
        }
    }
}
