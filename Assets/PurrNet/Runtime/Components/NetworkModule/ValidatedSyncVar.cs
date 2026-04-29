using System;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public class ValidatedSyncVar<T> : NetworkModule
    {
        private SyncVar<T> _authoritative;

        public delegate bool ServerValidationHandler(T oldValue, T newValue);
        public delegate void ValidationFailedHandler(T failedValue, T authoritativeValue);

        public event ServerValidationHandler serverValidation;
        public event ValidationFailedHandler onValidationFail;


        public delegate void OnChangeDelegate(T newValue, bool serverValidated);
        public event OnChangeDelegate onChanged;

        public delegate void OnChangeWithOldDelegate(T oldValue, T newValue, bool serverValidated);
        public event OnChangeWithOldDelegate onChangedWithOld;

        private T _display;
        private uint _nextPacketId;
        private uint _lastAppliedServerId;
        private uint _pendingId;
        private bool _hasPending;

        public static implicit operator T(ValidatedSyncVar<T> syncVar) => syncVar._display;

        public ValidatedSyncVar(T initialValue = default)
        {
            _authoritative = new SyncVar<T>(initialValue, 0f, false);
            _display = initialValue;
        }

        public T value
        {
            get => _display;
            set
            {
                if (!isServer && !isOwner)
                    return;

                var old = _display;
                if ((old == null && value == null) || (old != null && old.Equals(value)))
                    return;

                if (isServer)
                {
                    _display = value;
                    TriggerEvents(old, _display, false);
                    ServerValidateAndApply(value);
                    return;
                }

                if (!owner.HasValue)
                    return;

                _display = value;
                _pendingId = ++_nextPacketId;
                _hasPending = true;
                TriggerEvents(old, _display, false);

                using var pack = BitPackerPool.Get();
                Packer<T>.Write(pack, value);
                SubmitCandidate(owner.Value, _pendingId, pack);
            }
        }

        public override void OnPoolReset()
        {
            onChanged = null;
            onChangedWithOld = null;
            serverValidation = null;
            onValidationFail = null;
            _nextPacketId = 0;
            _lastAppliedServerId = 0;
            _pendingId = 0;
            _hasPending = false;
        }

        public override void OnEarlySpawn()
        {
            if (!isServer)
                _authoritative.onChangedWithOld += OnAuthoritativeChanged;
        }

        public override void OnDespawned()
        {
            if (!isServer)
                _authoritative.onChangedWithOld -= OnAuthoritativeChanged;
        }

        public override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool isSpawnEvent, bool asServer)
        {
            _authoritative.onChangedWithOld -= OnAuthoritativeChanged;
            if (!isServer)
                _authoritative.onChangedWithOld += OnAuthoritativeChanged;
        }

        private void OnAuthoritativeChanged(T oldAuth, T newAuth)
        {
            if (isOwner && _hasPending) return;
            var old = _display;
            if ((old == null && newAuth == null) || (old != null && old.Equals(newAuth)))
                return;
            _display = newAuth;
            TriggerEvents(old, _display, true);
        }

        private void TriggerEvents(T oldValue, T newValue, bool serverValidated)
        {
            try { onChanged?.Invoke(newValue, serverValidated); } catch (Exception e) { Debug.LogException(e); }
            try { onChangedWithOld?.Invoke(oldValue, newValue, serverValidated); } catch (Exception e) { Debug.LogException(e); }
        }

        private void ApplyAuthoritative(T v)
        {
            var oldDisplay = _display;
            _authoritative.value = v;
            _display = v;
            TriggerEvents(oldDisplay, v, true);
        }

        private bool RunServerValidators(T oldValue, T newValue)
        {
            var list = serverValidation?.GetInvocationList();
            if (list == null) return true;
            for (int i = 0; i < list.Length; i++)
                if (!((ServerValidationHandler)list[i]).Invoke(oldValue, newValue))
                    return false;
            return true;
        }

        private void ServerValidateAndApply(T proposed)
        {
            var current = _authoritative.value;
            if (!RunServerValidators(current, proposed))
            {
                var old = _display;
                _display = current;
                if (!Equals(old, _display)) TriggerEvents(old, _display, true);
                onValidationFail?.Invoke(proposed, current);
                return;
            }
            ApplyAuthoritative(proposed);
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: false)]
        private void SubmitCandidate(PlayerID sender, PackedUInt packetId, BitPacker candidate)
        {
            using (candidate)
            {
                if (!isServer) return;
                if (packetId <= _lastAppliedServerId) return;

                if (!owner.HasValue || sender != owner.Value)
                {
                    using var rejNoCtrl = BitPackerPool.Get();
                    var current = _authoritative.value;
                    Packer<T>.Write(rejNoCtrl, current);
                    T failedNoCtrl = default;
                    Packer<T>.Write(rejNoCtrl, failedNoCtrl);
                    RejectOwner(sender, packetId, rejNoCtrl);
                    return;
                }

                T proposed = default;
                Packer<T>.Read(candidate, ref proposed);
                var currentAuth = _authoritative.value;

                if (!RunServerValidators(currentAuth, proposed))
                {
                    using var rej = BitPackerPool.Get();
                    Packer<T>.Write(rej, currentAuth);
                    Packer<T>.Write(rej, proposed);
                    RejectOwner(sender, packetId, rej);
                    return;
                }

                _lastAppliedServerId = packetId;
                ApplyAuthoritative(proposed);

                using var ack = BitPackerPool.Get();
                Packer<T>.Write(ack, proposed);
                AcceptOwner(sender, packetId, ack);
            }
        }

        [TargetRpc(Channel.ReliableOrdered)]
        private void AcceptOwner(PlayerID target, PackedUInt packetId, BitPacker payload)
        {
            using (payload)
            {
                if (isServer) return;
                if (packetId < _pendingId) return;

                T v = default;
                Packer<T>.Read(payload, ref v);
                var old = _display;
                _display = v;
                _pendingId = packetId;
                _hasPending = false;
                TriggerEvents(old, v, true);
            }
        }

        [TargetRpc(Channel.ReliableOrdered)]
        private void RejectOwner(PlayerID target, PackedUInt packetId, BitPacker payload)
        {
            using (payload)
            {
                if (isServer) return;
                if (packetId < _pendingId) return;

                T authoritativeNow = default;
                T failed = default;
                Packer<T>.Read(payload, ref authoritativeNow);
                Packer<T>.Read(payload, ref failed);

                var old = _display;
                _display = authoritativeNow;
                _pendingId = packetId;
                _hasPending = false;
                TriggerEvents(old, _display, true);
                onValidationFail?.Invoke(failed, authoritativeNow);
            }
        }
    }
}
