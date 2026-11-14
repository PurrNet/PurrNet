using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace PurrNet.Essentials.Relay
{
    public abstract class RelaySession
    {
        internal const byte ServerClientConnected = 0;
        internal const byte ServerClientDisconnected = 1;
        internal const byte ServerClientData = 2;
        internal const byte ServerAuthenticated = 3;
        internal const byte ServerAuthenticationFailed = 4;

        private const int Attempts = 10;
        private const float RetrySeconds = 1f;

        [Serializable]
        private struct Authenticate
        {
            public string roomName;
            public string clientSecret;
            public bool nat;
            public bool webRtcP2P;
        }

        private struct Delay
        {
            public float due;
            public TaskCompletionSource<bool> completion;
        }

        private readonly List<Delay> _delays = new List<Delay>();
        private RelayState _state = RelayState.Disconnected;
        private int _run;
        private byte[] _sendBuffer = new byte[2048];

        internal RelayLink link { get; private set; }

        protected RelayJoinDetails details { get; private set; }

        public RelayOptions options { get; }

        public RelayState state => _state;

        public string relayRoomName { get; private set; }

        public RelayBudget budget { get; private set; }

        public RelayCloseReason closeReason { get; private set; }

        public string lastError { get; private set; }

        public bool isUdp => link != null && link.isUdp;

        public int roundTripTime => link != null ? link.roundTripTime : -1;

        public event Action<RelayState> onStateChanged;

        protected RelaySession(RelayOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        protected abstract Task<(string host, RelayJoinDetails details)> Resolve(Func<bool> cancelled);

        protected abstract void OnData(ArraySegment<byte> data);

        protected virtual void OnClosed()
        {
        }

        public async void Start()
        {
            if (_state != RelayState.Disconnected)
                Stop();

            int run = ++_run;
            closeReason = RelayCloseReason.Stopped;
            lastError = null;
            SetState(RelayState.Connecting);

            try
            {
                var (host, resolved) = await Resolve(() => run != _run);
                if (run != _run)
                    return;

                details = resolved;
                relayRoomName = string.IsNullOrEmpty(resolved.roomName) ? options.roomName : resolved.roomName;
                if (resolved.usage != null && resolved.usage.isValid)
                    budget = resolved.usage;

                link = new RelayLink(options.timeoutSeconds);
                link.onConnected += SendAuthentication;
                link.onData += OnData;
                link.onDisconnected += OnLinkLost;
                link.onError += OnLinkError;

                await link.Connect(host, resolved, options.forceWebSocket || RelayLink.prefersWebSocket);
            }
            catch (RelayRefusedException e)
            {
                if (run != _run)
                    return;
                if (e.budget != null && e.budget.isValid)
                    budget = e.budget;
                Close(RelayCloseReason.Refused, e.Message);
            }
            catch (Exception e)
            {
                if (run != _run)
                    return;
                Close(RelayCloseReason.Error, e.Message);
            }
        }

        public void Stop()
        {
            Close(RelayCloseReason.Stopped, null);
        }

        public void Poll()
        {
            if (_delays.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                for (int i = _delays.Count - 1; i >= 0; i--)
                {
                    if (_delays[i].due > now)
                        continue;
                    var completion = _delays[i].completion;
                    _delays.RemoveAt(i);
                    completion.TrySetResult(true);
                }
            }

            link?.Poll();
        }

        public virtual void Update(float deltaSeconds)
        {
            link?.Update(deltaSeconds);
        }

        public void Flush()
        {
            link?.Flush();
        }

        public int GetMaxPacketSize(RelayDelivery delivery)
        {
            if (link == null)
                return 1024;
            return link.GetMaxPacketSize(delivery) - 16;
        }

        protected async Task<T> Retry<T>(Func<Task<T>> action, Func<bool> cancelled)
        {
            Exception last = null;

            for (int i = 0; i < Attempts; i++)
            {
                if (cancelled())
                    throw new OperationCanceledException();

                if (i > 0)
                    await Wait(RetrySeconds);

                if (cancelled())
                    throw new OperationCanceledException();

                try
                {
                    return await action();
                }
                catch (RelayRefusedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    last = e;
                }
            }

            throw last ?? new Exception("Failed to reach the relay.");
        }

        private Task Wait(float seconds)
        {
            var completion = new TaskCompletionSource<bool>();
            _delays.Add(new Delay { due = Time.realtimeSinceStartup + seconds, completion = completion });
            return completion.Task;
        }

        protected byte[] SendBuffer(int length)
        {
            if (_sendBuffer.Length < length)
                _sendBuffer = new byte[Math.Max(length, _sendBuffer.Length * 2)];
            return _sendBuffer;
        }

        protected void MarkConnected()
        {
            SetState(RelayState.Connected);
        }

        protected void Close(RelayCloseReason reason, string error)
        {
            _run++;

            for (int i = 0; i < _delays.Count; i++)
                _delays[i].completion.TrySetCanceled();
            _delays.Clear();

            if (link != null)
            {
                link.onConnected -= SendAuthentication;
                link.onData -= OnData;
                link.onDisconnected -= OnLinkLost;
                link.onError -= OnLinkError;
                link.Close();
                link = null;
            }

            if (_state == RelayState.Disconnected)
                return;

            closeReason = reason;
            lastError = error;
            OnClosed();
            SetState(RelayState.Disconnected);
        }

        private void SetState(RelayState value)
        {
            if (_state == value)
                return;

            _state = value;
            onStateChanged?.Invoke(value);
        }

        private void SendAuthentication()
        {
            var authenticate = new Authenticate
            {
                roomName = relayRoomName,
                clientSecret = details.secret
            };

            var data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(authenticate));
            link.Send(data, 0, data.Length, RelayDelivery.ReliableOrdered);
        }

        private void OnLinkLost()
        {
            Close(RelayCloseReason.ConnectionLost, null);
        }

        private void OnLinkError(Exception exception)
        {
            lastError = exception.Message;
        }
    }
}
