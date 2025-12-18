using System;
using System.Collections.Generic;
using System.Reflection;
using K4os.Compression.LZ4;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Profiler;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet.Modules
{
    public class RPCModule : INetworkModule, IBatch, IFlushBatchedRPCs, IPromoteToServerModule
    {
        public delegate void RPCPreProcessDelegate(ref ByteData rpcData, RPCSignature signature, ref BitPacker packer);

        public delegate void RPCPostProcessDelegate(ByteData rpcData, RPCInfo info, ref BitPacker packer);

        public static event RPCPreProcessDelegate onPreProcessRpc;

        public static event RPCPostProcessDelegate onPostProcessRpc;

        readonly HierarchyFactory _hierarchyModule;
        readonly PlayersManager _playersManager;
        readonly ScenesModule _scenes;
        readonly GlobalOwnershipModule _ownership;
        readonly NetworkManager _manager;

        private readonly RPCBatch<NetworkIdentityRPCHeader> _normalRpcBatch;
        private readonly RPCBatch<NetworkModuleRPCHeader> _childRpcBatch;
        private readonly RPCBatch<StaticRPCHeader> _staticRpcBatch;

        public RPCModule(NetworkManager manager, PlayersManager playersManager, HierarchyFactory hierarchyModule,
            GlobalOwnershipModule ownerships, ScenesModule scenes)
        {
            _manager = manager;
            _playersManager = playersManager;
            _hierarchyModule = hierarchyModule;
            _scenes = scenes;
            _ownership = ownerships;

            _normalRpcBatch = new RPCBatch<NetworkIdentityRPCHeader>(_playersManager, ReceivedNormalBatchedRPC);
            _childRpcBatch = new RPCBatch<NetworkModuleRPCHeader>(_playersManager, ReceivedChildBatchedRPC);
            _staticRpcBatch = new RPCBatch<StaticRPCHeader>(_playersManager, ReiceStaticBatchedRPC);
        }

        public void PromoteToServerModule()
        {
            _normalRpcBatch.Clear();
            _childRpcBatch.Clear();
            _staticRpcBatch.Clear();
        }

        public void PostPromoteToServerModule() { }

        private void ReiceStaticBatchedRPC(PlayerID sender, StaticRPCHeader header, ByteData content, bool asServer)
        {
            ReceiveStaticRPC(sender, new StaticRPCPacket
            {
                header = header,
                data = content
            }, asServer);
        }

        private void ReceivedChildBatchedRPC(PlayerID sender, NetworkModuleRPCHeader header, ByteData content, bool asServer)
        {
            ReceiveChildRPC(sender, new ChildRPCPacket
            {
                header = header,
                data = content
            }, asServer);
        }

        private void ReceivedNormalBatchedRPC(PlayerID sender, NetworkIdentityRPCHeader header, ByteData content, bool asServer)
        {
            ReceiveRPC(sender, new RPCPacket
            {
                header = header,
                data = content
            }, asServer);
        }

        public void Enable(bool asServer)
        {
            _playersManager.Subscribe<RPCPacket>(ReceiveRPC);
            _playersManager.Subscribe<StaticRPCPacket>(ReceiveStaticRPC);
            _playersManager.Subscribe<ChildRPCPacket>(ReceiveChildRPC);

            _playersManager.onPlayerJoined += OnPlayerJoined;
            _scenes.onSceneUnloaded += OnSceneUnloaded;

            _hierarchyModule.onSentSpawnPacket += OnObserverAdded;
            _hierarchyModule.onIdentityRemoved += OnIdentityRemoved;
        }

        public void Disable(bool asServer)
        {
            _playersManager.Unsubscribe<RPCPacket>(ReceiveRPC);
            _playersManager.Unsubscribe<StaticRPCPacket>(ReceiveStaticRPC);
            _playersManager.Unsubscribe<ChildRPCPacket>(ReceiveChildRPC);

            _playersManager.onPlayerJoined -= OnPlayerJoined;
            _scenes.onSceneUnloaded -= OnSceneUnloaded;

            _hierarchyModule.onSentSpawnPacket -= OnObserverAdded;
            _hierarchyModule.onIdentityRemoved -= OnIdentityRemoved;
        }

        private void OnObserverAdded(PlayerID player, SceneID scene, NetworkID id)
        {
            if (!_hierarchyModule.TryGetIdentity(scene, id, out var identity))
                return;

            SendAnyInstanceRPCs(player, identity);
            SendAnyChildRPCs(player, identity);
        }

        // Clean up buffered RPCs when an identity is removed
        private void OnIdentityRemoved(NetworkIdentity identity)
        {
            for (int i = 0; i < _bufferedRpcsDatas.Count; i++)
            {
                var data = _bufferedRpcsDatas[i];

                if (data.rpcid.sceneId != identity.sceneId) continue;
                if (data.rpcid.networkId != identity.id) continue;

                FreeStream(data.stream);

                _bufferedRpcsKeys.Remove(data.rpcid);
                _bufferedRpcsDatas.RemoveAt(i--);
            }

            for (int i = 0; i < _bufferedChildRpcsDatas.Count; i++)
            {
                var data = _bufferedChildRpcsDatas[i];

                if (data.rpcid.sceneId != identity.sceneId) continue;
                if (data.rpcid.networkId != identity.id) continue;

                FreeStream(data.stream);

                _bufferedChildRpcsKeys.Remove(data.rpcid);
                _bufferedChildRpcsDatas.RemoveAt(i--);
            }
        }

        // Clean up buffered RPCs when a scene is unloaded
        private void OnSceneUnloaded(SceneID scene, bool asServer)
        {
            for (int i = 0; i < _bufferedRpcsDatas.Count; i++)
            {
                var data = _bufferedRpcsDatas[i];

                if (data.rpcid.sceneId != scene) continue;

                var key = data.rpcid;
                FreeStream(data.stream);

                _bufferedRpcsKeys.Remove(key);
                _bufferedRpcsDatas.RemoveAt(i--);
            }

            for (int i = 0; i < _bufferedChildRpcsDatas.Count; i++)
            {
                var data = _bufferedChildRpcsDatas[i];

                if (data.rpcid.sceneId != scene) continue;

                var key = data.rpcid;
                FreeStream(data.stream);

                _bufferedChildRpcsKeys.Remove(key);
                _bufferedChildRpcsDatas.RemoveAt(i--);
            }
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            SendAnyStaticRPCs(player);
        }

        [UsedByIL]
        public static PlayerID GetLocalPlayer()
        {
            var nm = NetworkManager.main;

            if (!nm) return default;

            if (!nm.TryGetModule<PlayersManager>(false, out var players))
                return default;

            return players.localPlayerId ?? default;
        }

        public static PlayerID GetLocalPlayer(NetworkManager nm)
        {
            if (!nm) return default;

            if (!nm.TryGetModule<PlayersManager>(false, out var players))
                return default;

            return players.localPlayerId ?? default;
        }

        [UsedByIL]
        public static bool ArePlayersEqual(PlayerID player1, PlayerID player2)
        {
            return player1.Equals(player2);
        }

        [UsedByIL]
        public static void SendStaticRPC(StaticRPCPacket packet, RPCSignature signature)
        {
            var nm = NetworkManager.main;

            if (!nm)
            {
                PurrLogger.LogError($"Can't send static RPC '{signature.rpcName}'. NetworkManager not found.");
                return;
            }

            if (!nm.TryGetModule<RPCModule>(nm.isServer, out var module))
            {
                PurrLogger.LogError("Failed to get RPC module while sending static RPC.", nm);
                return;
            }

            var rules = nm.networkRules;
            bool shouldIgnore = rules && rules.ShouldIgnoreRequireServer();

            if (!shouldIgnore && signature.requireServer && !nm.isServer)
            {
                PurrLogger.LogError(
                    $"Trying to send static RPC '{signature.rpcName}' of type {signature.type} without server.");
                return;
            }

            module.AppendToBufferedRPCs(packet, signature);

            switch (signature.type)
            {
                case RPCType.ServerRPC:
                {
                    if (nm.isServerOnly)
                        break;

                    if (signature.runLocally && nm.isServer)
                        break;

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                    if (Hasher.TryGetType(packet.header.typeHash, out var type))
                        Statistics.SentRPC(type, signature.type, signature.rpcName, packet.data.segment, null);
#endif
                    module.BatchToServer(packet, signature.channel);
                    break;
                }
                case RPCType.ObserversRPC:
                {
                    if (nm.isServer)
                    {
                        using var players = GetObservers(signature);

                        if (players.Count == 0)
                            break;

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        for (var i = players.Count - 1; i >= 0; --i)
                        {
                            if (Hasher.TryGetType(packet.header.typeHash, out var type))
                                Statistics.SentRPC(type, signature.type, signature.rpcName, packet.data.segment, null);
                        }
#endif

                        module.BatchToTargets(players, packet, signature.channel);
                    }
                    else
                    {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        if (Hasher.TryGetType(packet.header.typeHash, out var type))
                            Statistics.SentRPC(type, signature.type, signature.rpcName, packet.data.segment, null);
#endif
                        module.BatchToServer(packet, signature.channel);
                    }
                    break;
                }
                case RPCType.TargetRPC:
                {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                    if (Hasher.TryGetType(packet.header.typeHash, out var type))
                        Statistics.SentRPC(type, signature.type, signature.rpcName, packet.data.segment, null);
#endif
                    if (nm.isServer)
                    {
                        using var targets = signature.GetTargets();
                        module.BatchToTargets(targets, packet, signature.channel);
                    }
                    else
                    {
                        using var targets = signature.GetTargets();
                        for (int i = 0; i < targets.Count; i++)
                        {
                            packet.targetPlayerId = targets[i];
                            module.BatchToServer(packet, signature.channel);
                        }
                    }
                    break;
                }
                default: throw new ArgumentOutOfRangeException();
            }
        }

        [UsedByIL]
        public static bool ValidateReceivingStaticRPC(RPCInfo info, RPCSignature signature, IRpc data, bool asServer)
        {
            var networkManager = NetworkManager.main;

            if (!networkManager)
            {
                PurrLogger.LogError($"Aborted RPC '{signature.rpcName}'. NetworkManager not found.");
                return false;
            }

            var rules = networkManager.networkRules;

            if (!networkManager.TryGetModule<RPCModule>(networkManager.isServer, out var module))
                return false;

            if (signature.type == RPCType.ServerRPC)
            {
                if (!asServer)
                {
                    PurrLogger.LogError($"Trying to receive static server RPC '{signature.rpcName}' on client. Aborting RPC call.");
                    return false;
                }
                return true;
            }

            if (!asServer)
            {
                return true;
            }

            bool shouldIgnore = rules && rules.ShouldIgnoreRequireServer();

            if (!shouldIgnore && signature.requireServer)
            {
                PurrLogger.LogError(
                    $"Trying to receive static client RPC '{signature.rpcName}' on server. " +
                    "If you want automatic forwarding use 'requireServer: false'.");
                return false;
            }

            switch (signature.type)
            {
                case RPCType.ServerRPC: throw new InvalidOperationException("ServerRPC should be handled by server.");

                case RPCType.ObserversRPC:
                {
                    var playersManager = networkManager.GetModule<PlayersManager>(true);

                    for (var i = 0; i < networkManager.players.Count; ++i)
                    {
                        var observer = networkManager.players[i];

                        bool ignoreSender = observer == info.sender && (signature.excludeSender || signature.runLocally);

                        if (ignoreSender)
                            continue;

                        var rawData = BroadcastModule.GetImmediateData(data);
                        playersManager.Send(observer, rawData, signature.channel);
                    }

                    if (data is StaticRPCPacket staticRpc)
                        module.AppendToBufferedRPCs(staticRpc, signature);
                    return !networkManager.isClient;
                }
                case RPCType.TargetRPC:
                {
                    var rawData = BroadcastModule.GetImmediateData(data);
                    var playersManager = networkManager.GetModule<PlayersManager>(true);

                    bool isTargetingServer = data.targetPlayerId == PlayerID.Server;
                    bool shouldExecute = isTargetingServer && rules.CanTargetServerWithTargetRpc();

                    if (!isTargetingServer)
                        playersManager.Send(data.targetPlayerId, rawData, signature.channel);

                    if (data is StaticRPCPacket staticRpc)
                        module.AppendToBufferedRPCs(staticRpc, signature);
                    return shouldExecute;
                }
                default: throw new ArgumentOutOfRangeException(nameof(signature.type));
            }
        }

        static readonly Dictionary<StaticGenericKey, MethodInfo> _staticGenericHandlers =
            new Dictionary<StaticGenericKey, MethodInfo>();

        [UsedByIL]
        public static object CallStaticGeneric(RuntimeTypeHandle type, string methodName, GenericRPCHeader rpcHeader)
        {
            var targetType = Type.GetTypeFromHandle(type);
            var key = new StaticGenericKey(type.Value, methodName, rpcHeader.types);

            if (!_staticGenericHandlers.TryGetValue(key, out var gmethod))
            {
                var method = targetType.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                gmethod = method?.MakeGenericMethod(rpcHeader.types);

                _staticGenericHandlers[key] = gmethod;
            }

            if (gmethod == null)
            {
                PurrLogger.LogError($"Calling generic static RPC failed. Method '{methodName}' not found.");
                return null;
            }

            try
            {
                var res = gmethod.Invoke(null, rpcHeader.values);
                PreciseArrayPool<Type>.Return(rpcHeader.types);
                PreciseArrayPool<object>.Return(rpcHeader.values);
                return res;
            }
            catch (TargetInvocationException e)
            {
                var actualException = e.InnerException;

                if (actualException != null)
                {
                    PurrLogger.LogException(actualException);
                    throw BypassLoggingException.instance;
                }

                throw;
            }
        }

        private void SendAnyChildRPCs(PlayerID player, NetworkIdentity identity)
        {
            for (int i = 0; i < _bufferedChildRpcsDatas.Count; i++)
            {
                var data = _bufferedChildRpcsDatas[i];

                if (data.rpcid.sceneId != identity.sceneId)
                    continue;

                if (data.rpcid.networkId != identity.id)
                    continue;

                if (data.sig.excludeOwner && _ownership.TryGetOwner(identity, out var owner) && owner == player)
                    continue;

                switch (data.sig.type)
                {
                    case RPCType.ObserversRPC:
                    {
                        var packet = data.packet;
                        packet.data = data.stream.ToByteData();
                        _playersManager.Send(player, packet);

                        break;
                    }

                    case RPCType.TargetRPC:
                    {
                        if (data.sig.targetPlayer == player)
                        {
                            var packet = data.packet;
                            packet.data = data.stream.ToByteData();
                            _playersManager.Send(player, packet);
                        }

                        break;
                    }
                    case RPCType.ServerRPC:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void SendAnyInstanceRPCs(PlayerID player, NetworkIdentity identity)
        {
            for (int i = 0; i < _bufferedRpcsDatas.Count; i++)
            {
                var data = _bufferedRpcsDatas[i];

                if (data.rpcid.sceneId != identity.sceneId)
                    continue;

                if (data.rpcid.networkId != identity.id)
                    continue;

                if (data.sig.excludeOwner && _ownership.TryGetOwner(identity, out var owner) && owner == player)
                    continue;

                switch (data.sig.type)
                {
                    case RPCType.ObserversRPC:
                    {
                        var packet = data.packet;
                        packet.data = data.stream.ToByteData();
                        _playersManager.Send(player, packet);

                        break;
                    }

                    case RPCType.TargetRPC:
                    {
                        if (data.sig.targetPlayer == player)
                        {
                            var packet = data.packet;
                            packet.data = data.stream.ToByteData();
                            _playersManager.Send(player, packet);
                        }

                        break;
                    }
                }
            }
        }

        private void SendAnyStaticRPCs(PlayerID player)
        {
            for (int i = 0; i < _bufferedStaticRpcsDatas.Count; i++)
            {
                var data = _bufferedStaticRpcsDatas[i];

                switch (data.sig.type)
                {
                    case RPCType.ObserversRPC:
                    {
                        var packet = data.packet;
                        packet.data = data.stream.ToByteData();
                        _playersManager.Send(player, packet);

                        break;
                    }

                    case RPCType.TargetRPC:
                    {
                        if (data.sig.targetPlayer == player)
                        {
                            var packet = data.packet;
                            packet.data = data.stream.ToByteData();
                            _playersManager.Send(player, packet);
                        }

                        break;
                    }
                }
            }
        }

        [UsedByIL]
        public static BitPacker AllocStream(bool reading)
        {
            return BitPackerPool.Get(reading);
        }

        [UsedByIL]
        public static void FreeStream(BitPacker stream)
        {
            stream.Dispose();
        }

        readonly Dictionary<RPC_ID, RPC_DATA> _bufferedRpcsKeys = new Dictionary<RPC_ID, RPC_DATA>();

        readonly Dictionary<RPC_ID, STATIC_RPC_DATA>
            _bufferedStaticRpcsKeys = new Dictionary<RPC_ID, STATIC_RPC_DATA>();

        readonly Dictionary<RPC_ID, CHILD_RPC_DATA> _bufferedChildRpcsKeys = new Dictionary<RPC_ID, CHILD_RPC_DATA>();

        readonly List<RPC_DATA> _bufferedRpcsDatas = new List<RPC_DATA>();
        readonly List<STATIC_RPC_DATA> _bufferedStaticRpcsDatas = new List<STATIC_RPC_DATA>();
        readonly List<CHILD_RPC_DATA> _bufferedChildRpcsDatas = new List<CHILD_RPC_DATA>();

        private void AppendToBufferedRPCs(StaticRPCPacket packet, RPCSignature signature)
        {
            if (!signature.bufferLast) return;

            var rpcid = new RPC_ID(packet);

            if (_bufferedStaticRpcsKeys.TryGetValue(rpcid, out var data))
            {
                data.stream.ResetPosition();
                data.stream.WriteBytes(packet.data);
            }
            else
            {
                var newStream = AllocStream(false);
                newStream.WriteBytes(packet.data);

                var newdata = new STATIC_RPC_DATA
                {
                    rpcid = rpcid,
                    packet = packet,
                    sig = signature,
                    stream = newStream
                };

                _bufferedStaticRpcsKeys.Add(rpcid, newdata);
                _bufferedStaticRpcsDatas.Add(newdata);
            }
        }

        public void AppendToBufferedRPCs(ChildRPCPacket packet, RPCSignature signature)
        {
            if (!signature.bufferLast) return;

            var rpcid = new RPC_ID(packet);

            if (_bufferedChildRpcsKeys.TryGetValue(rpcid, out var data))
            {
                data.stream.ResetPosition();
                data.stream.WriteBytes(packet.data);
            }
            else
            {
                var newStream = AllocStream(false);
                newStream.WriteBytes(packet.data);

                var newdata = new CHILD_RPC_DATA
                {
                    rpcid = rpcid,
                    packet = packet,
                    sig = signature,
                    stream = newStream
                };

                _bufferedChildRpcsKeys.Add(rpcid, newdata);
                _bufferedChildRpcsDatas.Add(newdata);
            }
        }

        public void AppendToBufferedRPCs(RPCPacket packet, RPCSignature signature)
        {
            if (!signature.bufferLast) return;

            var rpcid = new RPC_ID(packet);

            if (_bufferedRpcsKeys.TryGetValue(rpcid, out var data))
            {
                data.stream.ResetPosition();
                data.stream.WriteBytes(packet.data);
            }
            else
            {
                var newStream = AllocStream(false);
                newStream.WriteBytes(packet.data);

                var newdata = new RPC_DATA
                {
                    rpcid = rpcid,
                    packet = packet,
                    sig = signature,
                    stream = newStream
                };

                _bufferedRpcsKeys.Add(rpcid, newdata);
                _bufferedRpcsDatas.Add(newdata);
            }
        }

        [UsedByIL]
        public static RPCPacket BuildRawRPC(NetworkID? networkId, SceneID id, int rpcId, BitPacker data)
        {
            var rpc = new RPCPacket
            {
                header = new NetworkIdentityRPCHeader
                {
                    networkId = networkId ?? default,
                    rpcId = rpcId,
                    sceneId = id,
                    senderId = GetLocalPlayer()
                },
                data = data.ToByteData(),
            };

            return rpc;
        }

        [UsedByIL]
        public static StaticRPCPacket BuildStaticRawRPC<T>(uint rpcId, BitPacker data)
        {
            var hash = Hasher.GetStableHashU32<T>();

            var rpc = new StaticRPCPacket
            {
                header = new StaticRPCHeader {
                    rpcId = rpcId,
                    typeHash = hash,
                    senderId = GetLocalPlayer()
                },
                data = data.ToByteData(),
            };

            return rpc;
        }

        static readonly Dictionary<RPCKey, StaticRPCHandler> _rpcHandlers = new ();

        delegate void StaticRPCHandler(BitPacker stream, StaticRPCPacket packet, RPCInfo info, bool asServer);

        static StaticRPCHandler GetStaticRPCHandler(Type type, Size rpcId)
        {
            var rpcKey = new RPCKey(type, rpcId);

            if (_rpcHandlers.TryGetValue(rpcKey, out var handler))
                return handler;

            string methodName = $"HandleRPCGenerated_{rpcId}";
            var method = type.GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (method != null)
            {
                var d = Delegate.CreateDelegate(typeof(StaticRPCHandler), method);
                if (d is StaticRPCHandler staticDel)
                {
                    _rpcHandlers[rpcKey] = staticDel;
                    return staticDel;
                }
            }

            _rpcHandlers[rpcKey] = null;
            return null;
        }


        void ReceiveStaticRPC(PlayerID player, StaticRPCPacket data, bool asServer)
        {
            if (!Hasher.TryGetType(data.header.typeHash, out var type))
            {
                PurrLogger.LogError($"Failed to resolve type with hash {data.header.typeHash}.");
                return;
            }

            using var stream = BitPackerPool.Get(data.data);

            var rpcHandlerPtr = GetStaticRPCHandler(type, data.header.rpcId);
            var info = new RPCInfo
            {
                manager = _manager,
                sender = player,
                asServer = asServer
            };

            if (rpcHandlerPtr != null)
            {
                try
                {
                    rpcHandlerPtr(stream, data, info, asServer);
                }
                catch (BypassLoggingException)
                {
                    // ignore
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                }
            }
            else PurrLogger.LogError($"Can't find RPC handler for id {data.header.rpcId} on '{type.Name}'.");
        }

        void ReceiveChildRPC(PlayerID player, ChildRPCPacket packet, bool asServer)
        {
            using var stream = BitPackerPool.Get(packet.data);

            var info = new RPCInfo
            {
                manager = _manager,
                sender = player,
                asServer = asServer
            };

            if (_hierarchyModule.TryGetIdentity(packet.header.sceneId, packet.header.networkId, out var identity) && identity)
            {
                if (!identity.enabled && !identity.ShouldPlayRPCsWhenDisabled())
                    return;

                if (!identity.TryGetModule(packet.header.childId, out var networkClass))
                {
                    PurrLogger.LogError(
                        $"Can't find child with id {packet.header.childId} in identity {identity.GetType().Name}.", identity);
                }
                else
                {
                    try
                    {
                        networkClass.OnReceivedRpc(packet.header.rpcId, stream, packet, info, asServer);
                    }
                    catch (BypassLoggingException)
                    {
                        // ignore
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogException(e);
                    }
                }
            }
        }

        [UsedByIL]
        public static DisposableList<PlayerID> GetObservers(RPCSignature signature)
        {
            var nm = NetworkManager.main;

            if (!nm)
            {
                PurrLogger.LogError($"Can't send static RPC '{signature.rpcName}'. NetworkManager not found.");
                return DisposableList<PlayerID>.Create();
            }

            if (!nm.TryGetModule<RPCModule>(nm.isServer, out var module))
            {
                PurrLogger.LogError("Failed to get RPC module while sending static RPC.", nm);
                return DisposableList<PlayerID>.Create();
            }

            var all = module._playersManager.players;

            var players = DisposableList<PlayerID>.Create(all.Count);

            if (signature.targetPlayer != null)
            {
                players.Add(signature.targetPlayer.Value);
                return players;
            }

            for (var i = 0; i < all.Count; i++)
            {
                var player = all[i];
                bool isLocalPlayer = player == nm.localPlayer;

                if (signature.runLocally && isLocalPlayer)
                    continue;

                if (signature.excludeSender && isLocalPlayer)
                    continue;

                players.Add(player);
            }
            return players;
        }

        [UsedByIL]
        public static void ModifyManyToOne(ref RPCSignature signature, PlayerID target)
        {
            if (signature.type != RPCType.TargetRPC)
            {
                signature.targetPlayer = target;
            }
            else
            {
                signature.targetPlayer = target;
                signature.targetPlayerEnumerable = null;
                signature.targetPlayerList = null;
            }
        }

        void ReceiveRPC(PlayerID player, RPCPacket packet, bool asServer)
        {
            using var stream = BitPackerPool.Get(packet.data);

            var info = new RPCInfo
            {
                manager = _manager,
                sender = packet.header.senderId,
                asServer = asServer
            };

            if (_hierarchyModule.TryGetIdentity(packet.header.sceneId, packet.header.networkId, out var identity) && identity)
            {
                if (!identity.enabled && !identity.ShouldPlayRPCsWhenDisabled())
                {
                    return;
                }

                try
                {
                    identity.OnReceivedRpc((int)packet.header.rpcId.value, stream, packet, info, asServer);
                }
                catch (BypassLoggingException)
                {
                    // ignore
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                }
            }
        }

        [UsedByIL]
        public static void PreProcessRpc(ref ByteData rpcData, RPCSignature signature, ref BitPacker packer)
        {
            rpcData = packer.ToByteData();
            onPreProcessRpc?.Invoke(ref rpcData, signature, ref packer);

            if (signature.compressionLevel == CompressionLevel.None)
                return;

            var level = signature.compressionLevel switch
            {
                CompressionLevel.None => default,
                CompressionLevel.Fast => LZ4Level.L00_FAST,
                CompressionLevel.Balanced => LZ4Level.L06_HC,
                CompressionLevel.Best => LZ4Level.L12_MAX,
                _ => throw new ArgumentOutOfRangeException()
            };

            var newPacker = packer.Pickle(level);
            rpcData = newPacker.ToByteData();
            packer.Dispose();
            packer = newPacker;
        }

        [UsedByIL]
        public static void PostProcessRpc(ByteData rpcData, RPCInfo info, ref BitPacker packer)
        {
            onPostProcessRpc?.Invoke(rpcData, info, ref packer);

            if (info.compileTimeSignature.compressionLevel == CompressionLevel.None)
                return;

            var newPacker = BitPackerPool.Get();
            newPacker.UnpickleFrom(rpcData);
            newPacker.ResetPositionAndMode(true);

            packer.Dispose();
            packer = newPacker;
        }


        private void BatchToTargets(DisposableList<PlayerID> players, RPCPacket packet, Channel signatureChannel)
        {
            for (int i = 0; i < players.Count; i++)
                _normalRpcBatch.Queue(players[i], packet.header, packet.data, signatureChannel);
        }

        private void BatchToTargets(DisposableList<PlayerID> players, ChildRPCPacket packet, Channel signatureChannel)
        {
            for (int i = 0; i < players.Count; i++)
                _childRpcBatch.Queue(players[i], packet.header, packet.data, signatureChannel);
        }

        private void BatchToTargets(DisposableList<PlayerID> players, StaticRPCPacket packet, Channel signatureChannel)
        {
            for (int i = 0; i < players.Count; i++)
                _staticRpcBatch.Queue(players[i], packet.header, packet.data, signatureChannel);
        }

        enum RPCMethod
        {
            NetworkIdentity,
            NetworkModule,
            Static
        }

        private RPCMethod lastUsedMethod;

        private void FlushByMethod(RPCMethod method)
        {
            switch (method)
            {
                case RPCMethod.NetworkIdentity:
                    _normalRpcBatch.FlushChannel(Channel.ReliableOrdered);
                    break;
                case RPCMethod.NetworkModule:
                    _childRpcBatch.FlushChannel(Channel.ReliableOrdered);
                    break;
                case RPCMethod.Static:
                    _staticRpcBatch.FlushChannel(Channel.ReliableOrdered);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(method), method, null);
            }
        }

        private void FlushIfDifferent(RPCMethod method)
        {
            if (lastUsedMethod != method)
            {
                FlushByMethod(lastUsedMethod);
                lastUsedMethod = method;
            }
        }

        public void BatchToServer<T>(T packet, Channel signatureChannel) where T : IRpc
        {
            switch (packet)
            {
                case RPCPacket normalRpc:
                    FlushIfDifferent(RPCMethod.NetworkIdentity);
                    _normalRpcBatch.Queue(PlayerID.Server, normalRpc.header, normalRpc.data, signatureChannel);
                    break;
                case ChildRPCPacket childRpc:
                    FlushIfDifferent(RPCMethod.NetworkModule);
                    _childRpcBatch.Queue(PlayerID.Server, childRpc.header, childRpc.data, signatureChannel);
                    break;
                case StaticRPCPacket staticRpc:
                    FlushIfDifferent(RPCMethod.Static);
                    _staticRpcBatch.Queue(PlayerID.Server, staticRpc.header, staticRpc.data, signatureChannel);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void BatchToTargets<T>(DisposableList<PlayerID> players, T packet, Channel signatureChannel) where T : IRpc
        {
            switch (packet)
            {
                case RPCPacket normalRpc:
                    FlushIfDifferent(RPCMethod.NetworkIdentity);
                    BatchToTargets(players, normalRpc, signatureChannel);
                    break;
                case ChildRPCPacket childRpc:
                    FlushIfDifferent(RPCMethod.NetworkModule);
                    BatchToTargets(players, childRpc, signatureChannel);
                    break;
                case StaticRPCPacket staticRpc:
                    FlushIfDifferent(RPCMethod.Static);
                    BatchToTargets(players, staticRpc, signatureChannel);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void BatchNetworkMessages()
        {
            _staticRpcBatch.Flush();
            _normalRpcBatch.Flush();
            _childRpcBatch.Flush();
        }

        public void FlushBatchedRPCs()
        {
            BatchNetworkMessages();
        }
    }
}
