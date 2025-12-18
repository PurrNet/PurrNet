using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
#if UNITASK_PURRNET_SUPPORT
using Cysharp.Threading.Tasks;
#endif
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Pooling;
using PurrNet.Profiler;
using PurrNet.Transports;
using Channel = PurrNet.Transports.Channel;

#if !UNITASK_PURRNET_SUPPORT
using RawTask = System.Threading.Tasks.Task;
#else
using RawTask = Cysharp.Threading.Tasks.UniTask;
#endif


namespace PurrNet
{
    public partial class NetworkIdentity
    {
        internal readonly struct InstanceGenericKey : IEquatable<InstanceGenericKey>
        {
            readonly string _methodName;
            readonly int _typesHash;
            readonly int _callerHash;

            public InstanceGenericKey(string methodName, Type caller, Type[] types)
            {
                _methodName = methodName;
                _typesHash = 0;

                _callerHash = caller.GetHashCode();

                for (int i = 0; i < types.Length; i++)
                    _typesHash ^= types[i].GetHashCode();
            }

            public override int GetHashCode() => _methodName.GetHashCode() ^ _typesHash ^ _callerHash;

            public bool Equals(InstanceGenericKey other)
            {
                return _methodName == other._methodName && _typesHash == other._typesHash &&
                       _callerHash == other._callerHash;
            }

            public override bool Equals(object obj)
            {
                return obj is InstanceGenericKey other && Equals(other);
            }
        }

        internal static readonly Dictionary<InstanceGenericKey, MethodInfo> genericMethods =
            new Dictionary<InstanceGenericKey, MethodInfo>();

        [UsedByIL]
        protected object CallGeneric(string methodName, GenericRPCHeader rpcHeader)
        {
            var key = new InstanceGenericKey(methodName, GetType(), rpcHeader.types);

            if (!genericMethods.TryGetValue(key, out var gmethod))
            {
                var method = GetType().GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                gmethod = method?.MakeGenericMethod(rpcHeader.types);

                genericMethods.Add(key, gmethod);
            }

            if (gmethod == null)
            {
                PurrLogger.LogError($"Calling generic RPC failed. Method '{methodName}' not found.");
                return null;
            }

