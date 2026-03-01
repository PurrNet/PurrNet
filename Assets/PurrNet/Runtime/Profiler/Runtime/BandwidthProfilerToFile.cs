using System;
using System.IO;
using PurrNet.Packing;
using PurrNet.Profiler;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Debug/Bandwidth Profiler To File")]
    public class BandwidthProfilerToFile : MonoBehaviour
    {
        [SerializeField] private string _fileName = "bandwidth_profiler.data";
        [SerializeField, Min(0.001f)] private float _maxFileSizeInMb = 10;

        private BinaryWriter _writer;

        private void OnEnable()
        {
            _writer = new BinaryWriter(File.Open(_fileName, FileMode.Create));
            Statistics.inspecting += 1;
            Statistics.onSample += OnSample;
        }

        private void OnDisable()
        {
            _writer?.Flush();
            _writer?.Dispose();
            Statistics.inspecting -= 1;
            Statistics.onSample -= OnSample;
        }

        readonly Stream _copyStream = new MemoryStream();

        private void CleanupFile()
        {
            if (_writer == null) return;

            var totalSize = Mathf.CeilToInt(_maxFileSizeInMb * 1024 * 1024);

            if (_writer.BaseStream.Length < totalSize) return;

            // cut the file in half
            MovePositionToNewStart(totalSize / 2);

            _copyStream.Position = 0;
            _copyStream.SetLength(0);

            _writer.BaseStream.CopyTo(_copyStream);
            _copyStream.Position = 0;

            _writer.BaseStream.Position = 0;
            _writer.BaseStream.SetLength(0);

            _copyStream.CopyTo(_writer.BaseStream);

            _writer.Flush();
        }

        private void MovePositionToNewStart(int targetBytesToRemove)
        {
            _writer.BaseStream.Position = 0;

            int removed = 0;

            while (removed < targetBytesToRemove)
            {
                var a = _writer.BaseStream.ReadByte();
                var b = _writer.BaseStream.ReadByte();
                var c = _writer.BaseStream.ReadByte();
                var d = _writer.BaseStream.ReadByte();

                var length = (a << 24) | (b << 16) | (c << 8) | d;
                removed += length + 4;

                _writer.BaseStream.Seek(length, SeekOrigin.Current);
            }
        }

        private void OnSample(TickSample sample)
        {
            using var packer = BitPackerPool.Get();

            Packer<int>.Write(packer, sample.receivedRpcs.Count);
            foreach (var rpc in sample.receivedRpcs)
                WriteRPCSample(packer, rpc);

            Packer<int>.Write(packer, sample.sentRpcs.Count);
            foreach (var rpc in sample.sentRpcs)
                WriteRPCSample(packer, rpc);

            Packer<int>.Write(packer, sample.receivedBroadcasts.Count);

            foreach (var broadcast in sample.receivedBroadcasts)
            {
                Packer<string>.Write(packer, broadcast.type.FullName);
                var byteData = broadcast.data.ToByteData();
                Packer<int>.Write(packer, byteData.length);
                packer.WriteBytes(byteData);
            }

            Packer<int>.Write(packer, sample.sentBroadcasts.Count);

            foreach (var broadcast in sample.sentBroadcasts)
            {
                Packer<string>.Write(packer, broadcast.type.FullName);
                var byteData = broadcast.data.ToByteData();
                Packer<int>.Write(packer, byteData.length);
                packer.WriteBytes(byteData);
            }

            Packer<int>.Write(packer, sample.forwardedBytes.Count);

            foreach (var bytes in sample.forwardedBytes)
                Packer<int>.Write(packer, bytes);

            var data = packer.ToByteData();
            _writer.Write(data.length);
            _writer.Write(data.span);

            CleanupFile();
        }

        private static void WriteRPCSample(BitPacker packer, RpcsSample rpc)
        {
            Packer<string>.Write(packer, rpc.type.FullName);
            Packer<RPCType>.Write(packer, rpc.rpcType);
            Packer<string>.Write(packer, rpc.method);

            var byteData = rpc.data.ToByteData();
            Packer<int>.Write(packer, byteData.length);
            packer.WriteBytes(byteData);
        }

        static TickSample ReadSample(BitPacker packer)
        {
            var sample = new TickSample();

            int receivedRpcsCount = Packer<int>.Read(packer);

            for (int i = 0; i < receivedRpcsCount; i++)
            {
                var type = Packer<string>.Read(packer);
                var rpcType = Packer<RPCType>.Read(packer);
                var method = Packer<string>.Read(packer);
                var length = Packer<int>.Read(packer);
                var bitData = new BitData(packer, packer.positionInBits, length * 8);
                sample.receivedRpcs.Add(new RpcsSample(Type.GetType(type), rpcType, method, bitData, null));
            }

            int sentRpcsCount = Packer<int>.Read(packer);

            for (int i = 0; i < sentRpcsCount; i++)
            {
                var type = Packer<string>.Read(packer);
                var rpcType = Packer<RPCType>.Read(packer);
                var method = Packer<string>.Read(packer);
                var length = Packer<int>.Read(packer);
                var bitData = new BitData(packer, packer.positionInBits, length * 8);
                sample.sentRpcs.Add(new RpcsSample(Type.GetType(type), rpcType, method, bitData, null));
            }

            int receivedBroadcastsCount = Packer<int>.Read(packer);

            for (int i = 0; i < receivedBroadcastsCount; i++)
            {
                var type = Packer<string>.Read(packer);
                var length = Packer<int>.Read(packer);
                var dataArray = new byte[length];
                packer.ReadBytes(dataArray);
                sample.receivedBroadcasts.Add(new BroadcastSample(Type.GetType(type), dataArray));
            }

            int sentBroadcastsCount = Packer<int>.Read(packer);

            for (int i = 0; i < sentBroadcastsCount; i++)
            {
                var type = Packer<string>.Read(packer);
                var length = Packer<int>.Read(packer);
                var dataArray = new byte[length];
                packer.ReadBytes(dataArray);
                sample.sentBroadcasts.Add(new BroadcastSample(Type.GetType(type), dataArray));
            }

            int forwardedBytesCount = Packer<int>.Read(packer);

            for (int i = 0; i < forwardedBytesCount; i++)
            {
                var bytes = Packer<int>.Read(packer);
                sample.forwardedBytes.Add(bytes);
            }

            return sample;
        }

        public static void LoadSamples(string file)
        {
            NetworkManager.LoadOrGenerateHashes();

            foreach (var sample in Statistics.samples)
                sample.Dispose();
            Statistics.samples.Clear();

            if (!File.Exists(file))
                return;

            using var reader = new BinaryReader(File.OpenRead(file));
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var length = reader.ReadInt32();
                var data = reader.ReadBytes(length);
                using var packer = BitPackerPool.Get(data);
                Statistics.samples.Add(ReadSample(packer));
            }
        }
    }
}
