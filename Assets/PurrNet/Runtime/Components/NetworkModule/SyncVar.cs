using UnityEngine;
using PurrNet.Logging;
using PurrNet.Modules;
using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    [Serializable]
    public class SyncVar<T> : NetworkModule
    {
        private TickManager _tickManager;

        [SerializeField, PurrLock] private T _value;

        private bool _isDirty;

        [SerializeField, Space(-5), Header("Sync Settings"), PurrLock]
        private bool _ownerAuth;

        [SerializeField, Min(0)] private float _sendIntervalInSeconds;

        public bool ownerAuth => _ownerAuth;

        public float sendIntervalInSeconds
        {
            get => _sendIntervalInSeconds;
            set => _sendIntervalInSeconds = value;
        }

        public event Action<T> onChanged;

        public delegate void ActionWithOld(T oldValue, T newValue);
        public event ActionWithOld onChangedWithOld;

        public bool isControllingSyncVar { get; private set; }

        private bool _isSubscribedToTickManager;

        static readonly IEqualityComparer<T> _cmp = EqualityComparer<T>.Default;

        public T value
        {
            get => _value;
            set
            {
                if (_cmp.Equals(value, _value)) return;

                if (isSpawned && !parent.IsController(_ownerAuth))
                {
                    PurrLogger.LogError(
                        $"Invalid permissions when setting `<b>SyncVar<{typeof(T).Name}> {name}</b>` on `{parent.name}`." +
                        $"\n{GetPermissionErrorDetails(_ownerAuth, this)}", parent);
                    return;
                }

                var oldValue = _value;
                _value = value;

                SetDirty();
                TriggerEvents(oldValue);
            }
        }

        public override void OnPoolReset()
        {
            onChanged = null;
            onChangedWithOld = null;
            isControllingSyncVar = false;
        }

        public override void OnOwnerDisconnected(PlayerID ownerId)
        {
            InvalidateIsController();
        }

        public override void OnOwnerReconnected(PlayerID ownerId)
        {
            InvalidateIsController();
        }

        public override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool isSpawnEvent, bool asServer)
        {
            InvalidateIsController();

            if (isSpawnEvent)
                return;

            if (_ownerAuth)
            {
                _id = 0;

                if (isOwner)
                    SetDirty();
            }
        }

        private void SubscribeToTickManager()
        {
            if (_isSubscribedToTickManager)
                return;

            _isSubscribedToTickManager = true;
            networkManager.tickModule.onTick += OnTick;
        }

        private void UnsubscribeFromTickManager()
        {
            if (!_isSubscribedToTickManager)
                return;

            _isSubscribedToTickManager = false;
            networkManager.tickModule.onTick -= OnTick;
        }

        public override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            if (isSpawner && ownerAuth && owner == player)
                return;

            SendLatestState(player, _id, _value);
        }

        public override void OnSpawn()
        {
            InvalidateIsController();
        }

        private void InvalidateIsController()
        {
            isControllingSyncVar = parent.IsController(_ownerAuth);
        }

        public override void OnDespawned()
        {
            if (isControllingSyncVar)
            {
                _id += 1;
                FlushImmediately();
            }
        }

        public void SetDirty()
        {
            if (_isDirty || !isControllingSyncVar)
                return;

            _isDirty = true;
            SubscribeToTickManager();
        }

        private float _lastSendTime;

        private void ForceSendUnreliable()
        {
            if (isServer)
                SendToAll(_id++, _value);
            else SendToServer(_id++, _value);
        }

        private void ForceSendReliable()
        {
            if (isServer)
                SendToAllReliably(_id++, _value);
            else SendToServerReliably(_id++, _value);
        }

        public void FlushImmediately()
        {
            ForceSendReliable();
            _lastSendTime = Time.time;
            _wasLastDirty = false;
            _isDirty = false;
            UnsubscribeFromTickManager();
        }

        public void OnTick()
        {
            if (!isControllingSyncVar)
                return;

            if (_isDirty)
            {
                float time = Time.time;

                if (time - _lastSendTime < _sendIntervalInSeconds)
                    return;

                ForceSendUnreliable();
                _lastSendTime = time;
                _wasLastDirty = true;
                _isDirty = false;
            }
            else if (_wasLastDirty)
            {
                ForceSendReliable();
                UnsubscribeFromTickManager();
                _wasLastDirty = false;
            }
        }

        private ulong _id;
        private bool _wasLastDirty;

        public SyncVar(T initialValue = default, float sendIntervalInSeconds = 0f, bool ownerAuth = false)
        {
            _value = initialValue;
            _sendIntervalInSeconds = sendIntervalInSeconds;
            _ownerAuth = ownerAuth;
        }

        [TargetRpc, UsedImplicitly]
        private void SendLatestState(PlayerID player, PackedULong packetId, T newValue)
        {
            if (isServer)
            {
                DisposeOf(newValue);
                return;
            }

            _id = packetId;

            var oldValue = _value;

            if (!Packer.Transform(ref _value, newValue))
            {
                DisposeOf(newValue);
                return;
            }

            TriggerEvents(oldValue);
            DisposeOf(newValue);
        }

        private static void DisposeOf(T newValue)
        {
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }

        [ServerRpc(Channel.Unreliable, requireOwnership: true)]
        private void SendToServer(PackedULong packetId, T newValue)
        {
            if (!_ownerAuth)
            {
                if (newValue is IDisposable disposable)
                    disposable.Dispose();
                return;
            }

            OnReceivedValue(packetId, newValue);
            SendToOthers(packetId, newValue);

            if (newValue is IDisposable newValDisp)
                newValDisp.Dispose();
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendToServerReliably(PackedULong packetId, T newValue)
        {
            if (!_ownerAuth)
            {
                if (newValue is IDisposable disposable)
                    disposable.Dispose();
                return;
            }

            OnReceivedValue(packetId, newValue);
            SendToOthersReliably(packetId, newValue);

            if (newValue is IDisposable newValDisp)
                newValDisp.Dispose();
        }

        [ObserversRpc(Channel.Unreliable, excludeOwner: true)]
        private void SendToOthers(PackedULong packetId, T newValue)
        {
            if (!isServer) OnReceivedValue(packetId, newValue);
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendToOthersReliably(PackedULong packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }

        [ObserversRpc(Channel.Unreliable)]
        private void SendToAll(PackedULong packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendToAllReliably(PackedULong packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }

        private void OnReceivedValue(PackedULong packetId, T newValue)
        {
            if (isControllingSyncVar)
            {
                return;
            }

            if (packetId <= _id)
            {
                return;
            }

            _id = packetId;
            var oldValue = _value;

            if (!Packer.Transform(ref _value, newValue))
                return;

            TriggerEvents(oldValue);
        }

        private void TriggerEvents(T oldValue)
        {
            try
            {
                onChanged?.Invoke(value);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                onChangedWithOld?.Invoke(oldValue, value);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static implicit operator T(SyncVar<T> syncVar)
        {
            return syncVar._value;
        }

        public override string ToString()
        {
            return value.ToString();
        }
    }
}
