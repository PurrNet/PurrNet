#if UNITY_PHYSICS_3D
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace PurrNet.Recording
{
    /// <summary>
    /// Copies a mesh's positions and triangle indices out of its raw vertex and index buffers into a MESH record's
    /// payload. Runs on a worker thread.
    /// </summary>
    unsafe struct MeshExtractJob : IJob
    {
        [ReadOnly] public NativeArray<byte> vertices;
        [ReadOnly] public NativeArray<byte> indices;
        /// <summary>Per sub-mesh: first index, index count, base vertex.</summary>
        [ReadOnly] public NativeArray<int> subMeshes;
        public int vertexStride, vertexOffset, vertexCount, indexStride;
        public bool half;
        [NativeDisableUnsafePtrRestriction] public byte* vertexOut;
        [NativeDisableUnsafePtrRestriction] public byte* indexOut;

        public void Execute()
        {
            byte* source = (byte*)vertices.GetUnsafeReadOnlyPtr() + vertexOffset;
            if (!half)
                UnsafeUtility.MemCpyStride(vertexOut, 12, source, vertexStride, 12, vertexCount);
            else
            {
                float* v = stackalloc float[3];
                for (int i = 0; i < vertexCount; i++)
                {
                    ushort* p = (ushort*)(source + (long)i * vertexStride);
                    v[0] = HalfToFloat(p[0]);
                    v[1] = HalfToFloat(p[1]);
                    v[2] = HalfToFloat(p[2]);
                    UnsafeUtility.MemCpy(vertexOut + (long)i * 12, v, 12);
                }
            }

            byte* index = (byte*)indices.GetUnsafeReadOnlyPtr();
            byte* target = indexOut;
            for (int sub = 0; sub < subMeshes.Length; sub += 3)
            {
                int start = subMeshes[sub], count = subMeshes[sub + 1], baseVertex = subMeshes[sub + 2];
                if (indexStride == 4 && baseVertex == 0)
                {
                    UnsafeUtility.MemCpy(target, index + (long)start * 4, (long)count * 4);
                    target += (long)count * 4;
                    continue;
                }
                for (int i = 0; i < count; i++)
                {
                    int value = baseVertex + (indexStride == 2
                        ? ((ushort*)index)[start + i]
                        : ((int*)index)[start + i]);
                    UnsafeUtility.MemCpy(target, &value, 4);
                    target += 4;
                }
            }
        }

        static float HalfToFloat(ushort half)
        {
            uint sign = (uint)(half >> 15) << 31, exponent = (uint)(half >> 10) & 0x1F, mantissa = (uint)half & 0x3FF;
            uint bits;
            if (exponent == 0)
            {
                if (mantissa == 0)
                    bits = sign;
                else
                {
                    int shift = 0;
                    while ((mantissa & 0x400) == 0)
                    {
                        mantissa <<= 1;
                        shift++;
                    }
                    bits = sign | (uint)(113 - shift) << 23 | (mantissa & 0x3FF) << 13;
                }
            }
            else if (exponent == 31)
                bits = sign | 0x7F800000u | mantissa << 13;
            else
                bits = sign | (exponent + 112) << 23 | mantissa << 13;
            return *(float*)&bits;
        }
    }

    /// <summary>
    /// Collider meshes. A mesh is written once per recording, and nothing about it has to happen on the tick it is
    /// first seen: the main thread only asks for the mesh's buffers and hands the result to the writer. A job copies
    /// the positions and indices out, and the writer thread compresses and writes them from native memory.
    /// </summary>
    public sealed partial class RecordingSession
    {
        /// <summary>Tests: read meshes back from the GPU in the editor too, like a player does for unreadable meshes.</summary>
        internal static bool forceGpuMeshRead;

        sealed unsafe class MeshCapture
        {
            public RecordingFile file;
            public long key;
            public int id;
            /// <summary>The id is only given to colliders once the data is there (a GPU read can fail).</summary>
            public bool publish;
            public byte* output;
            public int outputLength, vertexBytesAt, indexBytesAt;
            public Mesh.MeshDataArray meshData;
            public bool hasMeshData;
            public GraphicsBuffer vertexBuffer, indexBuffer;
            public NativeArray<byte> vertexRaw, indexRaw;
            public NativeArray<int> subMeshes;
            public int vertexStride, vertexOffset, vertexCount, indexStride, indexCount;
            public bool half;
            public int pendingReads;
            public bool failed, scheduled;
            public JobHandle job;
        }

        readonly List<MeshCapture> _meshCaptures = new List<MeshCapture>();
        readonly List<Vector3> _meshVertices = new List<Vector3>();
        readonly List<int> _meshTriangles = new List<int>();
        readonly RecordingBuffer _meshBuffer = new RecordingBuffer(1024);

        /// <summary>Meshes whose data is still being copied out.</summary>
        internal int pendingMeshes => _meshCaptures.Count;

        static int VarLength(ulong value)
        {
            int length = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                length++;
            }
            return length;
        }

        static unsafe byte* WriteVar(byte* at, ulong value)
        {
            while (value >= 0x80)
            {
                *at++ = (byte)(value | 0x80);
                value >>= 7;
            }
            *at++ = (byte)value;
            return at;
        }

        /// <summary>
        /// The id of the mesh's MESH record: above 0 when colliders can refer to it, 0 when the mesh cannot be
        /// recorded (the collider is written as its bounds), -1 while a read from the GPU is under way.
        /// </summary>
        int WriteMesh(Mesh mesh)
        {
            long key = RecordingObjectId.Of(mesh);
            if (_meshIds.TryGetValue(key, out int existing))
                return existing;

            try
            {
                if (forceGpuMeshRead || (!mesh.isReadable && !Application.isEditor))
                    return _meshIds[key] = ReadMeshFromGpu(mesh, key);
                int id = ReadMeshData(mesh, key);
                return _meshIds[key] = id != 0 ? id : WriteMeshOnMainThread(mesh);
            }
            catch (Exception)
            {
                return _meshIds[key] = 0;
            }
        }

        static bool SupportedPositions(VertexAttributeFormat format, int dimension, out bool half)
        {
            half = format == VertexAttributeFormat.Float16;
            return dimension == 3 && (format == VertexAttributeFormat.Float32 || half);
        }

        /// <summary>Allocates the record's payload and writes everything but the positions and indices.</summary>
        unsafe void BeginPayload(MeshCapture capture, string name)
        {
            int nameBytes = System.Text.Encoding.UTF8.GetByteCount(name);
            int prefix = VarLength((ulong)capture.id) + VarLength((ulong)nameBytes) + nameBytes + VarLength((ulong)capture.vertexCount);
            long length = (long)prefix + (long)capture.vertexCount * 12 + VarLength((ulong)capture.indexCount) + (long)capture.indexCount * 4;
            if (length > int.MaxValue)
                throw new InvalidOperationException("mesh too large for one record");
            capture.outputLength = (int)length;
            capture.output = (byte*)UnsafeUtility.Malloc(length, 4, Allocator.Persistent);
            byte* at = WriteVar(capture.output, (ulong)capture.id);
            at = WriteVar(at, (ulong)nameBytes);
            if (nameBytes > 0)
                fixed (char* chars = name)
                    at += System.Text.Encoding.UTF8.GetBytes(chars, name.Length, at, nameBytes);
            at = WriteVar(at, (ulong)capture.vertexCount);
            capture.vertexBytesAt = (int)(at - capture.output);
            at = WriteVar(at + (long)capture.vertexCount * 12, (ulong)capture.indexCount);
            capture.indexBytesAt = (int)(at - capture.output);
        }

        unsafe void Schedule(MeshCapture capture, NativeArray<byte> vertices, NativeArray<byte> indices)
        {
            capture.job = new MeshExtractJob
            {
                vertices = vertices, indices = indices, subMeshes = capture.subMeshes,
                vertexStride = capture.vertexStride, vertexOffset = capture.vertexOffset, vertexCount = capture.vertexCount,
                indexStride = capture.indexStride, half = capture.half,
                vertexOut = capture.output + capture.vertexBytesAt, indexOut = capture.output + capture.indexBytesAt
            }.Schedule();
            capture.scheduled = true;
        }

        /// <summary>A mesh whose data the engine can hand out directly (readable, or any mesh in the editor).</summary>
        unsafe int ReadMeshData(Mesh mesh, long key)
        {
            var capture = new MeshCapture { file = _file, key = key };
            var meshData = Mesh.AcquireReadOnlyMeshData(mesh);
            try
            {
                var data = meshData[0];
                if (!data.HasVertexAttribute(VertexAttribute.Position) ||
                    !SupportedPositions(data.GetVertexAttributeFormat(VertexAttribute.Position),
                        data.GetVertexAttributeDimension(VertexAttribute.Position), out capture.half))
                    return 0;

                int stream = data.GetVertexAttributeStream(VertexAttribute.Position);
                capture.vertexOffset = data.GetVertexAttributeOffset(VertexAttribute.Position);
                capture.vertexStride = data.GetVertexBufferStride(stream);
                capture.vertexCount = data.vertexCount;
                capture.indexStride = data.indexFormat == IndexFormat.UInt16 ? 2 : 4;
                capture.subMeshes = new NativeArray<int>(data.subMeshCount * 3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                for (int sub = 0; sub < data.subMeshCount; sub++)
                {
                    var desc = data.GetSubMesh(sub);
                    if (desc.topology != MeshTopology.Triangles)
                        return 0;
                    capture.subMeshes[sub * 3] = desc.indexStart;
                    capture.subMeshes[sub * 3 + 1] = desc.indexCount;
                    capture.subMeshes[sub * 3 + 2] = desc.baseVertex;
                    capture.indexCount += desc.indexCount;
                }

                capture.id = _nextMeshId++;
                BeginPayload(capture, mesh.name);
                capture.meshData = meshData;
                capture.hasMeshData = true;
                Schedule(capture, data.GetVertexData<byte>(stream), data.GetIndexData<byte>());
                _meshCaptures.Add(capture);
                return capture.id;
            }
            finally
            {
                if (!capture.scheduled)
                {
                    meshData.Dispose();
                    Free(capture);
                }
            }
        }

        /// <summary>A mesh the engine only keeps on the GPU (not readable, in a player): its buffers are read back.</summary>
        unsafe int ReadMeshFromGpu(Mesh mesh, long key)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback)
                return 0;

            var capture = new MeshCapture { file = _file, key = key, publish = true };
            bool requested = false;
            try
            {
                int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
                if (stream < 0 || !SupportedPositions(mesh.GetVertexAttributeFormat(VertexAttribute.Position),
                        mesh.GetVertexAttributeDimension(VertexAttribute.Position), out capture.half))
                    return 0;

                capture.vertexOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
                capture.vertexBuffer = mesh.GetVertexBuffer(stream);
                capture.indexBuffer = mesh.GetIndexBuffer();
                if (capture.vertexBuffer == null || capture.indexBuffer == null)
                    return 0;
                capture.vertexStride = capture.vertexBuffer.stride;
                capture.vertexCount = capture.vertexBuffer.count;
                capture.indexStride = capture.indexBuffer.stride;
                if (capture.indexStride != 2 && capture.indexStride != 4)
                    return 0;

                int subMeshes = mesh.subMeshCount;
                capture.subMeshes = new NativeArray<int>(subMeshes * 3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                for (int sub = 0; sub < subMeshes; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                        return 0;
                    int start = (int)mesh.GetIndexStart(sub), count = (int)mesh.GetIndexCount(sub);
                    if ((long)start + count > capture.indexBuffer.count)
                        return 0;
                    capture.subMeshes[sub * 3] = start;
                    capture.subMeshes[sub * 3 + 1] = count;
                    capture.subMeshes[sub * 3 + 2] = (int)mesh.GetBaseVertex(sub);
                    capture.indexCount += count;
                }

                capture.id = _nextMeshId++;
                BeginPayload(capture, mesh.name);
                capture.vertexRaw = new NativeArray<byte>(capture.vertexBuffer.count * capture.vertexBuffer.stride, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                capture.indexRaw = new NativeArray<byte>(capture.indexBuffer.count * capture.indexBuffer.stride, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                capture.pendingReads = 2;
                requested = true;
                _meshCaptures.Add(capture);
                AsyncGPUReadback.RequestIntoNativeArray(ref capture.vertexRaw, capture.vertexBuffer, request => OnMeshRead(capture, request));
                AsyncGPUReadback.RequestIntoNativeArray(ref capture.indexRaw, capture.indexBuffer, request => OnMeshRead(capture, request));
                return -1;
            }
            finally
            {
                if (!requested)
                    Free(capture);
            }
        }

        void OnMeshRead(MeshCapture capture, AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
                capture.failed = true;
            if (--capture.pendingReads > 0)
                return;

            capture.vertexBuffer?.Dispose();
            capture.indexBuffer?.Dispose();
            capture.vertexBuffer = capture.indexBuffer = null;
            if (capture.failed || !ReferenceEquals(_file, capture.file))
            {
                if (ReferenceEquals(_file, capture.file))
                    _meshIds[capture.key] = 0;
                _meshCaptures.Remove(capture);
                Free(capture);
                return;
            }
            Schedule(capture, capture.vertexRaw, capture.indexRaw);
        }

        /// <summary>
        /// Hands finished meshes to the writer. With <paramref name="finish"/> it waits for the copies that are
        /// running, so a recording that ends has every mesh its colliders refer to.
        /// </summary>
        unsafe void PumpMeshCaptures(bool finish)
        {
            for (int i = _meshCaptures.Count - 1; i >= 0; i--)
            {
                var capture = _meshCaptures[i];
                if (!capture.scheduled)
                {
                    if (finish)
                        _meshCaptures.RemoveAt(i);
                    continue;
                }
                if (!finish && !capture.job.IsCompleted)
                    continue;

                capture.job.Complete();
                _meshCaptures.RemoveAt(i);
                if (ReferenceEquals(_file, capture.file) && _file != null)
                {
                    _file.EnqueueNative(RecordKind.Mesh, (IntPtr)capture.output, capture.outputLength, true);
                    capture.output = null;
                    if (capture.publish)
                        _meshIds[capture.key] = capture.id;
                }
                Free(capture);
            }
        }

        static unsafe void Free(MeshCapture capture)
        {
            if (capture.hasMeshData)
                capture.meshData.Dispose();
            capture.hasMeshData = false;
            capture.vertexBuffer?.Dispose();
            capture.indexBuffer?.Dispose();
            capture.vertexBuffer = capture.indexBuffer = null;
            if (capture.vertexRaw.IsCreated) capture.vertexRaw.Dispose();
            if (capture.indexRaw.IsCreated) capture.indexRaw.Dispose();
            if (capture.subMeshes.IsCreated) capture.subMeshes.Dispose();
            if (capture.output != null)
                UnsafeUtility.Free(capture.output, Allocator.Persistent);
            capture.output = null;
        }

        /// <summary>
        /// Meshes whose positions are stored in a way the copy job does not read (compressed formats): through the
        /// engine's own conversion, on the main thread. Rare.
        /// </summary>
        int WriteMeshOnMainThread(Mesh mesh)
        {
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                    return 0;

            int id = _nextMeshId++;
            mesh.GetVertices(_meshVertices);
            var buffer = _meshBuffer;
            buffer.Clear();
            buffer.WriteVarUInt((ulong)id);
            buffer.WriteString(mesh.name);
            buffer.WriteVarUInt((ulong)_meshVertices.Count);
            foreach (var v in _meshVertices)
            {
                buffer.WriteFloat(v.x);
                buffer.WriteFloat(v.y);
                buffer.WriteFloat(v.z);
            }
            int indexCount = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                indexCount += (int)mesh.GetIndexCount(sub);
            buffer.WriteVarUInt((ulong)indexCount);
            buffer.Ensure(indexCount * 4);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                mesh.GetTriangles(_meshTriangles, sub);
                foreach (int i in _meshTriangles)
                {
                    buffer.data[buffer.length++] = (byte)i;
                    buffer.data[buffer.length++] = (byte)(i >> 8);
                    buffer.data[buffer.length++] = (byte)(i >> 16);
                    buffer.data[buffer.length++] = (byte)(i >> 24);
                }
            }
            _meshVertices.Clear();
            _meshTriangles.Clear();
            WriteMeta(RecordKind.Mesh, buffer.data, buffer.length);
            return id;
        }
    }
}
#endif
