using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.Serialization;

namespace PurrNet.StateMachine
{
    [AddComponentMenu("PurrNet/State Machine")]
    [DefaultExecutionOrder(-1000)]
    public sealed class StateMachine : NetworkBehaviour
    {
        [FormerlySerializedAs("ownerAuth")] 
        [SerializeField] private bool _ownerAuth = false;

        [Obsolete("User ownerAuth")]
        public bool OwnerAuth => _ownerAuth;
        public bool ownerAuth => _ownerAuth;

        [SerializeField] private List<StateNode> _states = new();
        private SyncList<StateNode> _syncedStates;

        public IReadOnlyList<StateNode> states => _syncedStates.ToList();

        /// <summary>
        /// Invoked for clients when receiving changes to the state machine from the server
        /// </summary>
        public event Action onReceivedNewData;

        /// <summary>
        /// Invoked for both server and client when state changes
        /// </summary>
        public event StateChangedDelegate onStateChanged;
        public delegate void StateChangedDelegate(StateNode previousState, StateNode newState);

        private Queue<Action> _stateChangeQueue = new();
        
        private Queue<IStateCommand> _stateCommandQueue = new();
        StateMachineState _currentState;
        private int _previousStateId = -1;

        public StateMachineState currentState => _currentState;
        public int previousStateId => _previousStateId;

        public StateNode currentStateNode => _currentState.stateId < 0 || _currentState.stateId >= _syncedStates.Count
            ? null
            : _syncedStates[_currentState.stateId];
        
        public StateNode previousStateNode => _previousStateId < 0 || _previousStateId >= _syncedStates.Count
            ? null
            : _syncedStates[_previousStateId];

        private bool _initialized;

        private void Awake()
        {
            _syncedStates = new SyncList<StateNode>(_states, _ownerAuth);
            _syncedStates.onChanged += OnSyncedStateListChanged;
            _currentState.stateId = -1;

            for (var i = 0; i < _syncedStates.Count; i++)
            {
                var state = _syncedStates[i];
                state.Setup(this);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _syncedStates.onChanged -= OnSyncedStateListChanged;
        }

        private void Update()
        {
            if (_currentState.stateId < 0 || _currentState.stateId >= _syncedStates.Count)
                return;

            var node = _syncedStates[_currentState.stateId];
            if(isServer)
                node.StateUpdate(true);
            if(isClient)
                node.StateUpdate(false);
            node.StateUpdate();
        }
        
        void LateUpdate()
        {
            while (_stateCommandQueue.Count > 0)
            {
                var command = _stateCommandQueue.Dequeue();
                command.Execute();
            }
        }

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned();

            if (asServer && networkManager.isPlannedHost)
                return;
            
            if (!IsController(_ownerAuth))
                return;

            if (_initialized)
                return;

            if (_syncedStates.Count > 0)
                SetState(_syncedStates[0]);

            _initialized = true;
        }

        protected override void OnDespawned(bool asServer)
        {
            base.OnDespawned(asServer);

            if (_currentState.stateId < 0 || _currentState.stateId >= _syncedStates.Count)
                return;
            
            _states[_currentState.stateId].Exit(asServer);
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            
            if (_currentState.stateId < 0 || _currentState.stateId >= _syncedStates.Count)
                return;
            
            _states[_currentState.stateId].Exit();
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            base.OnObserverAdded(player);
            
            if (!isServer)
                return;
            
            if (_currentState.stateId < 0 || _currentState.stateId >= _syncedStates.Count)
                return;
            
            var stateNode = _syncedStates[_currentState.stateId];
            SendStateToObserver(player, stateNode);
        }

        private void SendStateToObserver(PlayerID player, StateNode stateNode)
        {
            Type dataType = GetDataType(_currentState.stateId);
    
            if (dataType != null && _currentState.data != null && dataType.IsInstanceOfType(_currentState.data))
            {
                RpcStateChange_Target(player, _currentState, true, _currentState.data);
            }
            else
            {
                RpcStateChange_Target<ushort>(player, _currentState, false, 0);
            }
        }

        public Type GetDataType(int stateId)
        {
            if (stateId < 0 || stateId >= _syncedStates.Count)
                return null;

            var node = _syncedStates[stateId];
            var type = node.GetType();
            var generics = type.BaseType!.GenericTypeArguments;

            return generics.Length == 0 ? null : generics[0];
        }

