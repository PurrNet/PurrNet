using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet.Modules
{
    public delegate void ValueModifier<T>(ref T oldValue);

    public class DeltaModule : INetworkModule, IPostFixedUpdate, IPromoteToServerModule
    {
        private readonly PlayersManager _players;
        private readonly PlayersBroadcaster _broadcaster;
        private readonly Dictionary<PlayerID, Dictionary<KeyHash, ClientDeltaTracker>> _receivingTrackers;
        private readonly Dictionary<PlayerID, Dictionary<KeyHash, ClientDeltaTracker>> _sendingTrackers;

        private readonly List<DeltaAcknowledgeBatch> _acknowledgements = new ();

        private PlayerID _cachedWritePlayer;
        private Dictionary<KeyHash, ClientDeltaTracker> _cachedWriteDict;
        private PlayerID _cachedReadPlayer;
        private Dictionary<KeyHash, ClientDeltaTracker> _cachedReadDict;

        private bool _asServer;

        public DeltaModule(PlayersManager players, PlayersBroadcaster broadcaster)
        {
            _players = players;
            _broadcaster = broadcaster;
            _receivingTrackers = new Dictionary<PlayerID, Dictionary<KeyHash, ClientDeltaTracker>>();
            _sendingTrackers = new Dictionary<PlayerID, Dictionary<KeyHash, ClientDeltaTracker>>();
        }

        public void PromoteToServerModule()
        {
            _asServer = true;
            _acknowledgements.Clear();
            ClearTrackers();
        }

        public void PostPromoteToServerModule() { }

        public void Enable(bool asServer)
        {
            _asServer = asServer;
            _players.onPlayerLeft += OnPlayerLeft;
            _broadcaster.Subscribe<DeltaBatch>(AcknowledgeBatch);
            _broadcaster.Subscribe<DeltaAcknowledge>(Acknowledge);
            _broadcaster.Subscribe<DeltaCleanup>(Cleanup);
        }

        public void Disable(bool asServer)
        {
            _players.onPlayerLeft -= OnPlayerLeft;
            _broadcaster.Unsubscribe<DeltaBatch>(AcknowledgeBatch);
            _broadcaster.Unsubscribe<DeltaAcknowledge>(Acknowledge);
            _broadcaster.Unsubscribe<DeltaCleanup>(Cleanup);

            ClearTrackers();
        }

        private void ClearTrackers()
        {
            foreach (var clientDict in _sendingTrackers.Values)
            {
                foreach (var tracker in clientDict.Values)
                    tracker.Dispose();
            }

            foreach (var receiveDict in _receivingTrackers.Values)
            {
                foreach (var tracker in receiveDict.Values)
                    tracker.Dispose();
            }

            _sendingTrackers.Clear();
            _receivingTrackers.Clear();

            _cachedWritePlayer = default;
            _cachedWriteDict = null;
            _cachedReadPlayer = default;
            _cachedReadDict = null;
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (_receivingTrackers.Remove(player, out var receiveDict))
            {
                foreach (var tracker in receiveDict.Values)
                    tracker.Dispose();

                if (_cachedReadPlayer == player)
                {
                    _cachedReadPlayer = default;
                    _cachedReadDict = null;
                }
            }

            if (_sendingTrackers.Remove(player, out var clientDict))
            {
                foreach (var tracker in clientDict.Values)
                    tracker.Dispose();

                if (_cachedWritePlayer == player)
                {
                    _cachedWritePlayer = default;
                    _cachedWriteDict = null;
                }
            }
        }

        private ClientDeltaTracker GetTracker(PlayerID player, KeyHash key, bool isWriting)
        {
            var dictionary = isWriting ? _sendingTrackers : _receivingTrackers;
            if (!dictionary.TryGetValue(player, out var clientDict))
            {
                clientDict = new Dictionary<KeyHash, ClientDeltaTracker>();
                dictionary[player] = clientDict;
            }
            return clientDict.GetValueOrDefault(key);
        }

        private ClientDeltaTracker<T> GetOrCreateTracker<T>(PlayerID player, uint keyHash, bool isWriting)
        {
            Dictionary<KeyHash, ClientDeltaTracker> clientDict;

            if (isWriting)
            {
                if (_cachedWritePlayer == player && _cachedWriteDict != null)
                {
                    clientDict = _cachedWriteDict;
                }
                else
                {
                    if (!_sendingTrackers.TryGetValue(player, out clientDict))
                    {
                        clientDict = new Dictionary<KeyHash, ClientDeltaTracker>();
                        _sendingTrackers[player] = clientDict;
                    }
                    _cachedWritePlayer = player;
                    _cachedWriteDict = clientDict;
                }
            }
            else
            {
                if (_cachedReadPlayer == player && _cachedReadDict != null)
                {
                    clientDict = _cachedReadDict;
                }
                else
                {
                    if (!_receivingTrackers.TryGetValue(player, out clientDict))
                    {
                        clientDict = new Dictionary<KeyHash, ClientDeltaTracker>();
                        _receivingTrackers[player] = clientDict;
                    }
                    _cachedReadPlayer = player;
                    _cachedReadDict = clientDict;
                }
            }

            var key = new KeyHash(typeof(T), keyHash);
            if (!clientDict.TryGetValue(key, out var tracker))
            {
                var result = new ClientDeltaTracker<T>();
                tracker = result;
                clientDict[key] = tracker;
                return result;
            }

            if (tracker is not ClientDeltaTracker<T> typedTracker)
                throw new Exception($"Tracker for key {key} is not of type {typeof(ClientDeltaTracker<T>).Name}");

            return typedTracker;
        }

        public bool Write<Key, T>(BitPacker packer, PlayerID player, Key key, T newValue) where Key : struct, IStableHashable
        {
            PackedUInt cache = default;
            return Write(packer, player, key, newValue, ref cache);
        }

        public bool WriteReliableWithModifier<Key, T>(BitPacker packer, PlayerID player, Key key, T newValue, ValueModifier<T> modifier) where Key : struct, IStableHashable
        {
            var hash = GetKeyHash(key);
            var tracker = GetOrCreateTracker<T>(player, hash, true);

            T oldValue = default;

            int id = tracker.GetLastMatch();

            if (id >= 0)
            {
                if (tracker.TryGetValueAtIndex(id, out var confirmedValue))
                {
                    oldValue = Packer.Copy(confirmedValue);
                }
                else
                {
                    PurrLogger.LogError($"Confirmed value not found for key {hash} and {id} and player {player}");
                    oldValue = default;
                }
            }

            var pos = packer.positionInBits;
            packer.WriteBit(false);
            modifier(ref oldValue);
            bool changed = DeltaPacker<T>.Write(packer, oldValue, newValue);

            packer.WriteAt(pos, changed);

            if (changed)
            {
                tracker.Set(newValue);
                if (oldValue is IDisposable disposable)
                    disposable.Dispose();
            }
            else
            {
                tracker.SetWithoutCopy(oldValue);
                packer.SetBitPosition(pos + 1);
            }

            return changed;
        }

        public bool WriteReliable<Key, T>(BitPacker packer, PlayerID player, Key key, T newValue) where Key : struct, IStableHashable
        {
            var hash = GetKeyHash(key);
            var tracker = GetOrCreateTracker<T>(player, hash, true);

            T oldValue = default;

            int id = tracker.GetLastMatch();

            if (id >= 0)
            {
                if (tracker.TryGetValueAtIndex(id, out var confirmedValue))
                    oldValue = confirmedValue;
                else
                {
                    PurrLogger.LogError($"Confirmed value not found for key {hash} and {id} and player {player}");
                    oldValue = default;
                }
            }

            var pos = packer.positionInBits;
            packer.WriteBit(false);
            bool changed = DeltaPacker<T>.Write(packer, oldValue, newValue);

            packer.WriteAt(pos, changed);

            if (changed)
            {
                tracker.Set(newValue);
            }
            else
            {
                packer.SetBitPosition(pos + 1);
            }

            return changed;
        }

        public bool Write<Key, T>(BitPacker packer, PlayerID player, Key key, T newValue, ref PackedUInt cachedKey) where Key : struct, IStableHashable
        {
            var hash = GetKeyHash(key);
            return Write<T>(packer, player, hash, newValue, ref cachedKey);
        }

        public bool Write<T>(BitPacker packer, PlayerID player, uint precomputedHash, T newValue)
        {
            PackedUInt cache = default;
            return Write<T>(packer, player, precomputedHash, newValue, ref cache);
        }

        public bool Write<T>(BitPacker packer, PlayerID player, uint precomputedHash, T newValue, ref PackedUInt cachedKey)
        {
            var tracker = GetOrCreateTracker<T>(player, precomputedHash, true);

            T oldValue = default;

            int id = tracker.FindBestMatch(out var bestKey);

            if (id >= 0)
            {
                if (tracker.TryGetValueAtIndex(id, out var confirmedValue))
                    oldValue = confirmedValue;
                else
                {
                    PurrLogger.LogError($"Confirmed value not found for key {precomputedHash} and {id} and player {player}");
                    oldValue = default;
                }
            }

            DeltaPacker<PackedUInt>.Write(packer, cachedKey, bestKey);
            cachedKey = bestKey;

            var pos = packer.positionInBits;
            packer.WriteBit(false);
            bool changed = DeltaPacker<T>.Write(packer, oldValue, newValue);

            packer.WriteAt(pos, changed);

            if (changed)
            {
                PackedUInt newId = tracker.GenerateId();
                DeltaPacker<PackedUInt>.Write(packer, cachedKey, newId);
                cachedKey = newId;
                tracker.Set(newId, newValue);
            }
            else
            {
                packer.SetBitPosition(pos + 1);
            }

            return changed;
        }

        public void Read<Key, T>(BitPacker packer, Key key, PlayerID sender, ref T newValue) where Key : struct, IStableHashable
        {
            PackedUInt cachedKey = default;
            Read(packer, key, sender, ref newValue, ref cachedKey);
        }

        public void ReadReliable<Key, T>(BitPacker packer, Key key, ref T newValue) where Key : struct, IStableHashable
        {
            var player = _players.localPlayerId ?? default;

            var keyHash = GetKeyHash(key);
            var tracker = GetOrCreateTracker<T>(player, keyHash, false);

            bool changed = false;

            Packer<bool>.Read(packer, ref changed);

            if (changed)
            {
                DeltaPacker<T>.Read(packer, tracker.GetLastValue(), ref newValue);
                tracker.Set(newValue);
            }
            else
            {
                newValue = Packer.Copy(tracker.GetLastValue());
            }
        }

        public void ReadReliableWithModifier<Key, T>(BitPacker packer, Key key, ref T newValue, ValueModifier<T> modifier) where Key : struct, IStableHashable
        {
            var player = _players.localPlayerId ?? default;

            var keyHash = GetKeyHash(key);
            var tracker = GetOrCreateTracker<T>(player, keyHash, false);

            bool changed = false;

            Packer<bool>.Read(packer, ref changed);

            if (changed)
            {
                var oldValue = Packer.Copy(tracker.GetLastValue());

                modifier(ref oldValue);
                DeltaPacker<T>.Read(packer, oldValue, ref newValue);
                tracker.Set(newValue);

                if (oldValue is IDisposable disposable)
                    disposable.Dispose();
            }
            else
            {
                var oldValue = Packer.Copy(tracker.GetLastValue());
                modifier(ref oldValue);
                newValue = oldValue;
                tracker.Set(oldValue);
            }
        }

        public void Read<Key, T>(BitPacker packer, Key key, PlayerID sender, ref T newValue, ref PackedUInt cachedKey) where Key : struct, IStableHashable
        {
            var keyHash = GetKeyHash(key);
            Read<T>(packer, keyHash, sender, ref newValue, ref cachedKey);
        }

        public void Read<T>(BitPacker packer, uint precomputedHash, PlayerID sender, ref T newValue)
        {
            PackedUInt cachedKey = default;
            Read<T>(packer, precomputedHash, sender, ref newValue, ref cachedKey);
        }

        public void Read<T>(BitPacker packer, uint precomputedHash, PlayerID sender, ref T newValue, ref PackedUInt cachedKey)
        {
            var player = _players.localPlayerId ?? default;
            var tracker = GetOrCreateTracker<T>(player, precomputedHash, false);

            PackedUInt lastConfirmedId = default;
            DeltaPacker<PackedUInt>.Read(packer, cachedKey, ref lastConfirmedId);
            cachedKey = lastConfirmedId;

            bool changed = false;

            Packer<bool>.Read(packer, ref changed);

            if (changed)
            {
                PackedUInt valueId = default;
                T oldValue = default;

                if (lastConfirmedId != 0)
                {
                    if (tracker.TryGetValue(lastConfirmedId, out var confirmedValue))
                        oldValue = confirmedValue;
                    else PurrLogger.LogError($"Confirmed value not found for key {precomputedHash} and {lastConfirmedId.value} and player {player}");
                }

                DeltaPacker<T>.Read(packer, oldValue, ref newValue);
                DeltaPacker<PackedUInt>.Read(packer, cachedKey, ref valueId);
                cachedKey = valueId;

                tracker.Set(valueId, newValue);

                var data = new DeltaAcknowledge
                {
                    keyType = Hasher.GetStableHashU32<T>(),
                    keyHash = precomputedHash,
                    valueId = valueId
                };

                Batch(sender, data);
            }
            else if (lastConfirmedId != 0)
            {
                if (tracker.TryGetValue(lastConfirmedId, out var confirmedValue))
                    newValue = Packer.Copy(confirmedValue);
                else
                {
                    PurrLogger.LogError($"Confirmed value not found for key {precomputedHash} and {lastConfirmedId.value} and player {player}");
                    newValue = default;
                }
            }
            else newValue = default;
        }

        private void Batch(PlayerID sender, DeltaAcknowledge acknowledge)
        {
            int c = _acknowledgements.Count;
            for (int i = 0; i < c; i++)
            {
                var entry = _acknowledgements[i];
                if (entry.playerId != sender)
                     continue;

                // add sorted, one entry per key: keep only the newest valueId
                for (int j = 0; j < entry.entries.Count; j++)
                {
                    var existing = entry.entries[j];

                    if (existing.keyType.value == acknowledge.keyType.value &&
                        existing.keyHash.value == acknowledge.keyHash.value)
                    {
                        if (acknowledge.valueId.value > existing.valueId.value)
                            entry.entries[j] = acknowledge;
                        return;
                    }

                    if (existing.keyType.value > acknowledge.keyType.value ||
                        (existing.keyType.value == acknowledge.keyType.value && existing.keyHash.value > acknowledge.keyHash.value))
                    {
                        entry.entries.Insert(j, acknowledge);
                        return;
                    }
                }

                entry.entries.Add(acknowledge);
                return;
            }

            var entries = DisposableList<DeltaAcknowledge>.Create(16);
            entries.Add(acknowledge);
            _acknowledgements.Add(new DeltaAcknowledgeBatch
            {
                playerId = sender,
                entries = entries
            });
        }

        public static uint GetKeyHash<T>(T key) where T : struct, IStableHashable
        {
            uint typeHash = Hasher<T>.stableHash;
            uint valueHash = key.GetStableHash();
            return Hasher.CombineHashes(typeHash, valueHash);
        }

        private void Acknowledge(PlayerID player, DeltaAcknowledge data, bool asServer)
        {
            if (!Hasher.TryGetType(data.keyType.value, out var type))
                return;

            const float MAX_HISTORY_TIME_ALIVE = 0.5f;

            if (!asServer)
                player = default;

            var keyHash = new KeyHash(type, data.keyHash.value);
            var tracker = GetTracker(player, keyHash, true);

            if (tracker == null)
                return;

            tracker.ValidateId(data.valueId);
            var removeUpTo = tracker.CleanupUpTo(MAX_HISTORY_TIME_ALIVE);

            if (removeUpTo > 0)
            {
                var cleanupPacket = new DeltaCleanup
                {
                    keyType = data.keyType,
                    keyHash = data.keyHash,
                    upToId = removeUpTo
                };

                if (_asServer)
                    _broadcaster.Send(player, cleanupPacket, Channel.Unreliable);
                else _broadcaster.SendToServer(cleanupPacket, Channel.Unreliable);
            }
        }

        private void Cleanup(PlayerID sender, DeltaCleanup data, bool asserver)
        {
            var player = _players.localPlayerId ?? default;

            if (!Hasher.TryGetType(data.keyType.value, out var type))
                return;

            var key = new KeyHash(type, data.keyHash.value);
            if (!_receivingTrackers.TryGetValue(player, out var clientDict) ||
                !clientDict.TryGetValue(key, out var tracker))
                return;

            tracker.CleanupUpTo(data.upToId);
        }

        public void PostFixedUpdate()
        {
            SendAllAcks();
        }

        private void SendAllAcks()
        {
            for (int i = 0; i < _acknowledgements.Count; i++)
            {
                var batch = _acknowledgements[i];
                using var packer = BitPackerPool.Get();

                PackedUInt prevType = default;
                PackedUInt prevHash = default;
                PackedUInt prevVal = default;

                var count = batch.entries.Count;

                for (var e = 0; e < count; e++)
                {
                    var entry = batch.entries[e];
                    var mtu = _players.GetMTU(batch.playerId, Channel.Unreliable, _asServer)
                              - BroadcastModule.MAX_HEADER_SIZE;

                    DeltaPacker<PackedUInt>.Write(packer, prevType, entry.keyType);
                    DeltaPacker<PackedUInt>.Write(packer, prevHash, entry.keyHash);
                    DeltaPacker<PackedUInt>.Write(packer, prevVal, entry.valueId);

                    prevType = entry.keyType;
                    prevHash = entry.keyHash;
                    prevVal = entry.valueId;

                    if (packer.positionInBytes + 20 >= mtu)
                    {
                        var batchData = new DeltaBatch
                        {
                            data = packer,
                            bitCount = packer.positionInBits
                        };

                        if (_asServer)
                            _broadcaster.Send(batch.playerId, batchData, Channel.Unreliable);
                        else _broadcaster.SendToServer(batchData, Channel.Unreliable);

                        packer.ResetPositionAndMode(false);
                        prevHash = default;
                        prevVal = default;
                    }
                }

                if (packer.positionInBytes > 0)
                {
                    var batchData = new DeltaBatch
                    {
                        data = packer,
                        bitCount = packer.positionInBits
                    };

                    if (_asServer)
                        _broadcaster.Send(batch.playerId, batchData, Channel.Unreliable);
                    else _broadcaster.SendToServer(batchData, Channel.Unreliable);
                }

                batch.Dispose();
            }

            _acknowledgements.Clear();
        }

        private void AcknowledgeBatch(PlayerID player, DeltaBatch data, bool asserver)
        {
            using (data.data)
            {
                PackedUInt prevType = default;
                PackedUInt prevHash = default;
                PackedUInt prevVal = default;

                while (data.data.positionInBits < data.bitCount)
                {
                    DeltaPacker<PackedUInt>.Read(data.data, prevType, ref prevType);
                    DeltaPacker<PackedUInt>.Read(data.data, prevHash, ref prevHash);
                    DeltaPacker<PackedUInt>.Read(data.data, prevVal, ref prevVal);

                    var acknowledge = new DeltaAcknowledge
                    {
                        keyType = prevType,
                        keyHash = prevHash,
                        valueId = prevVal
                    };

                    Acknowledge(player, acknowledge, asserver);
                }
            }
        }
    }
}
