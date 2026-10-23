using System;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;

public sealed class RpcPostProcessPoolTests
{
    private static RPCInfo Info(CompressionLevel compression) => new RPCInfo
    {
        compileTimeSignature = new RPCSignature { compressionLevel = compression }
    };

    [Test]
    public void CompressedRpcReturnsTemporaryBuffersAndPreservesThePacket()
    {
        using var original = BitPackerPool.Get();
        original.WriteBits(123456, 32);
        using var packet = original.Pickle();
        var packetBits = new BitData(packet);
        var packetLength = packet.positionInBits;
        BitPacker decoded;
        using (RPCModule.BeginPostProcessScope())
        {
            RPCModule.PostProcessRpc(Info(CompressionLevel.Fast), ref packetBits);
            decoded = packetBits.packer;
            Assert.That(decoded.ReadBits(32), Is.EqualTo(123456));
            Assert.That(decoded, Is.Not.SameAs(packet));
        }
        using var rentedA = BitPackerPool.Get();
        using var rentedB = BitPackerPool.Get();
        Assert.That(rentedA == decoded || rentedB == decoded, Is.True,
            "the decompressed buffer was not returned to the pool");
        Assert.That(rentedA, Is.Not.SameAs(rentedB), "a rental was returned twice");
        Assert.That(rentedA, Is.Not.SameAs(packet));
        Assert.That(rentedB, Is.Not.SameAs(packet));
        Assert.That(packet.positionInBits, Is.EqualTo(packetLength));
        using var verify = BitPackerPool.Get();
        verify.UnpickleFrom(packet);
        verify.ResetPositionAndMode(true);
        Assert.That(verify.ReadBits(32), Is.EqualTo(123456));
    }

    [Test]
    public void UnprocessedRpcKeepsTheBorrowedPacketOutOfThePool()
    {
        using var packet = BitPackerPool.Get();
        packet.WriteBits(77, 32);
        var bits = new BitData(packet);
        using (RPCModule.BeginPostProcessScope())
        {
            RPCModule.PostProcessRpc(Info(CompressionLevel.None), ref bits);
            Assert.That(bits.packer, Is.SameAs(packet));
        }
        using var rented = BitPackerPool.Get();
        Assert.That(rented, Is.Not.SameAs(packet));
        packet.ResetPositionAndMode(true);
        Assert.That(packet.ReadBits(32), Is.EqualTo(77));
    }

    [Test]
    public void ThrowingPostProcessorStillReturnsItsCopy()
    {
        using var packet = BitPackerPool.Get();
        packet.WriteBits(99, 32);
        BitPacker copied = null;
        void Throw(RPCInfo _, ref BitData data)
        {
            copied = data.packer;
            throw new InvalidOperationException("post-processor failure");
        }
        RPCModule.onPostProcessRpc += Throw;
        try
        {
            var bits = new BitData(packet);
            using (RPCModule.BeginPostProcessScope())
                Assert.Throws<InvalidOperationException>(() =>
                    RPCModule.PostProcessRpc(Info(CompressionLevel.None), ref bits));
            using var rented = BitPackerPool.Get();
            Assert.That(rented, Is.SameAs(copied));
            packet.ResetPositionAndMode(true);
            Assert.That(packet.ReadBits(32), Is.EqualTo(99));
        }
        finally { RPCModule.onPostProcessRpc -= Throw; }
    }

    [Test]
    public void NestedProcessingKeepsTheOuterDecodedBufferAlive()
    {
        using var original = BitPackerPool.Get();
        original.WriteBits(2468, 32);
        using var packet = original.Pickle();
        var outer = new BitData(packet);
        using (RPCModule.BeginPostProcessScope())
        {
            RPCModule.PostProcessRpc(Info(CompressionLevel.Fast), ref outer);
            var decoded = outer.packer;
            BitPacker nestedDecoded;
            using (RPCModule.BeginPostProcessScope())
            {
                var nested = new BitData(packet);
                RPCModule.PostProcessRpc(Info(CompressionLevel.Fast), ref nested);
                nestedDecoded = nested.packer;
                Assert.That(nestedDecoded.ReadBits(32), Is.EqualTo(2468));
            }
            using var reusedA = BitPackerPool.Get();
            using var reusedB = BitPackerPool.Get();
            Assert.That(reusedA == nestedDecoded || reusedB == nestedDecoded, Is.True);
            Assert.That(reusedA, Is.Not.SameAs(decoded));
            Assert.That(reusedB, Is.Not.SameAs(decoded));
            Assert.That(decoded.ReadBits(32), Is.EqualTo(2468));
        }
    }
}
