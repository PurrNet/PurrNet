using System;
using System.Collections.Generic;
using K4os.Compression.LZ4;
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
        private readonly Dictionary<PlayerID, Dictionary<uint, ClientDeltaTracker>> _receivingTrackers;
        private readonly Dictionary<PlayerID, Dictionary<uint, ClientDeltaTracker>> _sendingTrackers;

        private readonly List<DeltaAcknowledgeBatch> _acknowledgements = new ();

        private bool _asServer;

        public DeltaModule(PlayersManager players, PlayersBroadcaster broadcaster)
        {
            _players = players;
            _broadcaster = broadcaster;
            _receivingTrackers = new Dictionary<PlayerID, Dictionary<uint, ClientDeltaTracker>>();
            _sendingTrackers = new Dictionary<PlayerID, Dictionary<uint, ClientDeltaTracker>>();
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
            foreach (var player in _sendingTrackers.Keys)
            {
                if (_sendingTrackers.TryGetValue(player, out var clientDict))
                {
                    foreach (var tracker in clientDict.Values)
                        tracker.Dispose();
                }
            }

            foreach (var player in _receivingTrackers.Keys)
            {
                if (_receivingTrackers.TryGetValue(player, out var receiveDict))
                {
                    foreach (var tracker in receiveDict.Values)
                        tracker.Dispose();
                }
            }

            _sendingTrackers.Clear();
            _receivingTrackers.Clear();
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (_receivingTrackers.Remove(player, out var receiveDict))
            {
                foreach (var tracker in receiveDict.Values)
                    tracker.Dispose();
            }

            if (_sendingTrackers.Remove(player, out var clientDict))
            {
                foreach (var tracker in clientDict.Values)
                    tracker.Dispose();
            }
        }

        private ClientDeltaTracker GetTracker(PlayerID player, uint key, bool isWriting)
        {
            var dictionary = isWriting ? _sendingTrackers : _receivingTrackers;
            if (!dictionary.TryGetValue(player, out var clientDict))
            {
                clientDict = new Dictionary<uint, ClientDeltaTracker>();
                dictionary[player] = clientDict;
            }
            return clientDict.GetValueOrDefault(key);
        }

        private ClientDeltaTracker<T> GetOrCreateTracker<T>(PlayerID player, uint key, bool isWriting)
        {
            var dictionary = isWriting ? _sendingTrackers : _receivingTrackers;
            if (!dictionary.TryGetValue(player, out var clientDict))
            {
                clientDict = new Dictionary<uint, ClientDeltaTracker>();
                dictionary[player] = clientDict;
            }

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
            Packer<bool>.Write(packer, false);
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
            Packer<bool>.Write(packer, false);
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
            var tracker = GetOrCreateTracker<T>(player, hash, true);

            T oldValue = default;

            int id = tracker.FindBestMatch(out var bestKey);

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

            DeltaPacker<PackedUInt>.Write(packer, cachedKey, bestKey);
            cachedKey = bestKey;

            var pos = packer.positionInBits;
            Packer<bool>.Write(packer, false);
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
            var player = _players.localPlayerId ?? default;

            var keyHash = GetKeyHash(key);
            var tracker = GetOrCreateTracker<T>(player, keyHash, false);

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
                    else PurrLogger.LogError($"Confirmed value not found for key {keyHash} and {lastConfirmedId.value} and player {player}");
                }

                DeltaPacker<T>.Read(packer, oldValue, ref newValue);
                DeltaPacker<PackedUInt>.Read(packer, cachedKey, ref valueId);
                cachedKey = valueId;

                tracker.Set(valueId, newValue);

                var data = new DeltaAcknowledge
                {
                    key = keyHash,
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
                    PurrLogger.LogError($"Confirmed value not found for key {keyHash} and {lastConfirmedId.value} and player {player}");
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

                // add sorted
                for (int j = 0; j < entry.entries.Count; j++)
                {
                    if (entry.entries[j].key > acknowledge.key)
                    {
                        entry.entries.Insert(j, acknowledge);
                        return;
                    }

                    if (entry.entries[j].key == acknowledge.key && entry.entries[j].valueId >= acknowledge.valueId)
                    {
                        // already acknowledged
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

        private static uint GetKeyHash<T>(T key) where T : struct, IStableHashable
        {
            uint typeHash = Hasher<T>.stableHash;
            uint valueHash = key.GetStableHash();
            return Hasher.CombineHashes(typeHash, valueHash);
        }

        private void Acknowledge(PlayerID player, DeltaAcknowledge data, bool asServer)
        {
            const float MAX_HISTORY_TIME_ALIVE = 0.5f;

            if (!asServer)
                player = default;

            var tracker = GetTracker(player, data.key, true);

            if (tracker == null)
                return;

            tracker.ValidateId(data.valueId);
            var removeUpTo = tracker.CleanupUpTo(MAX_HISTORY_TIME_ALIVE);

            if (removeUpTo > 0)
            {
                var cleanupPacket = new DeltaCleanup
                {
                    key = data.key,
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

            if (!_receivingTrackers.TryGetValue(player, out var clientDict) ||
                !clientDict.TryGetValue(data.key, out var tracker))
                return;

            tracker.CleanupUpTo(data.upToId);
        }

        const int MTU = 1024;

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

                PackedUInt prevKey = default;
                PackedUInt prevVal = default;

                var count = batch.entries.Count;

                for (var e = 0; e < count; e++)
                {
                    var entry = batch.entries[e];
                    DeltaPacker<PackedUInt>.Write(packer, prevKey, entry.key);
                    DeltaPacker<PackedUInt>.Write(packer, prevVal, entry.valueId);

                    prevKey = entry.key;
                    prevVal = entry.valueId;

                    if (packer.positionInBytes + 10 >= MTU)
                    {
                        using var pickled = packer.Pickle(LZ4Level.L12_MAX);
                        var batchData = new DeltaBatch
                        {
                            data = pickled,
                            ogBitCount = packer.positionInBits,
                            dataBitCount = pickled.positionInBits
                        };

                        if (_asServer)
                            _broadcaster.Send(batch.playerId, batchData, Channel.Unreliable);
                        else _broadcaster.SendToServer(batchData, Channel.Unreliable);

                        packer.ResetPositionAndMode(false);
                        prevKey = default;
                        prevVal = default;
                    }
                }

                if (packer.positionInBytes > 0)
                {
                    using var pickled = packer.Pickle(LZ4Level.L12_MAX);
                    var batchData = new DeltaBatch
                    {
                        data = pickled,
                        ogBitCount = packer.positionInBits,
                        dataBitCount = pickled.positionInBits
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
                using var packer = BitPackerPool.Get();
                data.data.SetBitPosition(data.dataBitCount);
                packer.UnpickleFrom(data.data);
                packer.ResetPositionAndMode(false);

                PackedUInt prevKey = default;
                PackedUInt prevVal = default;

                while (packer.positionInBits < data.ogBitCount)
                {
                    DeltaPacker<PackedUInt>.Read(packer, prevKey, ref prevKey);
                    DeltaPacker<PackedUInt>.Read(packer, prevVal, ref prevVal);

                    var acknowledge = new DeltaAcknowledge
                    {
                        key = prevKey,
                        valueId = prevVal
                    };

                    Acknowledge(player, acknowledge, asserver);
                }
            }
        }
    }
}
