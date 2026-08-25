using System;
using System.Collections.Generic;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    public delegate void PlayerBroadcastDelegate<in T>(PlayerID player, T data, bool asServer);

    internal interface IPlayerBroadcastCallback
    {
        bool IsSame(object callback);

        void TriggerCallback(PlayerID playerId, BitPacker data, bool asServer);
    }

    internal readonly struct PlayerBroadcastCallback<T> : IPlayerBroadcastCallback
    {
        readonly PlayerBroadcastDelegate<T> callback;

        public PlayerBroadcastCallback(PlayerBroadcastDelegate<T> callback)
        {
            this.callback = callback;
        }

        public bool IsSame(object callbackToCmp)
        {
            return callbackToCmp is PlayerBroadcastDelegate<T> action && action == callback;
        }

        public void TriggerCallback(PlayerID playerId, BitPacker data, bool asServer)
        {
            var result = default(T);
            Packer<T>.Read(data, ref result);
            callback?.Invoke(playerId, result, asServer);
        }
    }

    public class PlayersBroadcaster : INetworkModule, IPlayerBroadcaster, IPromoteToServerModule
    {
        private readonly BroadcastModule _broadcastModule;
        private readonly PlayersManager _playersManager;

        private readonly Dictionary<uint, List<IPlayerBroadcastCallback>> _actions =
            new Dictionary<uint, List<IPlayerBroadcastCallback>>();

        private readonly List<Connection> _connections = new List<Connection>();

        private readonly Dictionary<PlayerID, ExactOutboundBarrier> _exactOutboundBarriers =
            new Dictionary<PlayerID, ExactOutboundBarrier>();

        private readonly Dictionary<PlayerID, int> _exactOutboundBarrierBypassDepth =
            new Dictionary<PlayerID, int>();

        private readonly struct ExactOutboundBarrier
        {
            public readonly HostMigrationTransitionOptions transition;
            public readonly Connection connection;

            public ExactOutboundBarrier(HostMigrationTransitionOptions transition,
                Connection connection)
            {
                this.transition = transition;
                this.connection = connection;
            }
        }

        private bool _asServer;

        public PlayersBroadcaster(BroadcastModule broadcastModule, PlayersManager playersManager)
        {
            _broadcastModule = broadcastModule;
            _playersManager = playersManager;
        }

        public void PromoteToServerModule()
        {
            _asServer = true;
        }

        public void PostPromoteToServerModule()
        {

        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;
            _broadcastModule.onRawDataReceived += OnRawDataReceived;
        }

        private void OnRawDataReceived(Connection conn, uint hash, BitPacker data)
        {
            if (!_playersManager.TryGetPlayer(conn, out var player))
                player = default;

            var bitpos = data.positionInBits;
            if (_actions.TryGetValue(hash, out var actions))
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    actions[i].TriggerCallback(player, data, _asServer);
                    data.SetBitPosition(bitpos);
                }
            }
        }

        public void Disable(bool asServer)
        {
            _broadcastModule.onRawDataReceived -= OnRawDataReceived;
            DropAllExactOutboundBarriers();
        }

        internal bool BeginExactOutboundBarrier(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            failure = null;
            if (!_asServer || !transition.canReconcile || player.isServer || player.isBot)
            {
                failure = "an exact outbound barrier requires a remote human player on the server";
                return false;
            }

            if (!_playersManager.TryGetConnection(player, out var connection))
            {
                failure = $"migration player {player} has no active connection to fence";
                return false;
            }

            if (_exactOutboundBarriers.TryGetValue(player, out var existing))
            {
                if (existing.transition != transition)
                {
                    failure = $"migration player {player} is already fenced for {existing.transition}";
                    return false;
                }

                if (existing.connection == connection)
                    return true;

                _exactOutboundBarrierBypassDepth.Remove(player);
                _broadcastModule.DropReliableOrderedOutboundBarrier(existing.connection);
            }

            _broadcastModule.BeginReliableOrderedOutboundBarrier(connection);
            _exactOutboundBarriers[player] = new ExactOutboundBarrier(transition, connection);
            return true;
        }

        internal bool ReleaseExactOutboundBarrier(PlayerID player,
            HostMigrationTransitionOptions transition)
        {
            if (!_exactOutboundBarriers.TryGetValue(player, out var barrier) ||
                barrier.transition != transition)
                return false;

            _exactOutboundBarriers.Remove(player);
            _exactOutboundBarrierBypassDepth.Remove(player);
            _broadcastModule.ReleaseReliableOrderedOutboundBarrier(barrier.connection);
            return true;
        }

        internal void DropExactOutboundBarrier(PlayerID player)
        {
            if (!_exactOutboundBarriers.Remove(player, out var barrier))
                return;

            _exactOutboundBarrierBypassDepth.Remove(player);
            _broadcastModule.DropReliableOrderedOutboundBarrier(barrier.connection);
        }

        internal void DropAllExactOutboundBarriers()
        {
            foreach (var barrier in _exactOutboundBarriers.Values)
                _broadcastModule.DropReliableOrderedOutboundBarrier(barrier.connection);
            _exactOutboundBarriers.Clear();
            _exactOutboundBarrierBypassDepth.Clear();
        }

        internal void SendExactBarrierBypass<T>(PlayerID player, T data,
            Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            if (!_exactOutboundBarriers.TryGetValue(player, out var barrier))
                return;

            _broadcastModule.SendBarrierBypass(
                barrier.connection, data, method, mtuOverride);
        }

        internal bool HasExactOutboundBarrier(PlayerID player,
            HostMigrationTransitionOptions transition) =>
            _exactOutboundBarriers.TryGetValue(player, out var barrier) &&
            barrier.transition == transition;

        internal bool BeginExactPackageBaselineCapture(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (!_exactOutboundBarriers.TryGetValue(player, out var barrier) ||
                barrier.transition != transition)
            {
                failure = $"player {player} has no exact outbound barrier for {transition}";
                return false;
            }

            return _broadcastModule.BeginPackageBaselineCapture(
                barrier.connection, out failure);
        }

        internal bool FinishExactPackageBaselineCapture(PlayerID player,
            HostMigrationTransitionOptions transition, bool commit, out string failure)
        {
            if (!_exactOutboundBarriers.TryGetValue(player, out var barrier) ||
                barrier.transition != transition)
            {
                failure = $"player {player} has no exact outbound barrier for {transition}";
                return false;
            }

            return _broadcastModule.FinishPackageBaselineCapture(
                barrier.connection, commit, out failure);
        }

        internal bool PublishExactPackageBaselines(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (!_exactOutboundBarriers.TryGetValue(player, out var barrier) ||
                barrier.transition != transition)
            {
                failure = $"player {player} has no exact outbound barrier for {transition}";
                return false;
            }

            return _broadcastModule.PublishPackageBaselines(
                barrier.connection, out failure);
        }

        internal bool RunExactOutboundBarrierBypass(PlayerID player,
            HostMigrationTransitionOptions transition, Action action)
        {
            if (action == null || !HasExactOutboundBarrier(player, transition))
                return false;

            _exactOutboundBarrierBypassDepth.TryGetValue(player, out var depth);
            _exactOutboundBarrierBypassDepth[player] = depth + 1;
            try
            {
                action();
            }
            finally
            {
                if (_exactOutboundBarrierBypassDepth.TryGetValue(player, out depth) && depth > 1)
                    _exactOutboundBarrierBypassDepth[player] = depth - 1;
                else
                    _exactOutboundBarrierBypassDepth.Remove(player);
            }

            return true;
        }

        public void Subscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
        {
            var hash = Hasher.GetStableHashU32(typeof(T));

            if (_actions.TryGetValue(hash, out var actions))
            {
                actions.Add(new PlayerBroadcastCallback<T>(callback));
                return;
            }

            _actions.Add(hash, new List<IPlayerBroadcastCallback>
            {
                new PlayerBroadcastCallback<T>(callback)
            });
        }

        public void Unsubscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
        {
            var hash = Hasher.GetStableHashU32(typeof(T));
            if (!_actions.TryGetValue(hash, out var actions))
                return;

            object boxed = callback;

            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].IsSame(boxed))
                {
                    actions.RemoveAt(i);
                    return;
                }
            }
        }

        public void Send<T>(PlayerID player, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            if (player == PlayerID.Server)
            {
                SendToServer(data, method, mtuOverride);
                return;
            }

            if (player.isBot)
                return;

            if (!_playersManager.TryGetConnection(player, out var conn))
                return;

            if (_exactOutboundBarrierBypassDepth.Count != 0 &&
                _exactOutboundBarrierBypassDepth.ContainsKey(player) &&
                _exactOutboundBarriers.TryGetValue(player, out var barrier) &&
                barrier.connection == conn)
            {
                _broadcastModule.SendBarrierBypass(conn, data, method, mtuOverride);
                return;
            }

            _broadcastModule.Send(conn, data, method, mtuOverride);
        }

        public void Send<T>(IEnumerable<PlayerID> players, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            _connections.Clear();

            foreach (var player in players)
            {
                if (player.isBot)
                    continue;

                if (_playersManager.TryGetConnection(player, out var conn))
                    _connections.Add(conn);
            }

            _broadcastModule.Send(_connections, data, method, mtuOverride);
        }

        public void Send<T>(IReadOnlyList<PlayerID> players, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            _connections.Clear();

            for (var index = 0; index < players.Count; index++)
            {
                var player = players[index];
                if (player.isBot)
                    continue;

                if (_playersManager.TryGetConnection(player, out var conn))
                    _connections.Add(conn);
            }

            _broadcastModule.Send(_connections, data, method, mtuOverride);
        }

        public void Send<T>(IList<PlayerID> players, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            _connections.Clear();

            for (var index = 0; index < players.Count; index++)
            {
                var player = players[index];
                if (player.isBot)
                    continue;

                if (_playersManager.TryGetConnection(player, out var conn))
                    _connections.Add(conn);
            }

            _broadcastModule.Send(_connections, data, method, mtuOverride);
        }

        public void SendToAll<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            _broadcastModule.SendToAll(data, method, mtuOverride);
        }

        public void SendToServer<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            _broadcastModule.SendToServer(data, method, mtuOverride);
        }
    }
}
