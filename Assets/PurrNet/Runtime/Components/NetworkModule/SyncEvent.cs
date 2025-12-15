using UnityEngine;
using PurrNet.Logging;
using System;
using PurrNet.Packing;
using UnityEngine.Events;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    public struct SyncEventData : IDisposable
    {
        public BitPacker _dataPacker;

        public SyncEventData AddData<T>(T data)
        {
            if(_dataPacker == null)
                _dataPacker = BitPackerPool.Get();
            Packer<T>.Write(_dataPacker, data);
            return this;
        }

        public T ReadData<T>()
        {
            if (_dataPacker == null)
                return default;
            
            return Packer<T>.Read(_dataPacker);
        }

        public void ResetPosition()
        {
            _dataPacker?.ResetPosition();
        }

        public void Dispose()
        {
            _dataPacker.Dispose();
        }
    }
    
    [Serializable]
    public abstract class SyncEventBase : NetworkModule
    {
        [SerializeField, PurrLock] protected bool _ownerAuth;

        /// <summary>
        /// Whether it is the owner or  the server that has the authority to invoke the event
        /// </summary>
        public bool ownerAuth => _ownerAuth;

        protected SyncEventBase(bool ownerAuth = false)
        {
            _ownerAuth = ownerAuth;
        }

        protected bool ValidateInvoke()
        {
            if (!isSpawned) return true;

            bool controller = parent.IsController(_ownerAuth);
            if (!controller)
            {
                PurrLogger.LogError(
                    $"Invalid permissions when invoking `<b>{GetType().Name} {name}</b>` on `{parent.name}`." +
                    $"\n{GetPermissionErrorDetails(_ownerAuth, this)}", parent);
                return false;
            }

            return true;
        }

        protected abstract void InvokeLocal();
    }
    
    [Serializable]
    public abstract class SyncEventLogic<T> : SyncEventBase
    {
        protected SyncEventData _lastData;

        protected SyncEventLogic(bool ownerAuth = false) : base(ownerAuth) { }

        protected abstract void InvokeUnityEvent(T data);
        protected virtual void ClearUnityEvent() { }

        public void InvokePacket(SyncEventData data)
        {
            if (!ValidateInvoke()) return;

            _lastData = data;

            if (isSpawned)
            {
                if (isServer) SendToAll(data);
                else SendToServer(data);
            }
            
            _lastData.ResetPosition();
            InvokeLocal();
        }

        public void RemoveAllListeners(bool sync = false)
        {
            if (!sync) { ClearUnityEvent(); return; }
            RemoveAllListenersRpc();
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendToServer(SyncEventData data)
        {
            if (!_ownerAuth) return;
            SendToOthers(data);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendToOthers(SyncEventData data)
        {
            using (data)
            {
                if (isServer && !isHost) return;
                _lastData = data;
                InvokeLocal();
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendToAll(SyncEventData data)
        {
            using (data)
            {
                if (!isHost)
                {
                    _lastData = data;
                    InvokeLocal();
                }
            }
        }

        [ObserversRpc(runLocally: true)]
        private void RemoveAllListenersRpc() => ClearUnityEvent();
    }
    
    [Serializable]
    public class SerializableSyncUnityEvent : UnityEvent { }
    
    [Serializable]
    public class SyncEvent : SyncEventLogic<byte>
    {
        [SerializeField] private SerializableSyncUnityEvent unityEvent = new SerializableSyncUnityEvent();

        public SyncEvent(bool ownerAuth = false) : base(ownerAuth) { }

        public void AddListener(UnityAction listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction listener) => unityEvent.RemoveListener(listener);

        public void Invoke() => InvokePacket(default);

        protected override void InvokeLocal()
        {
            unityEvent.Invoke();
        }

        protected override void InvokeUnityEvent(byte data) => unityEvent?.Invoke();
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();
        
        public static SyncEvent operator +(SyncEvent e, UnityAction listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent operator -(SyncEvent e, UnityAction listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }
    
    [Serializable]
    public class SerializableSyncUnityEvent<T> : UnityEvent<T> { }
    
    [Serializable]
    public class SyncEvent<T> : SyncEventLogic<T>
    {
        [SerializeField] private SerializableSyncUnityEvent<T> unityEvent = new SerializableSyncUnityEvent<T>();

        public SyncEvent(bool ownerAuth = false) : base(ownerAuth) { }

        public void AddListener(UnityAction<T> listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction<T> listener) => unityEvent.RemoveListener(listener);

        public void Invoke(T arg) => InvokePacket(new SyncEventData().AddData<T>(arg));

        protected override void InvokeLocal()
        {
            T value = _lastData.ReadData<T>();
            unityEvent.Invoke(value);
        }

        protected override void InvokeUnityEvent(T arg) => unityEvent?.Invoke(arg);
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();
        
        public static SyncEvent<T> operator +(SyncEvent<T> e, UnityAction<T> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T> operator -(SyncEvent<T> e, UnityAction<T> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }

    [Serializable]
    public class SerializableSyncUnityEvent<T1, T2> : UnityEvent<T1, T2> { }

    [Serializable]
    public class SyncEvent<T1, T2> : SyncEventLogic<(T1, T2)>
    {
        [SerializeField] private SerializableSyncUnityEvent<T1, T2> unityEvent = new SerializableSyncUnityEvent<T1, T2>();

        public SyncEvent(bool ownerAuth = false) : base(ownerAuth) { }

        public void AddListener(UnityAction<T1, T2> listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction<T1, T2> listener) => unityEvent.RemoveListener(listener);

        public void Invoke(T1 arg1, T2 arg2) => InvokePacket(new SyncEventData().AddData<T1>(arg1).AddData<T2>(arg2));
        
        protected override void InvokeLocal()
        {
            T1 value1 = _lastData.ReadData<T1>();
            T2 value2 = _lastData.ReadData<T2>();
            unityEvent.Invoke(value1, value2);
        }
        
        protected override void InvokeUnityEvent((T1, T2) data) => unityEvent?.Invoke(data.Item1, data.Item2);
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();
        
        public static SyncEvent<T1, T2> operator +(SyncEvent<T1, T2> e, UnityAction<T1, T2> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T1, T2> operator -(SyncEvent<T1, T2> e, UnityAction<T1, T2> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }
    
    [Serializable]
    public class SerializableSyncUnityEvent<T1, T2, T3> : UnityEvent<T1, T2, T3> { }
    
    [Serializable]
    public class SyncEvent<T1, T2, T3> : SyncEventLogic<(T1, T2, T3)>
    {
        [SerializeField] private SerializableSyncUnityEvent<T1, T2, T3> unityEvent = new SerializableSyncUnityEvent<T1, T2, T3>();

        public SyncEvent(bool ownerAuth = false) : base(ownerAuth) { }
    
        public void AddListener(UnityAction<T1, T2, T3> listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction<T1, T2, T3> listener) => unityEvent.RemoveListener(listener);

        public void Invoke(T1 a, T2 b, T3 c) => InvokePacket(new SyncEventData().AddData<T1>(a).AddData<T2>(b).AddData<T3>(c));
        protected override void InvokeLocal()
        {
            T1 value1 = _lastData.ReadData<T1>();
            T2 value2 = _lastData.ReadData<T2>();
            T3 value3 = _lastData.ReadData<T3>();
            unityEvent.Invoke(value1, value2, value3);
        }
        
        protected override void InvokeUnityEvent((T1, T2, T3) data) => unityEvent?.Invoke(data.Item1, data.Item2, data.Item3);
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();
        
        public static SyncEvent<T1, T2, T3> operator +(SyncEvent<T1, T2, T3> e, UnityAction<T1, T2, T3> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T1, T2, T3> operator -(SyncEvent<T1, T2, T3> e, UnityAction<T1, T2, T3> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }
    
    [Serializable]
    public class SerializableSyncUnityEvent<T1, T2, T3, T4> : UnityEvent<T1, T2, T3, T4> { }
    
    [Serializable]
    public class SyncEvent<T1, T2, T3, T4> : SyncEventLogic<(T1, T2, T3, T4)>
    {
        [SerializeField] private SerializableSyncUnityEvent<T1, T2, T3, T4> unityEvent = new SerializableSyncUnityEvent<T1, T2, T3, T4>();

        public SyncEvent(bool ownerAuth = false) : base(ownerAuth) { }
    
        public void AddListener(UnityAction<T1, T2, T3, T4> listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction<T1, T2, T3, T4> listener) => unityEvent.RemoveListener(listener);

        public void Invoke(T1 a, T2 b, T3 c, T4 d) => InvokePacket(new SyncEventData().AddData<T1>(a).AddData<T2>(b).AddData<T3>(c).AddData<T4>(d));
        protected override void InvokeLocal()
        {
            T1 value1 = _lastData.ReadData<T1>();
            T2 value2 = _lastData.ReadData<T2>();
            T3 value3 = _lastData.ReadData<T3>();
            T4 value4 = _lastData.ReadData<T4>();
            unityEvent.Invoke(value1, value2, value3, value4);
        }
        
        protected override void InvokeUnityEvent((T1, T2, T3, T4) data) => unityEvent?.Invoke(data.Item1, data.Item2, data.Item3, data.Item4);
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();
        
        public static SyncEvent<T1, T2, T3, T4> operator +(SyncEvent<T1, T2, T3, T4> e, UnityAction<T1, T2, T3, T4> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T1, T2, T3, T4> operator -(SyncEvent<T1, T2, T3, T4> e, UnityAction<T1, T2, T3, T4> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }
    
    [Serializable]
    public class SyncEvent<T1, T2, T3, T4, T5> : SyncEventLogic<(T1, T2, T3, T4, T5)>
    {
        private event Action<T1, T2, T3, T4, T5> unityEvent;
        
        public SyncEvent(bool ownerAuth = false) : base(ownerAuth) { }
    
        public void AddListener(Action<T1, T2, T3, T4, T5> listener) => unityEvent += listener;
        public void RemoveListener(Action<T1, T2, T3, T4, T5> listener) => unityEvent -= listener;

        public void Invoke(T1 a, T2 b, T3 c, T4 d, T5 e) => InvokePacket(new SyncEventData().AddData<T1>(a).AddData<T2>(b).AddData<T3>(c).AddData<T4>(d).AddData<T5>(e));
        protected override void InvokeLocal()
        {
            T1 value1 = _lastData.ReadData<T1>();
            T2 value2 = _lastData.ReadData<T2>();
            T3 value3 = _lastData.ReadData<T3>();
            T4 value4 = _lastData.ReadData<T4>();
            T5 value5 = _lastData.ReadData<T5>();
            unityEvent.Invoke(value1, value2, value3, value4, value5);
        }
        
        protected override void InvokeUnityEvent((T1, T2, T3, T4, T5) data) => unityEvent?.Invoke(data.Item1, data.Item2, data.Item3, data.Item4, data.Item5);
        
        public static SyncEvent<T1, T2, T3, T4, T5> operator +(SyncEvent<T1, T2, T3, T4, T5> e, Action<T1, T2, T3, T4, T5> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T1, T2, T3, T4, T5> operator -(SyncEvent<T1, T2, T3, T4, T5> e, Action<T1, T2, T3, T4, T5> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }
}
