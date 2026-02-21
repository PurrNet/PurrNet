using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using K4os.Compression.LZ4;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Profiler;
using PurrNet.Transports;
using PurrNet.Utils;
using Unity.Profiling;

namespace PurrNet.Modules
{
    public class RPCModule : INetworkModule, IBatch, IFlushBatchedRPCs, IPromoteToServerModule
    {
        public delegate void RPCPreProcessDelegate(RPCSignature signature, ref BitPacker packer);

        public delegate void RPCPostProcessDelegate(RPCInfo info, ref BitData packer);

        public static event RPCPreProcessDelegate onPreProcessRpc;

        public static event RPCPostProcessDelegate onPostProcessRpc;

        readonly HierarchyFactory _hierarchyModule;
        readonly PlayersManager _playersManager;
        readonly ScenesModule _scenes;
        readonly GlobalOwnershipModule _ownership;
        readonly NetworkManager _manager;

        private RPCBatch _unionBatch;

        public RPCModule(NetworkManager manager, PlayersManager playersManager, HierarchyFactory hierarchyModule,
            GlobalOwnershipModule ownerships, ScenesModule scenes)
        {
            _manager = manager;
            _playersManager = playersManager;
            _hierarchyModule = hierarchyModule;
            _scenes = scenes;
            _ownership = ownerships;
        }

        private void ReceivedUnionBatchedRPC(PlayerID sender, UnionRPCHeader header, BitData content, bool asServer)
        {
            try
            {
                if (header.moduleRpc.HasValue)
                {
                    ReceiveChildRPC(sender, new ChildRPCPacket
                    {
                        header = header.ToModuleHeader(),
                        data = content
                    }, asServer);
                }
                else if (header.identityRpc.HasValue)
                {
                    ReceiveRPC(sender, new RPCPacket
                    {
                        header = header.ToIdentityHeader(),
                        data = content
                    }, asServer);
                }
                else
                {
                    ReceiveStaticRPC(sender, new StaticRPCPacket
                    {
                        header = header.ToStaticHeader(),
                        data = content
                    }, asServer);
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        public void PromoteToServerModule()
        {
            _unionBatch.Clear();
        }

        public void PostPromoteToServerModule() { }

        public void Enable(bool asServer)
        {
            _playersManager.Subscribe<RPCPacket>(ReceiveRPC);
            _playersManager.Subscribe<StaticRPCPacket>(ReceiveStaticRPC);
            _playersManager.Subscribe<ChildRPCPacket>(ReceiveChildRPC);

            _playersManager.onPlayerJoined += OnPlayerJoined;
            _scenes.onSceneUnloaded += OnSceneUnloaded;

            _hierarchyModule.onSentSpawnPacket += OnObserverAdded;
            _hierarchyModule.onIdentityRemoved += OnIdentityRemoved;

            _unionBatch = new RPCBatch(_playersManager, ReceivedUnionBatchedRPC);
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

            _unionBatch.Dispose();
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

                data.stream.Dispose();

                _bufferedRpcsKeys.Remove(data.rpcid);
                _bufferedRpcsDatas.RemoveAt(i--);
            }

            for (int i = 0; i < _bufferedChildRpcsDatas.Count; i++)
            {
                var data = _bufferedChildRpcsDatas[i];

                if (data.rpcid.sceneId != identity.sceneId) continue;
                if (data.rpcid.networkId != identity.id) continue;

                data.stream.Dispose();

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
                data.stream.Dispose();

                _bufferedRpcsKeys.Remove(key);
                _bufferedRpcsDatas.RemoveAt(i--);
            }

            for (int i = 0; i < _bufferedChildRpcsDatas.Count; i++)
            {
                var data = _bufferedChildRpcsDatas[i];

                if (data.rpcid.sceneId != scene) continue;

                var key = data.rpcid;
                data.stream.Dispose();

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
                        Statistics.SentRPC(type, signature.type, signature.rpcName, packet.data, null);
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
                                Statistics.SentRPC(type, signature.type, signature.rpcName, packet.data, null);
                        }
#endif

                        module.BatchToTargets(players, packet, signature.channel);
                    }
                    else
                    {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        if (Hasher.TryGetType(packet.header.typeHash, out var type))
                            Statistics.SentRPC(type, signature.type, signature.rpcName, packet.data, null);
#endif
                        module.BatchToServer(packet, signature.channel);
                    }
                    break;
                }
                case RPCType.TargetRPC:
                {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                    if (Hasher.TryGetType(packet.header.typeHash, out var type))
                        Statistics.SentRPC(type, signature.type, signature.rpcName, packet.data, null);
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
        public static bool ValidateReceivingStaticRPC<T>(RPCInfo info, RPCSignature signature, T data, bool asServer) where T : struct, IRpc
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
                    var finalList = DisposableList<PlayerID>.Create(playersManager.players.Count);

                    for (var i = 0; i < networkManager.players.Count; ++i)
                    {
                        var observer = networkManager.players[i];

                        bool ignoreSender = observer == info.sender && (signature.excludeSender || signature.runLocally);

                        if (ignoreSender)
                            continue;

                        finalList.Add(observer);
                    }

                    playersManager.Send(finalList, data, signature.channel);
                    finalList.Dispose();

                    if (data is StaticRPCPacket staticRpc)
                        module.AppendToBufferedRPCs(staticRpc, signature);
                    return !networkManager.isClient;
                }
                case RPCType.TargetRPC:
                {
                    var playersManager = networkManager.GetModule<PlayersManager>(true);

                    bool isTargetingServer = data.targetPlayerId == PlayerID.Server;
                    bool shouldExecute = isTargetingServer && rules.CanTargetServerWithTargetRpc();

                    if (!isTargetingServer)
                        playersManager.Send(data.targetPlayerId, data, signature.channel);

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
                        _unionBatch.Queue(player, new UnionRPCHeader(data.header), new BitData(data.stream), Channel.ReliableOrdered);
                        break;
                    }

                    case RPCType.TargetRPC:
                    {
                        if (data.sig.targetPlayer == player)
                            _unionBatch.Queue(player, new UnionRPCHeader(data.header), new BitData(data.stream), Channel.ReliableOrdered);
                        break;
                    }
                    case RPCType.ServerRPC:
                        break;
                    default:
                        PurrLogger.LogError($"Unexpected RPC type {data.sig.type} in SendAnyChildRPCs.");
                        break;
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
                        _unionBatch.Queue(player, new UnionRPCHeader(data.header), new BitData(data.stream), Channel.ReliableOrdered);
                        break;
                    }