        /// <summary>
        /// Checks whether the given type T is the state we are currently in
        /// </summary>
        public bool IsCurrentState<T>() where T : StateNode
        {
            return currentStateNode is T;
        }

        /// <summary>
        /// Checks whether the given type T is the state we are currently in.
        /// Also outputs the instance of the state with the type
        /// </summary>
        public bool IsCurrentState<T>(out T stateInstance) where T : StateNode
        {
            if (currentStateNode is T state)
            {
                stateInstance = state;
                return true;
            }

            stateInstance = null;
            return false;
        }
        
        /// <summary>
        /// Checks whether the given stateToCheck is the state instance we are currently in
        /// </summary>
        public bool IsCurrentState(StateNode stateToCheck)
        {
            return currentStateNode == stateToCheck;
        }

        /// <summary>
        /// Adds a state to the StateMachine
        /// </summary>
        /// <param name="state">The state to add</param>
        public void AddState(StateNode state)
        {
            if (!state)
            {
                PurrLogger.LogError($"Failed to add state | State is null");
                return;
            }
            
            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError($"Failed to add state {state.name}:{state.GetType().Name} | Only the controller can add states");
                return;
            }
            
            _syncedStates.Add(state);
            state.Setup(this);
        }

        /// <summary>
        /// Adds a state to the StateMachine at a specific index
        /// </summary>
        /// <param name="state">The state to add</param>
        /// <param name="index">The index to add it at</param>
        public void InsertState(StateNode state, int index)
        {
            if (!state)
            {
                PurrLogger.LogError($"Failed to insert state at index: {index} | State is null");
                return;
            }
            
            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError($"Failed to insert state at index: {index} | State: {state.name}:{state.GetType().Name} | Only the controller can add states");
                return;
            }
            
