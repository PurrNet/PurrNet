namespace PurrNet
{
    public partial class NetworkRigidbody
    {
        // This source counter stays monotonic across authority changes so an in-flight packet
        // from an earlier ownership stint cannot hold a re-acquired controller below its value.
        private uint _sendStateSequence;

        private uint _serverAuthorityEpoch;
        private uint _serverReceivedSequence;
        private bool _serverHasReceivedSequence;
        private uint _serverRelaySequence;
        private RigidbodyStateData _latestServerState;
        private bool _hasLatestServerState;
        private bool _serverAuthorityStateInitialized;
        private bool _serverAuthorityHasConnectedOwner;
        private PlayerID? _serverAuthorityOwner;

        private uint _receivedAuthorityEpoch;
        private uint _receivedStateSequence;
        private bool _hasReceivedStateOrder;

        private void InitializeStateOrdering()
        {
            _sendStateSequence = 0;
            _receivedAuthorityEpoch = 0;
            _receivedStateSequence = 0;
            _hasReceivedStateOrder = false;

            if (!isServer)
                return;

            EnsureServerAuthorityEpoch();
            _serverReceivedSequence = 0;
            _serverHasReceivedSequence = false;
            _serverRelaySequence = 0;
            RecordServerAuthorityState();
            _latestServerState = default;
            _hasLatestServerState = false;
        }

        private void ResetStateOrdering()
        {
            _sendStateSequence = 0;
            _serverAuthorityEpoch = 0;
            _serverReceivedSequence = 0;
            _serverHasReceivedSequence = false;
            _serverRelaySequence = 0;
            _latestServerState = default;
            _hasLatestServerState = false;
            _serverAuthorityStateInitialized = false;
            _serverAuthorityHasConnectedOwner = false;
            _serverAuthorityOwner = null;
            _receivedAuthorityEpoch = 0;
            _receivedStateSequence = 0;
            _hasReceivedStateOrder = false;
        }

        private uint NextStateSequence()
        {
            _sendStateSequence = NetworkRigidbodySequenceMath.IncrementNonZero(_sendStateSequence);
            return _sendStateSequence;
        }

        private uint NextServerRelaySequence()
        {
            _serverRelaySequence = NetworkRigidbodySequenceMath.IncrementNonZero(_serverRelaySequence);
            return _serverRelaySequence;
        }

        private void EnsureServerAuthorityEpoch()
        {
            if (_serverAuthorityEpoch == 0)
                _serverAuthorityEpoch = 1;
        }

        private void BeginServerAuthorityEpoch()
        {
            EnsureServerAuthorityEpoch();
            _serverAuthorityEpoch = NetworkRigidbodySequenceMath.IncrementNonZero(_serverAuthorityEpoch);
            _serverReceivedSequence = 0;
            _serverHasReceivedSequence = false;
            _serverRelaySequence = 0;
            _latestServerState = default;
            _hasLatestServerState = false;
        }

        private bool TryStampAndCacheServerAuthorityAnchor(ref RigidbodyStateData data, string source)
        {
            EnsureServerAuthorityEpoch();
            data.authorityEpoch = _serverAuthorityEpoch;
            data.sequence = 0;

            if (!ValidateOutgoingSnapshot(in data, source))
                return false;

            CacheLatestServerState(in data);
            return true;
        }

        private void CacheLatestServerState(in RigidbodyStateData data)
        {
            _latestServerState = data;
            _hasLatestServerState = true;
        }

        private bool TryPrepareServerOrder(uint sequence, out uint authorityEpoch)
        {
            EnsureServerAuthorityEpoch();
            authorityEpoch = _serverAuthorityEpoch;

            return NetworkRigidbodySequenceMath.TryAcceptSourceSequence(
                sequence,
                ref _serverHasReceivedSequence,
                ref _serverReceivedSequence);
        }

        private bool TryPrepareStateForServerRelay(ref RigidbodyStateData data)
        {
            if (!TryPrepareServerOrder(data.sequence, out data.authorityEpoch))
                return false;

            // Controllers provide source order; observers only see canonical server relay order.
            data.sequence = NextServerRelaySequence();
            CacheLatestServerState(in data);
            return true;
        }

        private bool TryGetLatestServerState(out RigidbodyStateData data)
        {
            data = _latestServerState;
            return isServer && _hasLatestServerState;
        }

        private bool IsCurrentControllerSender(RPCInfo info)
        {
            return _ownerAuth
                   && hasConnectedOwner
                   && owner.HasValue
                   && info.sender == owner.Value;
        }

        private bool TryAcceptStateOrder(in RigidbodyStateData data)
        {
            return TryAcceptStateOrder(data.authorityEpoch, data.sequence);
        }

        private bool TryAcceptStateOrder(uint authorityEpoch, uint sequence)
        {
            bool accepted = NetworkRigidbodySequenceMath.TryAccept(
                authorityEpoch,
                sequence,
                ref _hasReceivedStateOrder,
                ref _receivedAuthorityEpoch,
                ref _receivedStateSequence,
                out bool epochChanged);

            if (accepted && epochChanged)
                ClearBuffer();
            return accepted;
        }

        private void RecordServerAuthorityState()
        {
            _serverAuthorityOwner = hasConnectedOwner && owner.HasValue ? owner : null;
            _serverAuthorityHasConnectedOwner = _serverAuthorityOwner.HasValue;
            _serverAuthorityStateInitialized = true;
        }

        private bool TryRecordServerAuthorityTransition()
        {
            var connectedOwner = hasConnectedOwner && owner.HasValue ? owner : null;
            bool hasConnectedController = connectedOwner.HasValue;

            if (_serverAuthorityStateInitialized &&
                _serverAuthorityHasConnectedOwner == hasConnectedController &&
                Equals(_serverAuthorityOwner, connectedOwner))
                return false;

            _serverAuthorityOwner = connectedOwner;
            _serverAuthorityHasConnectedOwner = hasConnectedController;
            _serverAuthorityStateInitialized = true;
            return true;
        }

        private RigidbodyStateData CaptureServerAuthorityAnchor()
        {
            // Auto-ownership can change before OnSpawned initializes the interpolation target.
            return !isFullySpawned || IsController(_ownerAuth)
                ? CaptureCurrentState()
                : CaptureTargetState();
        }

        private void BroadcastServerAuthorityTransition(PlayerID? primaryTarget, PlayerID? secondaryTarget)
        {
            if (!isServer || !_ownerAuth || !isSpawned || !_rigidbody)
                return;

            if (!TryRecordServerAuthorityTransition())
                return;

            BeginServerAuthorityEpoch();

            var anchor = CaptureServerAuthorityAnchor();
            if (!TryStampAndCacheServerAuthorityAnchor(ref anchor, "authority transition"))
                return;

            SyncReliableState(anchor);

            if (primaryTarget.HasValue && primaryTarget != localPlayer)
                SendHandoffState(primaryTarget.Value, anchor);

            if (secondaryTarget.HasValue &&
                secondaryTarget != primaryTarget &&
                secondaryTarget != localPlayer)
                SendHandoffState(secondaryTarget.Value, anchor);
        }
    }

    internal static class NetworkRigidbodySequenceMath
    {
        internal static bool IsNewer(uint incoming, uint current)
        {
            return incoming != current && unchecked((int)(incoming - current)) > 0;
        }

        internal static bool TryAcceptSourceSequence(
            uint incoming,
            ref bool hasSequence,
            ref uint current)
        {
            if (incoming == 0)
                return false;

            if (hasSequence && !IsNewer(incoming, current))
                return false;

            current = incoming;
            hasSequence = true;
            return true;
        }

        internal static bool TryAccept(
            uint incomingEpoch,
            uint incomingSequence,
            ref bool hasOrder,
            ref uint currentEpoch,
            ref uint currentSequence,
            out bool epochChanged)
        {
            epochChanged = false;

            // Observer data must have passed through the server's canonical epoch stamp.
            if (incomingEpoch == 0)
                return false;

            if (!hasOrder)
            {
                currentEpoch = incomingEpoch;
                currentSequence = incomingSequence;
                hasOrder = true;
                return true;
            }

            if (incomingEpoch == currentEpoch)
            {
                if (!IsNewer(incomingSequence, currentSequence))
                    return false;

                currentSequence = incomingSequence;
                return true;
            }

            if (!IsNewer(incomingEpoch, currentEpoch))
                return false;

            currentEpoch = incomingEpoch;
            currentSequence = incomingSequence;
            epochChanged = true;
            return true;
        }

        internal static uint IncrementNonZero(uint value)
        {
            unchecked
            {
                value++;
                return value == 0 ? 1u : value;
            }
        }
    }
}
