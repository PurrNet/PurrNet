using System;
using System.Collections.Generic;
using K4os.Compression.LZ4;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    internal struct BigDataState
    {
        public PackedUInt id;
        public PlayerID player;
        public int sentPartsCount;
        public DisposableList<int> confirmedParts;
        public DisposableList<int> requestedParts;
    }

    internal struct BigDataReceiveState
    {
        public PackedUInt id;
        public int totalParts;
        public int totalLength;
        public float timeSinceLastReceivedPart;
        public DisposableList<int> confirmedParts;
    }

    [Serializable]
    public struct SyncStatus
    {
        public float percent;
        public bool isDone;

        public bool Equals(SyncStatus other)
        {
            return Mathf.Approximately(percent, other.percent) && isDone == other.isDone;
        }
    }

    [Serializable]
    public class SyncBigData : NetworkModule, ITick
    {
        [SerializeField] private SyncStatus _syncStatus;
        [SerializeField, PurrLock] private bool _ownerAuth;
        [SerializeField, Min(1)] private int _maxKBPerSec;

        private List<BigDataState> _pending = new ();

        public SyncStatus syncStatus
        {
            get => _syncStatus;
            private set => _syncStatus = value;
        }
        private BigDataReceiveState _receivingState;

        public event Action<SyncStatus> onSyncStatusChanged;

        /// <summary>
        /// Is the data ready to be used?
        /// Aka, download finished and uncompressed data is available.
        /// </summary>
        public bool isDataReady => _syncStatus.percent == 0 || _syncStatus.isDone;

        /// <summary>
        /// Download progress, between 0 and 1.
        /// </summary>
        public float progress => _syncStatus.percent;

        public ArraySegment<byte> compressedData => _compressedData ?? Array.Empty<byte>();
        public ArraySegment<byte> data => _uncompressedData ?? Array.Empty<byte>();

        private byte[] _uncompressedData;
        private byte[] _compressedData;
        private const int PART_SIZE = 1000;
        private int _totalParts;

        public int maxKBPerSec
        {
            get => _maxKBPerSec;
            set => _maxKBPerSec = Mathf.Max(1, value);
        }

        public SyncBigData(bool ownerAuth = false, int maxKBPerSec = 15)
        {
            _ownerAuth = ownerAuth;
            _maxKBPerSec = Mathf.Max(1, maxKBPerSec);
        }

        public override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            if (!IsController(_ownerAuth))
                _pending?.Clear();
        }

        public override void OnOwnerDisconnected(PlayerID ownerId)
        {
            // if owner disconnected and we have partial data, clear it since we can't download it anymore'
            if (_ownerAuth && _syncStatus is { percent: > 0, isDone: false })
            {
                ClearData();
            }
        }

        public void ClearData()
        {
            SetData(default);
        }

        public void SetData(ReadOnlySpan<byte> data)
        {
            if (!isSpawned)
            {
                PurrLogger.LogError($"Trying to set data on `<b>{GetType().Name} {name}</b>` which is not spawned.", parent);
                return;
            }

            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError(
                    $"Invalid permissions when setting `<b>{GetType().Name} {name}</b>` on `{parent.name}`." +
                    $"\n{GetPermissionErrorDetails(_ownerAuth, this)}", parent);
                return;
            }

            ++_nextId;
            _syncStatus = default;
            _receivingState = default;

            if (!data.IsEmpty)
            {
                _uncompressedData = data.ToArray();
                _compressedData = LZ4Pickler.Pickle(_uncompressedData, LZ4Level.L12_MAX);
                _totalParts = (int)Math.Ceiling(_compressedData.Length / (double)PART_SIZE);
            }
            else
            {
                _uncompressedData = Array.Empty<byte>();
                _compressedData = Array.Empty<byte>();
                _totalParts = 0;
            }

            OnDataReady();
            ReQueueEveryone();
        }

        private uint _nextId;

        public override void OnObserverAdded(PlayerID player)
        {
            if (_compressedData == null)
                return;

            if (player == localPlayer)
                return;

            _pending ??= new List<BigDataState>();
            _pending.Add(new BigDataState
            {
                player = player,
                id = _nextId,
                confirmedParts = DisposableList<int>.Create(),
                requestedParts = DisposableList<int>.Create()
            });
        }

        public override void OnObserverRemoved(PlayerID player)
        {
            _pending?.RemoveAll(v => v.player == player);
        }

        private void ReQueueEveryone()
        {
            _pending ??= new List<BigDataState>();
            _pending.Clear();

            if (_compressedData == null)
                return;

            if (isServer)
            {
                for (var i = parent.observers.Count - 1; i >= 0; i--)
                {
                    var observer = parent.observers[i];

                    if (observer == localPlayer || (_ownerAuth && observer == owner))
                        continue;

                    _pending.Add(new BigDataState
                    {
                        player = observer,
                        id = _nextId,
                        confirmedParts = DisposableList<int>.Create(),
                        requestedParts = DisposableList<int>.Create()
                    });
                }
            }
            else if (IsController(_ownerAuth))
            {
                _pending.Add(new BigDataState
                {
                    player = PlayerID.Server,
                    id = _nextId,
                    confirmedParts = DisposableList<int>.Create(),
                    requestedParts = DisposableList<int>.Create()
                });
            }
        }

        private ByteData GetDataPart(int part)
        {
            if (_compressedData == null) return default;

            if (part >= _totalParts)
                return default;

            if (part == _totalParts - 1)
            {
                return new ByteData(_compressedData, part * PART_SIZE,
                    _compressedData.Length - part * PART_SIZE);
            }

            return new ByteData(_compressedData, part * PART_SIZE, PART_SIZE);
        }

        private float _partsCounter;

        public void OnTick(float delta)
        {
            if (_syncStatus is { percent: > 0, isDone: false })
            {
                float timeSinceLastPart = Time.unscaledTime - _receivingState.timeSinceLastReceivedPart;
                float expectedPerTick = _maxKBPerSec * delta;
                float minTimeToPart = 1f / expectedPerTick;

                if (timeSinceLastPart > minTimeToPart * 3)
                {
                    _receivingState.timeSinceLastReceivedPart = Time.unscaledTime;
                    RequestMissingParts();
                }
            }

            if (_pending == null || _pending.Count == 0)
                return;

            _partsCounter += _maxKBPerSec * delta;

            if (_partsCounter < 1)
                return;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var state = _pending[i];

                bool isFirst = state.sentPartsCount == 0;

                if (isFirst)
                {
                    SendDownloadStart(ref state);
                    _pending[i] = state;
                    continue;
                }

                bool hasFirstPart = state.confirmedParts.Count > 0;

                if (!hasFirstPart)
                    continue;

                int partsBudget = (int)_partsCounter;
                SendNewParts(ref partsBudget, ref state);
                SendRequestedParts(partsBudget, state);
                _pending[i] = state;
            }

            _partsCounter = Mathf.Max(0, _partsCounter - (int)_partsCounter);
        }

        private void RequestMissingParts()
        {
            var parts = DisposableList<Size>.Create();
            for (int i = 1; i < _receivingState.confirmedParts.Count; i++)
            {
                var prev = _receivingState.confirmedParts[i - 1];
                var curr = _receivingState.confirmedParts[i];

                for (int j = prev + 1; j < curr; j++)
                    parts.Add(j);
            }

            int lastConfirmed = _receivingState.confirmedParts[^1];
            if (lastConfirmed < _totalParts - 1)
            {
                for (int j = lastConfirmed + 1; j < _totalParts; j++)
                    parts.Add(j);
            }

            const int MAX_TOTAL_ENTRIES = 300;
            if (parts.Count > MAX_TOTAL_ENTRIES)
                parts.RemoveRange(MAX_TOTAL_ENTRIES, parts.Count - MAX_TOTAL_ENTRIES);

            const int MAX_ENTRIES = 100;

            bool isServerAndOwnerAuth = isServer && _ownerAuth && owner.HasValue;

            if (parts.Count > MAX_ENTRIES)
            {
                int chunks = parts.Count / MAX_ENTRIES;

                for (int i = 0; i < chunks; i++)
                {
                    using var chunk = DisposableList<Size>.Create();
                    int start = i * MAX_ENTRIES;
                    for (int j = 0; j < MAX_ENTRIES; j++)
                        chunk.Add(parts[start + j]);

                    if (isServerAndOwnerAuth)
                         RequestMissingParts(owner.Value, chunk);
                    else RequestMissingParts(chunk);
                }

                parts.RemoveRange(chunks * MAX_ENTRIES, parts.Count - chunks * MAX_ENTRIES);
            }

            if (parts.Count > 0)
            {
                if (isServerAndOwnerAuth)
                     RequestMissingParts(owner.Value, parts);
                else RequestMissingParts(parts);
            }
        }

        [TargetRpc(Channel.Unreliable)]
        private void RequestMissingParts(PlayerID owner, DisposableList<Size> parts)
        {
            HandlePending(parts, PlayerID.Server);
        }

        [ServerRpc(Channel.Unreliable)]
        private void RequestMissingParts(DisposableList<Size> parts, RPCInfo info = default)
        {
            HandlePending(parts, info.sender);
        }

        private void HandlePending(DisposableList<Size> parts, PlayerID sender)
        {
            using (parts)
            {
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    var v = _pending[i];
                    if (v.player == sender)
                    {
                        for (var p = 0; p < parts.Count; p++)
                        {
                            var part = parts[p];
                            if (!v.requestedParts.Contains(part))
                                v.requestedParts.Add(part);
                        }
                        break;
                    }
                }
            }
        }

        private void SendRequestedParts(int partsBudget, BigDataState state)
        {
            bool proxying = isServer && _syncStatus is { isDone: false, percent: > 0 } &&
                            !_receivingState.confirmedParts.isDisposed;

            if (partsBudget > 0)
            {
                for (int j = 0; j < state.requestedParts.Count; j++)
                {
                    if (partsBudget <= 0)
                        break;

                    int requestedPartId = state.requestedParts[j];

                    // if we are proxying incomplete stuff
                    if (proxying)
                    {
                        // make sure we have the part that was requested
                        bool hasPart = _receivingState.confirmedParts.Contains(requestedPartId);
                        if (!hasPart)
                            continue;
                    }

                    var part = GetDataPart(requestedPartId);

                    if (state.player == PlayerID.Server)
                        SendPartToServer(state.id, part, requestedPartId);
                    else SendPartToTarget(state.player, state.id, part, requestedPartId);

                    state.requestedParts.RemoveAt(j--);
                    --partsBudget;
                }
            }
        }

        private void SendNewParts(ref int partsBudget, ref BigDataState state)
        {
            bool proxying = isServer && _syncStatus is { isDone: false, percent: > 0 } &&
                            !_receivingState.confirmedParts.isDisposed;

            for (int j = partsBudget; j >= 0; --j)
            {
                if (state.sentPartsCount < _totalParts)
                {
                    var part = GetDataPart(state.sentPartsCount);
                    if (state.player == PlayerID.Server)
                    {
                        SendPartToServer(state.id, part, state.sentPartsCount);
                    }
                    else
                    {
                        if (proxying)
                        {
                            bool hasPart = _receivingState.confirmedParts.Contains(state.sentPartsCount);
                            if (!hasPart)
                                break;
                        }
                        SendPartToTarget(state.player, state.id, part, state.sentPartsCount);
                    }
                    ++state.sentPartsCount;
                    --partsBudget;
                }
                else break;
            }
        }

        private void SendDownloadStart(ref BigDataState state)
        {
            if (isServer)
                 SendFirstPart(state.player, state.id, GetDataPart(0), _totalParts, _compressedData.Length);
            else SendFirstPartToServer(state.id, GetDataPart(0), _totalParts, _compressedData.Length);
            state.sentPartsCount++;
        }

        [ServerRpc]
        private void SendFirstPartToServer(PackedUInt tid, ByteData data, int totalParts, int totalLength)
        {
            if (!_ownerAuth || !owner.HasValue)
                return;

            HandleFirstPart(tid, data, totalParts, totalLength);
            ConfirmFirstPartWithOwner(owner.Value);
            ReQueueEveryone();
        }

        [TargetRpc]
        private void SendFirstPart(PlayerID player, PackedUInt tid, ByteData data, int totalParts, int totalLength)
        {
            HandleFirstPart(tid, data, totalParts, totalLength);
            ConfirmFirstPart();
        }

        private void HandleFirstPart(PackedUInt tid, ByteData data, int totalParts, int totalLength)
        {
            _receivingState = new BigDataReceiveState
            {
                id = tid,
                totalParts = totalParts,
                totalLength = totalLength,
                confirmedParts = DisposableList<int>.Create()
            };

            _totalParts = totalParts;

            if (_compressedData == null)
                _compressedData = new byte[totalLength];
            else if (_compressedData.Length != totalLength)
                Array.Resize(ref _compressedData, totalLength);

            InsertConfirmedPart(data, 0);
        }

        [ServerRpc(channel: Channel.Unreliable)]
        private void SendPartToServer(PackedUInt id, ByteData data, int partId)
        {
            if (_receivingState.id != id)
                return;
            InsertConfirmedPart(data, partId);
        }

        [TargetRpc(channel: Channel.Unreliable)]
        private void SendPartToTarget(PlayerID player, PackedUInt id, ByteData data, int partId)
        {
            if (_receivingState.id != id)
                return;
            InsertConfirmedPart(data, partId);
        }

        private void InsertConfirmedPart(ByteData data, int partId)
        {
            if (_receivingState.confirmedParts.isDisposed)
                return;

            int toInsert = _receivingState.confirmedParts.Count;
            for (int i = 0; i < toInsert; i++)
            {
                if (_receivingState.confirmedParts[i] > partId)
                {
                    toInsert = i;
                    break;
                }

                // If we already have this part, we're done'
                if (_receivingState.confirmedParts[i] == partId) return;
            }

            _receivingState.confirmedParts.Insert(toInsert, partId);
            _receivingState.timeSinceLastReceivedPart = Time.unscaledTime;

            int partSize = Mathf.Min(PART_SIZE, _receivingState.totalLength - partId * PART_SIZE);
            if (partSize > 0)
                Array.Copy(data.data, data.offset, _compressedData, partId * PART_SIZE, partSize);

            syncStatus = new SyncStatus
            {
                percent = _receivingState.confirmedParts.Count / (float)Mathf.Max(1, _receivingState.totalParts),
                isDone = _receivingState.confirmedParts.Count >= _receivingState.totalParts
            };

            if (syncStatus.isDone)
            {
                _uncompressedData = LZ4Pickler.Unpickle(_compressedData);
                OnDataReady();
            }
            onSyncStatusChanged?.Invoke(syncStatus);
        }

        protected virtual void OnDataReady() {}

        delegate void ModifyEntry(ref BigDataState entry);

        private bool ModifyState(PlayerID player, ModifyEntry modify)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var v = _pending[i];
                if (v.player == player)
                {
                    modify(ref v);
                    _pending[i] = v;
                    return true;
                }
            }

            return false;
        }

        [ServerRpc(requireOwnership: false)]
        private void ConfirmFirstPart(RPCInfo info = default)
        {
            if (!ModifyState(info.sender, ConfirmFirstEntry))
            {
                PurrLogger.LogError($"Failed to confirm first part for player {info.sender} for " +
                                    $"`<b>{GetType().Name} {name}</b>` on `{parent.name}`", parent);
            }
        }

        [TargetRpc]
        private void ConfirmFirstPartWithOwner(PlayerID target)
        {
            if (!ModifyState(PlayerID.Server, ConfirmFirstEntry))
            {
                PurrLogger.LogError($"Failed to confirm first part for Server for " +
                                    $"`<b>{GetType().Name} {name}</b>` on `{parent.name}`", parent);
            }
        }

        static void ConfirmFirstEntry(ref BigDataState entry)
        {
            entry.confirmedParts.Add(0);
        }
    }
}