                    case RPCType.TargetRPC:
                    {
                        if (data.sig.targetPlayer == player)
                            _unionBatch.Queue(player, new UnionRPCHeader(data.header), new BitData(data.stream), Channel.ReliableOrdered);
                        break;
                    }
                    case RPCType.ServerRPC:
                        break;
                    default:
                        PurrLogger.LogError($"Unexpected RPC type {data.sig.type} in SendAnyInstanceRPCs.");
                        break;
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
                        _unionBatch.Queue(player, new UnionRPCHeader(data.header), new BitData(data.stream), Channel.ReliableOrdered);
                        break;
                    }

                    case RPCType.TargetRPC:
                    {
                        if (data.sig.targetPlayer == player)
                            _unionBatch.Queue(player, new UnionRPCHeader(data.header), new BitData(data.stream), Channel.ReliableOrdered);
                        break;
                    }
                    case RPCType.ServerRPC:
                        break;
                    default:
                        PurrLogger.LogError($"Unexpected RPC type {data.sig.type} in SendAnyStaticRPCs.");
                        break;
                }
            }
        }

        [UsedByIL]
        public static BitPacker AllocStream(bool reading) => BitPackerPool.Get(reading);

        [UsedByIL]
        public static void FreeStream(BitPacker stream) => stream.Dispose();

        readonly Dictionary<RPC_ID, RPC_DATA_BASE<NetworkIdentityRPCHeader>> _bufferedRpcsKeys = new ();
        readonly Dictionary<RPC_ID, RPC_DATA_BASE<StaticRPCHeader>> _bufferedStaticRpcsKeys = new ();
        readonly Dictionary<RPC_ID, RPC_DATA_BASE<NetworkModuleRPCHeader>> _bufferedChildRpcsKeys = new ();

        readonly List<RPC_DATA_BASE<NetworkIdentityRPCHeader>> _bufferedRpcsDatas = new ();
        readonly List<RPC_DATA_BASE<StaticRPCHeader>> _bufferedStaticRpcsDatas = new ();
        readonly List<RPC_DATA_BASE<NetworkModuleRPCHeader>> _bufferedChildRpcsDatas = new ();

        static readonly ProfilerMarker _bufferRPCMarker = new ProfilerMarker($"RPCModule.AppendToBufferedRPCs");

        private void AppendToBufferedRPCs(StaticRPCPacket packet, RPCSignature signature)
        {
            using (_bufferRPCMarker.Auto())
            {
                AppendToBufferedRPCs(_bufferedStaticRpcsKeys,
                    _bufferedStaticRpcsDatas,
                    packet.header,
                    new RPC_ID(packet),
                    packet.data,
                    signature
                );
            }
        }

        public void AppendToBufferedRPCs(ChildRPCPacket packet, RPCSignature signature)
        {
            using (_bufferRPCMarker.Auto())
            {
                AppendToBufferedRPCs(_bufferedChildRpcsKeys,
                    _bufferedChildRpcsDatas,
                    packet.header,
                    new RPC_ID(packet),
                    packet.data,
                    signature
                );
            }
        }

        public void AppendToBufferedRPCs(RPCPacket packet, RPCSignature signature)
        {
            using (_bufferRPCMarker.Auto())
            {
                AppendToBufferedRPCs(
                    _bufferedRpcsKeys,
                    _bufferedRpcsDatas,
                    packet.header,
                    new RPC_ID(packet),
                    packet.data,
                    signature
                );
            }
        }

        private void AppendToBufferedRPCs<T>(
            Dictionary<RPC_ID, RPC_DATA_BASE<T>> keys,
            List<RPC_DATA_BASE<T>> datas,
            T header,
            RPC_ID rpcid,
            BitData bitData,
            in RPCSignature signature)
        {
            if (!signature.bufferLast) return;

            if (_bufferedRpcsKeys.TryGetValue(rpcid, out var data))
            {
                data.stream.ResetPosition();
                data.stream.WriteBitDataWithoutConsumingIt(bitData);
            }
            else
            {
                var newStream = AllocStream(false);
                newStream.WriteBitsWithoutConsumingIt(bitData.packer, bitData.bitLength);

                var newdata = new RPC_DATA_BASE<T>
                {
                    rpcid = rpcid,
                    header = header,
                    sig = signature,
                    stream = newStream
                };

                keys.Add(rpcid, newdata);
                datas.Add(newdata);
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
                data = new BitData(data)
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
                data = new BitData(data)
            };

            return rpc;
        }

        static readonly Dictionary<RPCKey, StaticRPCHandler> _rpcHandlers = new ();

        delegate void StaticRPCHandler(StaticRPCPacket packet, RPCInfo info, bool asServer);

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

            var rpcHandlerPtr = GetStaticRPCHandler(type, data.header.rpcId);
            var info = new RPCInfo
            {
                manager = _manager,
                sender = player,
                asServer = asServer
            };

            if (rpcHandlerPtr != null)
            {
                using (data.data.AutoScope())
                {
                    try
                    {
                        rpcHandlerPtr(data, info, asServer);
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
            else PurrLogger.LogError($"Can't find RPC handler for id {data.header.rpcId} on '{type.Name}'.");
        }

        void ReceiveChildRPC(PlayerID player, ChildRPCPacket packet, bool asServer)
        {
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
                    using (packet.data.AutoScope())
                    {
                        try
                        {
                            networkClass.OnReceivedRpc(packet.header.rpcId, packet, info, asServer);
                        }
                        catch (BypassLoggingException)
                        {
                            // ignore
                        }
                        catch (Exception e)
                        {
                            PurrLogger.LogException(e, networkClass.parent);
                        }
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

                using (packet.data.AutoScope())
                {
                    try
                    {
                        identity.OnReceivedRpc((int)packet.header.rpcId.value, packet, info, asServer);
                    }
                    catch (BypassLoggingException)
                    {
                        // ignore
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogException(e, identity);
                    }
                }
            }
        }

        [UsedByIL]
        public static void PreProcessRpc(RPCSignature signature, ref BitPacker packer, ref BitData rpcData)
        {
            bool hasCompression = signature.compressionLevel != CompressionLevel.None;
            bool hasCustomPostProcessor = onPreProcessRpc != null;

            if (hasCompression || hasCustomPostProcessor)
            {
                if (hasCompression)
                {
                    var level = signature.compressionLevel switch
                    {
                        CompressionLevel.None => default,
                        CompressionLevel.Fast => LZ4Level.L00_FAST,
                        CompressionLevel.Balanced => LZ4Level.L06_HC,
                        CompressionLevel.Best => LZ4Level.L12_MAX,
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    var newPacker = packer.Pickle(level);
                    packer.Dispose();
                    packer = newPacker;
                }

                if (hasCustomPostProcessor)
                    onPreProcessRpc?.Invoke(signature, ref packer);
            }

            rpcData = new BitData(packer);
        }

        [UsedByIL]
        public static void PostProcessRpc(RPCInfo info, ref BitData data)
        {
            bool hasCompression = info.compileTimeSignature.compressionLevel != CompressionLevel.None;
            bool hasCustomPostProcessor = onPostProcessRpc != null;

            if (hasCustomPostProcessor || hasCompression)
            {
                // We copy and leave the original data intact, so that the user can modify it freely.
                var dataCopy = BitPackerPool.Get();
                dataCopy.WriteBitDataWithoutConsumingIt(data);
                data = new BitData(dataCopy);

                if (hasCompression)
                {
                    var newPacker = BitPackerPool.Get();
                    newPacker.UnpickleFrom(dataCopy);
                    newPacker.ResetPositionAndMode(true);
                    data = new BitData(newPacker);
                }

                if (hasCustomPostProcessor)
                    onPostProcessRpc.Invoke(info, ref data);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BatchToServer(RPCPacket normalRpc, Channel signatureChannel)
        {
            _unionBatch.Queue(PlayerID.Server, new UnionRPCHeader(normalRpc.header), normalRpc.data, signatureChannel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BatchToServer(ChildRPCPacket childRpc, Channel signatureChannel)
        {
            _unionBatch.Queue(PlayerID.Server, new UnionRPCHeader(childRpc.header), childRpc.data, signatureChannel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BatchToServer(StaticRPCPacket staticRpc, Channel signatureChannel)
        {
            _unionBatch.Queue(PlayerID.Server, new UnionRPCHeader(staticRpc.header), staticRpc.data, signatureChannel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BatchToTargets(DisposableList<PlayerID> players, RPCPacket packet, Channel signatureChannel)
        {
            _unionBatch.Queue(players, new UnionRPCHeader(packet.header), packet.data, signatureChannel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BatchToTargets(DisposableList<PlayerID> players, ChildRPCPacket packet, Channel signatureChannel)
        {
            _unionBatch.Queue(players, new UnionRPCHeader(packet.header), packet.data, signatureChannel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BatchToTargets(DisposableList<PlayerID> players, StaticRPCPacket packet, Channel signatureChannel)
        {
            _unionBatch.Queue(players, new UnionRPCHeader(packet.header), packet.data, signatureChannel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BatchNetworkMessages()
        {
            _unionBatch.Flush();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FlushBatchedRPCs()
        {
            BatchNetworkMessages();
        }
    }
}