            _syncedStates.Insert(index, state);
            state.Setup(this);
        }

        /// <summary>
        /// Removes a state from the StateMachine
        /// </summary>
        /// <param name="state">State you want to remove</param>
        public bool RemoveState(StateNode state)
        {
            if (!state)
            {
                PurrLogger.LogError($"Failed to remove state | State is null");
                return false;
            }
            
            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError($"Failed to remove state {state.name}:{state.GetType().Name} | Only the controller can remove states");
                return false;
            }

            if (currentStateNode == state)
            {
                PurrLogger.LogError($"Failed to remove state {state.name}:{state.GetType().Name} | Cannot remove current state");
                return false;
            }
            
            return _syncedStates.Remove(state);
        }

        /// <summary>
        /// Removes a state from the StateMachine at a specific index
        /// </summary>
        /// <param name="index">The index at which you wish to remove a state</param>
        public void RemoveStateAt(int index)
        {
            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError($"Failed to remove state at index {index} | Only the controller can remove states");
                return;
            }
            
            if (_syncedStates[index] == currentStateNode)
            {
                PurrLogger.LogError($"Failed to remove state at index {index} | Cannot remove current state");
                return;
            }
            
            _syncedStates.RemoveAt(index);
        }
        
        private void OnSyncedStateListChanged(SyncListChange<StateNode> change)
        {
            _states = _syncedStates.ToList();

            if (change.operation == SyncListOperation.Insert && change.index <= _currentState.stateId)
                _currentState.stateId++;

            if (change.operation == SyncListOperation.Insert && change.index <= _previousStateId)
                _previousStateId++;

            if (change.operation == SyncListOperation.Removed)
            {
                if (change.index < _currentState.stateId)
                    _currentState.stateId--;
                else if (change.index == _currentState.stateId)
                    _currentState.stateId = -1;

                if (change.index < _previousStateId)
                    _previousStateId--;
                else if (change.index == _previousStateId)
                    _previousStateId = -1;
            }

            if (change.operation == SyncListOperation.Insert || 
                change.operation == SyncListOperation.Added || 
                change.operation == SyncListOperation.Set)
            {
                if(change.value)
                    change.value.Setup(this);
            }
        }

        [ServerRpc]
        private void RpcStateChange_Server<T>(StateMachineState state, bool hasData, T data)
        {
            RpcStateChange<T>(state, hasData, data);
        }

        [ObserversRpc(bufferLast: true)]
        private void RpcStateChange<T>(StateMachineState state, bool hasData, T data)
        {
            if (IsController(_ownerAuth)) return;
            
            if(_currentState.stateId > -1 && _syncedStates.Count > _currentState.stateId)
                UpdateStateId(_syncedStates[_currentState.stateId]);
            _currentState = state;
            _currentState.data = data;

            if (_currentState.stateId < 0 || _currentState.stateId >= _syncedStates.Count)
                return;

            var newState = _syncedStates[_currentState.stateId];
            var prevState = previousStateNode;

            try
            {
                _stateChangeQueue.Enqueue(() =>
                {
                    onStateChanged?.Invoke(prevState, newState);
                });
                
                if (hasData && newState is StateNode<T> node)
                {
                    if(isServer)
                        node.Enter(data, true);
                    if(isClient)
                        node.Enter(data, false);
                    node.Enter(data);
                }
                else
                {
                    if(isServer)
                        newState.Enter(true);
                    if(isClient)
                        newState.Enter(false);
                    newState.Enter();
                }
            }
            catch(Exception e)
            {
                PurrLogger.LogException(e);
            }

            HandleStateChangeQueue();
            onReceivedNewData?.Invoke();
        }

        private void HandleStateChangeQueue()
        {
            if (_stateChangeQueue.Count == 0)
                return;

            while (_stateChangeQueue.Count > 0)
            {
                var del = _stateChangeQueue.Dequeue();
                del.Invoke();
            }
        }

        [TargetRpc]
        private void RpcStateChange_Target<T>(PlayerID target, StateMachineState state, bool hasData, T data)
        {
            if (IsController(_ownerAuth)) return;

            _currentState = state;
            _currentState.data = data;

            if (_currentState.stateId < 0 || _currentState.stateId >= _syncedStates.Count)
                return;

            HandleStateChangeQueue();
            onReceivedNewData?.Invoke();
        }

        private void UpdateStateId(StateNode node)
        {
            var idx = node == null ? -2 : _syncedStates.IndexOf(node);

            if (idx == -1)
                PurrLogger.LogException($"State '{node.name}' of type {node.GetType().Name} not in states list");

            var newStateId = idx < 0 ? -1 : idx;

            var oldState = _currentState.stateId < 0 || _currentState.stateId >= _syncedStates.Count
                ? null
                : _syncedStates[_currentState.stateId];

            try
            {
                if (oldState)
                {
                    if(isServer)
                        oldState.Exit(true);
                    if (isClient)
                        oldState.Exit(false);
                    oldState.Exit();
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }

            _previousStateId = _currentState.stateId;
            _currentState.stateId = newStateId;
        }

        /// <summary>
        /// Goes to a specific state in the StateMachine list
        /// </summary>
        /// <param name="state">Reference to the state you want to go to</param>
        /// <param name="data">Data to send with the state</param>
        /// <param name="force">Whether to skip the CanEnter and CanExit checks</param>
        /// <typeparam name="T">Your data type</typeparam>
        public bool SetState<T>(StateNode<T> state, T data, bool force = false)
        {
            if (!force && TryEvaluateTransition(state, data) != StateTransitionStatus.Success)
                return false;
            
            _stateCommandQueue.Enqueue(new GenericStateCommand<T>(state, data, SetStateInternal));
            return true;
        }

        private void SetStateInternal<T>(StateNode<T> state, T data)
        {
            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError(
                    $"Only the controller can set state. Non-owner tried to set state to {state.name}:{state.GetType().Name} | OwnerAuth: {_ownerAuth}"
                );
                return;
            }

            UpdateStateId(state);
            _currentState.data = data;
            
            var newState = _syncedStates[_currentState.stateId];
            var prevState = previousStateNode;

            if (isServer)
                RpcStateChange(_currentState, true, data);
            else
                RpcStateChange_Server(_currentState, true, data);

            try
            {
                if (state)
                {
                    _stateChangeQueue.Enqueue(() =>
                    {
                        onStateChanged?.Invoke(prevState, newState);
                    });
                    if(isServer)
                        state.Enter(data, true);
                    if(isClient)
                        state.Enter(data, false);
                    state.Enter(data);
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }

            HandleStateChangeQueue();
        }

        /// <summary>
        /// Goes to a specific state in the StateMachine list
        /// </summary>
        /// <param name="state">Reference to the state you want to go to</param>
        /// <param name="force">Whether to skip the CanEnter and CanExit checks</param>
        public bool SetState(StateNode state, bool force = false)
        {
            if (!force && TryEvaluateTransition(state) != StateTransitionStatus.Success)
                return false;
            
            _stateCommandQueue.Enqueue(new StateCommand(state, SetStateInternal));
            return true;
        }

        private void SetStateInternal(StateNode state)
        {
            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogError(
                    $"Only the controller can set state. Non-owner tried to set state to {state.name}:{state.GetType().Name} | OwnerAuth: {_ownerAuth}"
                );
                return;
            }

            UpdateStateId(state);
            _currentState.data = null;

            var newState = _syncedStates[_currentState.stateId];
            var prevState = previousStateNode;
            
            if (isServer)
                RpcStateChange<ushort>(_currentState, false, 0);
            else
                RpcStateChange_Server<ushort>(_currentState, false, 0);

            try
            {
                if (state)
                {
                    _stateChangeQueue.Enqueue(() =>
                    {
                        onStateChanged?.Invoke(prevState, newState);
                    });
                    if(isServer)
                        state.Enter(true);
                    if(isClient)
                        state.Enter(false);
                    state.Enter();
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }

            HandleStateChangeQueue();
        }

        /// <summary>
        /// Takes the state machine to the next state in the states list
        /// </summary>
        /// <param name="data">Data to send with the state</param>
        /// <param name="force">Whether to skip the CanEnter and CanExit checks</param>
        /// <typeparam name="T">The type of your data</typeparam>
        public bool Next<T>(T data, bool force = false)
        {
            var startId = _currentState.stateId;
            var nextNodeId = GetNextId(startId);

            if (_syncedStates[nextNodeId] is StateNode<T> node)
                return SetState(node, data, force);
            
            PurrLogger.LogException($"Node {_syncedStates[nextNodeId].name}:{_syncedStates[nextNodeId].GetType().Name} does not have a generic type argument of type {typeof(T).Name}");
            return false;
        }

        /// <summary>
        /// Will continue to the next state in the states list until it finds a state that can be entered
        /// </summary>
        /// <param name="data">Data utilized to enter next state</param>
        /// <typeparam name="T">The type of your data</typeparam>
        /// <returns>Whether it successfully found any state that is valid to enter</returns>
        public bool NextValid<T>(T data)
        {
            if (currentStateNode != null && !currentStateNode.CanExit())
                return false;
            
            var startId = _currentState.stateId;
            var nextNodeId = GetNextId(startId);

            do
            {
                var node = _syncedStates[nextNodeId];
                if (node is StateNode<T> genericNode && SetState(genericNode, data))
                    return true;

                nextNodeId = GetNextId(nextNodeId);
            }
            while (nextNodeId != startId);

            return false;
        }

        /// <summary>
        /// Takes the state machine to the next state in the states list
        /// </summary>
        /// <param name="force">Whether to skip the CanEnter and CanExit checks</param>
        public bool Next(bool force = false)
        {
            var startId = _currentState.stateId;
            var nextNodeId = GetNextId(startId);

            return SetState(_syncedStates[nextNodeId], force);
        }
        
        /// <summary>
        /// Will continue to the next state in the states list until it finds a state that can be entered
        /// </summary>
        /// <returns>Whether it successfully found any state that is valid to enter</returns>
        public bool NextValid()
        {
            if (currentStateNode != null && !currentStateNode.CanExit())
                return false;
            
            var startId = _currentState.stateId;
            var nextNodeId = GetNextId(startId);

            do
            {
                if (SetState(_syncedStates[nextNodeId]))
                    return true;

                nextNodeId = GetNextId(nextNodeId);
            }
            while (nextNodeId != startId);

            return false;
        }

        private int GetNextId(int currentId)
        {
            var nextNodeId = currentId + 1;
            if (nextNodeId >= _syncedStates.Count)
                nextNodeId = 0;
            return nextNodeId;
        }

        /// <summary>
        /// Takes the state machine to the previous state in the states list
        /// </summary>
        public bool Previous(bool force = false)
        {
            var prevNodeId = _currentState.stateId - 1;
            if (prevNodeId < 0)
                prevNodeId = _syncedStates.Count - 1;

            return SetState(_syncedStates[prevNodeId], force);
        }
        
        /// <summary>
        /// Will continue to the previous state in the states list until it finds a state that can be entered
        /// </summary>
        public bool PreviousValid()
        {
            if (currentStateNode != null && !currentStateNode.CanExit())
                return false;

            var startId = _currentState.stateId;
            var prevNodeId = GetPreviousId(startId);

            do
            {
                if (SetState(_syncedStates[prevNodeId]))
                    return true;

                prevNodeId = GetPreviousId(prevNodeId);
            }
            while (prevNodeId != startId);

            return false;
        }

        /// <summary>
        /// Takes the state machine to the previous state in the states list
        /// </summary>
        /// <param name="data">Data to send to the previous state</param>
        /// <param name="force">Whether to skip the CanEnter and CanExit checks</param>
        /// <typeparam name="T">The type of your data</typeparam>
        public bool Previous<T>(T data, bool force = false)
        {
            var prevNodeId = _currentState.stateId - 1;
            if (prevNodeId < 0)
                prevNodeId = _syncedStates.Count - 1;

            var prevNode = _syncedStates[prevNodeId];

            if (prevNode is StateNode<T> stateNode)
            {
                return SetState(stateNode, data, force);
            }
            PurrLogger.LogException(
                $"Node {prevNode.name}:{prevNode.GetType().Name} does not have a generic type argument of type {typeof(T).Name}");
            return false;
        }
        
        /// <summary>
        /// Will continue to the previous state in the states list until it finds a state that can be entered
        /// </summary>
        /// <param name="data">Data to send to the previous state</param>
        /// <typeparam name="T">The type of your data</typeparam>
        public bool PreviousValid<T>(T data)
        {
            if (currentStateNode != null && !currentStateNode.CanExit())
                return false;

            var startId = _currentState.stateId;
            var prevNodeId = GetPreviousId(startId);

            do
            {
                var node = _syncedStates[prevNodeId];
                if (node is StateNode<T> genericNode && SetState(genericNode, data))
                    return true;

                prevNodeId = GetPreviousId(prevNodeId);
            }
            while (prevNodeId != startId);

            return false;
        }
        
        private int GetPreviousId(int currentId)
        {
            var prevNodeId = currentId - 1;
            if (prevNodeId < 0)
                prevNodeId = _syncedStates.Count - 1;
            return prevNodeId;
        }
        
        internal enum StateTransitionStatus
        {
            Success,
            InvalidState,
            CannotExit,
            CannotEnter,
            WrongGenericType
        }

        internal StateTransitionStatus TryEvaluateTransition(StateNode to)
        {
            if (to == null) return StateTransitionStatus.InvalidState;
            if (currentStateNode != null && !currentStateNode.CanExit()) return StateTransitionStatus.CannotExit;
            if (!to.CanEnter()) return StateTransitionStatus.CannotEnter;
            return StateTransitionStatus.Success;
        }

        internal StateTransitionStatus TryEvaluateTransition<T>(StateNode<T> to, T data)
        {
            if (to == null) return StateTransitionStatus.InvalidState;
            if (currentStateNode != null && !currentStateNode.CanExit()) return StateTransitionStatus.CannotExit;
            if (!to.CanEnter() || !to.CanEnter(data)) return StateTransitionStatus.CannotEnter;
            return StateTransitionStatus.Success;
        }
    }

    public struct StateMachineState : IPacked
    {
        public int stateId;
        public object data;

        public void Write(BitPacker packer)
        {
            Packer<int>.Write(packer, stateId);
            Packer<object>.Write(packer, data);
        }

        public void Read(BitPacker packer)
        {
            Packer<int>.Read(packer, ref stateId);
            Packer<object>.Read(packer, ref data);
        }
    }
    
    public interface IStateCommand
    {
        void Execute();
    }
    
    internal struct GenericStateCommand<T> : IStateCommand
    {
        private StateNode<T> state;
        private T data;
        private Action<StateNode<T>, T> setStateMethod;

        public GenericStateCommand(StateNode<T> state, T data, Action<StateNode<T>, T> setStateMethod)
        {
            this.state = state;
            this.data = data;
            this.setStateMethod = setStateMethod;
        }

        public void Execute()
        {
            setStateMethod(state, data);
        }
    }

    internal struct StateCommand : IStateCommand
    {
        private StateNode state;
        private Action<StateNode> setStateMethod;

        public StateCommand(StateNode state, Action<StateNode> setStateMethod)
        {
            this.state = state;
            this.setStateMethod = setStateMethod;
        }

        public void Execute()
        {
            setStateMethod(state);
        }
    }
}