            try
            {
                var res = gmethod.Invoke(this, rpcHeader.values);
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

        /// <summary>
        /// Used internally to get next RPC id.
        /// Do not use this method directly.
        /// </summary>
        [UsedByIL]
        public Task<T> GetNextId<T>(RPCType rpcType, PlayerID? target, float timeout, out RpcRequest request)
        {
            request = default;

            if (!networkManager)
            {
                return Task.FromException<T>(new InvalidOperationException(
                    "NetworkIdentity is not spawned."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule<RpcRequestResponseModule>(asServer, out var module))
            {
                return Task.FromException<T>(new InvalidOperationException(
                    "RpcRequestResponseModule module is missing."));
            }

            return module.GetNextId<T>(target, timeout, out request);
        }

        [UsedByIL]
        public RawTask GetNextIdUniTask(RPCType rpcType, PlayerID? target, float timeout, out RpcRequest request)
        {
            request = default;

            if (!networkManager)
            {
                return RawTask.FromException(new InvalidOperationException(
                    "NetworkIdentity is not spawned."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule<RpcRequestResponseModule>(asServer, out var module))
            {
                return RawTask.FromException(new InvalidOperationException(
                    "RpcRequestResponseModule module is missing."));
            }

            return module.GetNextIdUniTask(target, timeout, out request);
        }

        [UsedByIL]
#if !UNITASK_PURRNET_SUPPORT
        public Task<T>
#else
        public UniTask<T>
#endif
            GetNextIdUniTask<T>(RPCType rpcType, PlayerID? target, float timeout, out RpcRequest request)
        {
            request = default;

            if (!networkManager)
            {
                return RawTask.FromException<T>(new InvalidOperationException(
                    "NetworkIdentity is not spawned."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule<RpcRequestResponseModule>(asServer, out var module))
            {
                return RawTask.FromException<T>(new InvalidOperationException(
                    "RpcRequestResponseModule module is missing."));
            }

            return module.GetNextIdUniTask<T>(target, timeout, out request);
        }

        /// <summary>
        /// Used internally to get next RPC id.
        /// Do not use this method directly.
        /// </summary>
        [UsedByIL]
        public Task GetNextId(RPCType rpcType, PlayerID? target, float timeout, out RpcRequest request)
        {
            request = default;

            if (!networkManager)
            {
                return Task.FromException(new InvalidOperationException(
                    "NetworkIdentity is not spawned."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule<RpcRequestResponseModule>(asServer, out var module))
            {
                return Task.FromException(new InvalidOperationException(
                    "RpcRequestResponseModule module is missing."));
            }

            return module.GetNextId(target, timeout, out request);
        }

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
        private Type _myType;
#endif

        [UsedByIL]
        public DisposableList<PlayerID> GetObservers(RPCSignature signature)
        {
            var players = DisposableList<PlayerID>.Create(observers.Count);

            if (signature.targetPlayer != null)
            {
                players.Add(signature.targetPlayer.Value);
                return players;
            }

            for (var i = 0; i < observers.Count; i++)
            {
                var player = observers[i];
                bool isLocalPlayer = player == networkManager.localPlayer;

                if (signature.runLocally && isLocalPlayer)
                    continue;

                if (signature.excludeSender && isLocalPlayer)
                    continue;

                if (signature.excludeOwner && !IsNotOwnerPredicate(player))
                    continue;

                players.Add(player);
            }
            return players;
        }

        public void SendRPC<T>(Type statisticsParent, RPCModule rpcModule, T packet, RPCSignature signature) where T : IRpc
        {
            switch (signature.type)
            {
                case RPCType.ServerRPC:
                    if (networkManager.isServerOnly)
                        break;

                    if (signature.runLocally && isServer)
                        break;

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                    Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData.segment, this);
#endif
                    rpcModule.BatchToServer(packet, signature.channel);
                    break;
                case RPCType.ObserversRPC:
                {
                    if (isServer)
                    {
                        using var players = GetObservers(signature);

                        if (players.Count == 0)
                            break;

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        for (var i = players.Count - 1; i >= 0; --i)
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData.segment, this);
#endif
                        rpcModule.BatchToTargets(players, packet, signature.channel);
                    }
                    else
                    {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData.segment, this);
#endif
                        rpcModule.BatchToServer(packet, signature.channel);
                    }

                    break;
                }
                case RPCType.TargetRPC:
                    if (isServer)
                    {
                        using var players = signature.GetTargets();

                        if (players.Count == 0)
                            break;

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        for (var i = players.Count - 1; i >= 0; --i)
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData.segment, this);
#endif
                        rpcModule.BatchToTargets(players, packet, signature.channel);
                    }
                    else
                    {
                        using var targets = signature.GetTargets();

                        if (targets.Count == 0)
                            break;

                        // TODO: we should batch this into one packet to the server instead of N
                        for (int i = 0; i < targets.Count; i++)
                        {
                            packet.targetPlayerId = targets[i];
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData.segment, this);
#endif
                            rpcModule.BatchToServer(packet, signature.channel);
                        }
                    }

                    break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        [UsedByIL]
        protected void SendRPC(RPCPacket packet, RPCSignature signature)
        {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            _myType ??= GetType();
#endif
            if (!ValidateSendingRPC(signature, out var module))
                return;

            module.AppendToBufferedRPCs(packet, signature);

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            SendRPC(_myType, module, packet, signature);
#else
            SendRPC(null, module, packet, signature);
#endif
        }

        public bool ValidateSendingRPC(RPCSignature signature, out RPCModule module)
        {
            if (!isSpawned)
            {
                if (signature is { runLocally: false, channel: Channel.ReliableOrdered or Channel.ReliableUnordered })
                    PurrLogger.LogError($"Trying to send RPC `{signature.rpcName}` from '{GetType().Name}' which is not spawned.", this);
                module = null;
                return false;
            }

            if (!networkManager.TryGetModule<RPCModule>(networkManager.isServer, out module))
            {
                if (signature is { runLocally: false, channel: Channel.ReliableOrdered or Channel.ReliableUnordered })
                    PurrLogger.LogError($"Trying to send RPC `{signature.rpcName}` from `{GetType().Name}` but RPCModule is missing for `{(networkManager.isServer ? "server" : "client")}`.", this);
                return false;
            }

            var rules = networkManager.networkRules;
            bool shouldIgnoreOwnership = rules && rules.ShouldIgnoreRequireOwner();

            if (!shouldIgnoreOwnership && signature.requireOwnership && !isOwner)
            {
                if (signature is { runLocally: false, channel: Channel.ReliableOrdered or Channel.ReliableUnordered })
                    PurrLogger.LogError(
                        $"Trying to send RPC '{signature.rpcName}' from '{GetType().Name}' without ownership.", this);
                return false;
            }

            bool shouldIgnore = rules && rules.ShouldIgnoreRequireServer();

            if (!shouldIgnore && signature.requireServer && !networkManager.isServer)
            {
                if (signature is { runLocally: false, channel: Channel.ReliableOrdered or Channel.ReliableUnordered })
                    PurrLogger.LogError(
                        $"Trying to send RPC '{signature.rpcName}' from '{GetType().Name}' without server.", this);
                return false;
            }

            return true;
        }

        [UsedByIL]
        public bool ValidateReceivingRPC(RPCInfo info, RPCSignature signature, IRpc data, bool asServer)
        {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            _myType ??= GetType();
            Statistics.ReceivedRPC(_myType, signature.type, signature.rpcName, data.rpcData.segment, this);
#endif
            return ValidateIncomingRPC(info, signature, data, asServer);
        }

        internal bool ValidateIncomingRPC(RPCInfo info, RPCSignature signature, IRpc data, bool asServer)
        {
            var rules = networkManager.networkRules;
            bool shouldIgnoreOwnership = rules && rules.ShouldIgnoreRequireOwner();

            if (!networkManager.TryGetModule<RPCModule>(networkManager.isServer, out var module))
                return false;

            if (!shouldIgnoreOwnership && signature.requireOwnership && info.sender != owner)
                return false;

            if (signature.excludeOwner && isOwner)
                return false;

            if (signature.type == RPCType.ServerRPC)
            {
                if (!asServer)
                {
                    PurrLogger.LogError(
                        $"Trying to receive server RPC '{signature.rpcName}' from '{name}' on client. Aborting RPC call.",
                        this);
                    return false;
                }

                var idObservers = observers;

                if (idObservers == null)
                {
                    PurrLogger.LogError(
                        $"Trying to receive server RPC '{signature.rpcName}' from '{name}' but failed to get observers.",
                        this);
                    return false;
                }

                if (!IsObserver(info.sender) && signature.channel == Channel.ReliableOrdered)
                {
                    PurrLogger.LogError(
                        $"Trying to receive server RPC '{signature.rpcName}' from '{name}' by player '{info.sender}' which is not an observer. Aborting RPC call.",
                        this);
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
                    $"Trying to receive client RPC '{signature.rpcName}' from '{name}' on server. " +
                    "If you want automatic forwarding use 'requireServer: false'.", this);
                return false;
            }

            switch (signature.type)
            {
                case RPCType.ServerRPC: throw new InvalidOperationException("ServerRPC should be handled by server.");

                case RPCType.ObserversRPC:
                {
                    var cachedOwner = owner;
                    using var players = DisposableList<PlayerID>.Create(observers.Count);

                    for (var i = 0; i < observers.Count; ++i)
                    {
                        var observer = observers[i];

                        bool ignoreSender = observer == info.sender && (signature.excludeSender || signature.runLocally);
                        bool ignoreOwner = signature.excludeOwner && observer == cachedOwner;

                        if (ignoreSender || ignoreOwner)
                            continue;

                        players.Add(observer);
                    }

                    Send(players, BroadcastModule.GetImmediateData(data), signature.channel);
                    AppendToBufferedRPCs(signature, data, module);
                    return !isClient;
                }
                case RPCType.TargetRPC:
                {
                    var rawData = BroadcastModule.GetImmediateData(data);
                    bool shouldExecute = SendToTargetOrServer(rules, data.targetPlayerId, rawData, signature.channel);
                    AppendToBufferedRPCs(signature, data, module);
                    return shouldExecute;
                }
                default: throw new ArgumentOutOfRangeException(nameof(signature.type));
            }
        }

        private static void AppendToBufferedRPCs(RPCSignature signature, IRpc data, RPCModule module)
        {
            switch (data)
            {
                case RPCPacket rpcPacket:
                    module.AppendToBufferedRPCs(rpcPacket, signature);
                    break;
                case ChildRPCPacket childRpcPacket:
                    module.AppendToBufferedRPCs(childRpcPacket, signature);
                    break;
            }
        }

        public void Send<T>(PlayerID player, T packet, Channel method = Channel.ReliableOrdered)
        {
            if (networkManager.isServer)
                networkManager.GetModule<PlayersManager>(true).Send(player, packet, method);
        }

        public void Send(PlayerID player, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (networkManager.isServer)
                networkManager.GetModule<PlayersManager>(true).SendRaw(player, data, method);
        }

        bool SendToTargetOrServer(NetworkRules rules, PlayerID player, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (player == PlayerID.Server)
            {
                if (rules.CanTargetServerWithTargetRpc())
                    return true;

                PurrLogger.LogError($"Trying to send TargetRPC to server `{name}`" +
                                    $" but `NetworkRules` don't allow for this.", this);
                return false;
            }

            if (!IsObserver(player))
            {
                PurrLogger.LogError($"Trying to send TargetRPC to player '{player}' which is not observing '{name}'.",
                    this);
                return false;
            }

            Send(player, data, method);
            return false;
        }

        public void Send<T>(IReadOnlyList<PlayerID> players, T data, Channel method = Channel.ReliableOrdered)
        {
            if (networkManager.isServer)
                networkManager.GetModule<PlayersManager>(true).Send(players, data, method);
        }

        public void Send(IReadOnlyList<PlayerID> players, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (networkManager.isServer)
                networkManager.GetModule<PlayersManager>(true).SendRaw(players, data, method);
        }

        public void SendToServer<T>(T packet, Channel method = Channel.ReliableOrdered)
        {
            if (networkManager.isClient)
                networkManager.GetModule<PlayersManager>(false).SendToServer(packet, method);
        }
    }
}
