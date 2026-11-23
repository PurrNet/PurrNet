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

#if PURRNET_TERRAIN
        const int TerrainHeightReadBytes = 256 * 1024;
        const int TerrainHoleReadBytes = 64 * 1024;
        const int TerrainMaterialSamples = 32;

        sealed class TerrainCapture
        {
            public TerrainData data;
            public long key;
            public int id, print, side, nextHeight;
            public List<float[,]> heights;
            public byte[] holes;
            public int materialWidth, materialHeight, materialLayers, nextMaterialRow;
            public int[] materials;
        }

        readonly Queue<TerrainCapture> _terrainCaptures = new Queue<TerrainCapture>();
        readonly Dictionary<long, TerrainCapture> _terrainRequests = new Dictionary<long, TerrainCapture>();
        int _lastTerrainFrame = -1;
        bool _terrainRefresh;

        /// <summary>
        /// Writes a native-resolution height grid. The viewer derives the vertices and triangles from the grid.
        /// </summary>
        int WriteTerrain(TerrainData data)
        {
            long key = RecordingObjectId.Of(data);
            int print = TerrainPrint(data);
            if (_meshIds.TryGetValue(key, out int existing) && _terrainPrints.TryGetValue(key, out int last) && last == print)
                return existing;
            CancelTerrainCapture(key);
            _terrainPrints[key] = print;

            try
            {
                int side = data.heightmapResolution;
                int id = _nextMeshId++;
                if (!RecordingFile.encodesOnMainThread && side * side * sizeof(float) > TerrainHeightReadBytes)
                {
                    // The reference can be backdated immediately. The file contains the complete grid only
                    // after all reads agree with the same terrain revision.
                    var capture = new TerrainCapture { data = data, key = key, id = id, print = print, side = side };
                    _terrainCaptures.Enqueue(capture);
                    _terrainRequests.Add(key, capture);
                }
                else
                {
                    var heights = new List<float[,]> { data.GetHeights(0, 0, side, side) };
                    byte[] holes = null;
                    int holeSide = side - 1;
                    int rowsPerRead = TerrainHoleReadBytes / holeSide;
                    for (int z = 0; z < holeSide; z += rowsPerRead)
                        ReadTerrainHoles(data, holeSide, z, Math.Min(rowsPerRead, holeSide - z), ref holes);
                    _terrainSampled = true;
                    EnqueueTerrain(data, side, id, heights, holes);
                    if (data.alphamapLayers > 1)
                    {
                        var capture = new TerrainCapture
                            { data = data, key = key, id = id, print = print, side = side, nextHeight = side };
                        _terrainCaptures.Enqueue(capture);
                        _terrainRequests.Add(key, capture);
                    }
                }
                return _meshIds[key] = id;
            }
            catch (Exception)
            {
                return _meshIds[key] = 0;
            }
        }

        void PumpTerrainCapture()
        {
            if (_file == null || _file.error != null || _terrainCaptures.Count == 0 || _lastTerrainFrame == Time.frameCount)
                return;
            _lastTerrainFrame = Time.frameCount;
            var capture = _terrainCaptures.Peek();
            if (!_terrainRequests.TryGetValue(capture.key, out var pending) || !ReferenceEquals(pending, capture))
            {
                _terrainCaptures.Dequeue();
                return;
            }
            if (capture.nextHeight == 0 && (_file.pendingTerrainBytes > TerrainHeightReadBytes || lastClosing != null))
                return;
            var data = capture.data;
            if (!data || TerrainPrint(data) != capture.print)
            {
                InvalidateTerrain(capture.key);
                _terrainCaptures.Dequeue();
                return;
            }

            try
            {
                if (capture.nextHeight < capture.side)
                {
                    ReadTerrainHeightSlice(capture);
                    if (capture.nextHeight < capture.side || data.alphamapLayers > 1)
                        return;
                }
                else if (!ReadTerrainMaterialRow(capture))
                    return;
            }
            catch (Exception)
            {
                // Its collider has bounds available even when Unity cannot read the grid.
            }
            _terrainCaptures.Dequeue();
            CancelTerrainCapture(capture.key);
        }

        void ReadTerrainHeightSlice(TerrainCapture capture)
        {
            var data = capture.data;
            int side = capture.side;
            int z = capture.nextHeight;
            int rows = Math.Min(TerrainHeightReadBytes / (side * sizeof(float)), side - z);
            capture.heights ??= new List<float[,]>();
            capture.heights.Add(data.GetHeights(0, z, side, rows));
            int holeSide = side - 1;
            int holeRows = Math.Min(rows, holeSide - z);
            if (holeRows > 0)
                ReadTerrainHoles(data, holeSide, z, holeRows, ref capture.holes);
            capture.nextHeight += rows;
            if (capture.nextHeight == side)
            {
                EnqueueTerrain(data, side, capture.id, capture.heights, capture.holes);
                capture.heights = null;
                capture.holes = null;
            }
        }

        // Sample one row of cell centers per frame. Point reads keep allocations independent of alphamap size.
        bool ReadTerrainMaterialRow(TerrainCapture capture)
        {
            var data = capture.data;
            int layers = data.alphamapLayers;
            int width = data.alphamapWidth, height = data.alphamapHeight;
            int side = Math.Min(TerrainMaterialSamples, Math.Min(width, height));
            if (layers < 2 || side == 0)
                return true;

            if (capture.materials == null || capture.materialWidth != width ||
                capture.materialHeight != height || capture.materialLayers != layers)
            {
                capture.materialWidth = width;
                capture.materialHeight = height;
                capture.materialLayers = layers;
                capture.materials = new int[side * side];
                capture.nextMaterialRow = 0;
            }

            int row = capture.nextMaterialRow++;
            int z = (2 * row + 1) * height / (2 * side);
            for (int col = 0; col < side; col++)
            {
                int x = (2 * col + 1) * width / (2 * side);
                var weights = data.GetAlphamaps(x, z, 1, 1);
                int dominant = 0;
                for (int layer = 1; layer < layers; layer++)
                    if (weights[0, 0, layer] > weights[0, 0, dominant])
                        dominant = layer;
                capture.materials[row * side + col] = dominant;
            }
            if (capture.nextMaterialRow < side)
                return false;

            var terrainLayers = data.terrainLayers;
            if (terrainLayers.Length != layers)
                return true;
            var buffer = _meshBuffer;
            buffer.Clear();
            buffer.WriteVarUInt((ulong)capture.id);
            buffer.WriteVarUInt((ulong)side);
            buffer.WriteVarUInt((ulong)layers);
            foreach (var layer in terrainLayers)
                buffer.WriteString(layer ? Name(layer) : string.Empty);
            foreach (int material in capture.materials)
                buffer.WriteVarUInt((ulong)material);
            WriteMeta(RecordKind.TerrainMaterials, buffer.data, buffer.length);
            return true;
        }

        void EnqueueTerrain(TerrainData data, int side, int id, List<float[,]> heights, byte[] holes)
        {
            // Id, UTF-8 name, side (varuints), size.xyz, normalized heights in z/x order,
            // then a bit per quad (least-significant bit first) if any quads are holes.
            var prefix = _meshBuffer;
            prefix.Clear();
            prefix.WriteVarUInt((ulong)id);
            prefix.WriteString(Name(data));
            prefix.WriteVarUInt((ulong)side);
            var size = data.size;
            prefix.WriteFloat(size.x);
            prefix.WriteFloat(size.y);
            prefix.WriteFloat(size.z);
            _file.EnqueueTerrain(prefix, heights, holes);
        }

        void CancelTerrainCapture(long key)
        {
            if (_terrainRequests.Remove(key, out var capture))
            {
                capture.heights = null;
                capture.holes = null;
                capture.materials = null;
            }
        }

        void InvalidateTerrain(long key)
        {
            CancelTerrainCapture(key);
            _terrainRefresh |= _meshIds.Remove(key);
        }

        static unsafe void ReadTerrainHoles(TerrainData data, int side, int z, int rows, ref byte[] mask)
        {
            // Skip solid rows with one comparison.
            byte* solid = stackalloc byte[side];
            UnsafeUtility.MemSet(solid, 1, side);
            var holes = data.GetHoles(0, z, side, rows);
            fixed (bool* surface = &holes[0, 0])
            {
                for (int row = 0; row < rows; row++)
                {
                    bool* rowSurface = surface + row * side;
                    if (UnsafeUtility.MemCmp(rowSurface, solid, side) == 0)
                        continue;
                    mask ??= new byte[(side * side + 7) / 8];
                    int cell = (z + row) * side;
                    for (int x = 0; x < side; x++, cell++)
                        if (!rowSurface[x])
                            mask[cell >> 3] |= (byte)(1 << (cell & 7));
                }
            }
        }

        /// <summary>
        /// A few heights and the height range of a terrain: a game that reuses a terrain somewhere else with other
        /// heights changes them, and the terrain is sampled again.
        /// </summary>
        static int TerrainPrint(TerrainData data)
        {
            int resolution = data.heightmapResolution, quarter = resolution / 4, far = quarter * 3;
            var bounds = data.bounds;
            int print = unchecked(resolution * 31 + data.size.GetHashCode());
            print = unchecked(print * 31 + BitConverter.SingleToInt32Bits(bounds.center.y));
            print = unchecked(print * 31 + BitConverter.SingleToInt32Bits(bounds.size.y));
            print = unchecked(print * 31 + BitConverter.SingleToInt32Bits(data.GetHeight(quarter, quarter)));
            print = unchecked(print * 31 + BitConverter.SingleToInt32Bits(data.GetHeight(far, quarter)));
            print = unchecked(print * 31 + BitConverter.SingleToInt32Bits(data.GetHeight(quarter, far)));
            print = unchecked(print * 31 + BitConverter.SingleToInt32Bits(data.GetHeight(far, far)));
            print = unchecked(print * 31 + BitConverter.SingleToInt32Bits(data.GetHeight(resolution / 2, resolution / 2)));
            return print;
        }

        void OnTerrainChanged(Terrain terrain, RectInt region, bool synched)
        {
            if (_file != null && terrain && terrain.terrainData)
                InvalidateTerrain(RecordingObjectId.Of(terrain.terrainData));
        }

        void OnTerrainTextureChanged(Terrain terrain, string textureName, RectInt region, bool synched)
        {
            if (textureName == TerrainData.HolesTextureName)
                OnTerrainChanged(terrain, region, synched);
            else if (textureName == TerrainData.AlphamapTextureName && _file != null && terrain && terrain.terrainData &&
                     _terrainRequests.TryGetValue(RecordingObjectId.Of(terrain.terrainData), out var capture))
                capture.nextMaterialRow = 0;
        }
#endif
    }
}
#endif
