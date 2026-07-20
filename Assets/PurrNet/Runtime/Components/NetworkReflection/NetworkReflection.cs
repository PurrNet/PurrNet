using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Reflection")]
    public partial class NetworkReflection : NetworkIdentity, ITick
    {
        [PurrDocs("plug-n-play-components/network-reflection-auto-sync")]
        [Tooltip("The behaviour to track")]
        [SerializeField, HideInInspector]
        Object _trackedBehaviour;

        [Tooltip("The fields/properties to track and sync on the behaviour")] [SerializeField, HideInInspector]
        List<ReflectionData> _trackedFields;

        [Tooltip("Methods to expose as RPCs on the behaviour")] [SerializeField, HideInInspector]
        List<ReflectionMethodData> _trackedMethods;

        [Tooltip(
            "If true the owner has authority over this animator, if no owner is set it is controlled by the server")]
        [SerializeField, HideInInspector]
        private bool _ownerAuth = true;

        private ReflectedValue[] _reflectedValues;

        /// <summary>
        /// The type of the tracked behaviour
        /// </summary>
        public Type trackedType => _trackedBehaviour ? _trackedBehaviour.GetType() : null;

        private bool _initialized;

        /// <summary>
        /// The fields/properties to track and sync on the behaviour
        /// </summary>
        public List<ReflectionData> trackedFields
        {
            get => _trackedFields;
            set => _trackedFields = value;
        }

        /// <summary>
        /// The methods to expose as RPCs on the behaviour
        /// </summary>
        public List<ReflectionMethodData> trackedMethods
        {
            get => _trackedMethods;
            set => _trackedMethods = value;
        }

        /// <summary>
        /// The tracked behaviour instance
        /// </summary>
        public Object trackedBehaviour => _trackedBehaviour;

        private void Awake()
        {
            if (_initialized)
                return;
            _initialized = true;
            
            if (_trackedBehaviour == null)
            {
                PurrLogger.LogError("Tracked behaviour is null, aborting", this);
                return;
            }

            _reflectedValues = new ReflectedValue[_trackedFields.Count];

            for (var i = 0; i < _trackedFields.Count; i++)
            {
                var value = new ReflectedValue(_trackedBehaviour, _trackedFields[i]);
                _reflectedValues[i] = value;
            }

            InitMethodTracking();
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            if(_reflectedValues == null)
                Awake();
            
            for (var i = 0; i < _reflectedValues.Length; i++)
            {
                var reflectedValue = _reflectedValues[i];
                SendMemberUpdate(i, reflectedValue.lastValue);
            }
        }

        public void OnTick(float delta)
        {
            if (!IsController(_ownerAuth)) return;

            if (!_trackedBehaviour || _reflectedValues == null)
                return;

            for (var i = 0; i < _reflectedValues.Length; i++)
            {
                var reflectedValue = _reflectedValues[i];
                if (reflectedValue.Update())
                    SendMemberUpdate(i, reflectedValue.lastValue);
            }
        }

        private void SendMemberUpdate(int index, object data)
        {
            if (isServer)
                ObserversRpc(index, data);
            else ForwardThroughServer(index, data);
        }

        [ServerRpc]
        private void ForwardThroughServer(int index, object data)
        {
            if (_ownerAuth)
                ObserversRpc(index, data);
        }

        [ObserversRpc]
        private void ObserversRpc(int index, object value)
        {
            if (IsController(_ownerAuth))
                return;

            if (index < 0 || index >= _reflectedValues.Length)
            {
                PurrLogger.LogError($"Invalid index {index} on {name}", this);
                return;
            }

            var reflectedValue = _reflectedValues[index];

            if (reflectedValue.valueType == null)
            {
                PurrLogger.LogError($"Invalid type on {name} for {reflectedValue.name}", this);
                return;
            }

            reflectedValue.SetValue(value);
        }
    }
}
