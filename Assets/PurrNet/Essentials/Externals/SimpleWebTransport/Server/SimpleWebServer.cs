using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JamesFrowen.SimpleWeb
{
    public class SimpleWebServer
    {
        public event Action<int> onConnect;
        public event Action<int> onDisconnect;
        public event Action<int, ArraySegment<byte>> onData;
        public event Action<int, Exception> onError;

        readonly int maxMessagesPerTick;
        readonly WebSocketServer server;
        readonly BufferPool bufferPool;

        public bool Active { get; private set; }

        public SimpleWebServer(int maxMessagesPerTick, TcpConfig tcpConfig, int maxMessageSize, int handshakeMaxSize,
            SslConfig sslConfig)
        {
            this.maxMessagesPerTick = maxMessagesPerTick;
            // use max because bufferpool is used for both messages and handshake
            int max = Math.Max(maxMessageSize, handshakeMaxSize);
            bufferPool = new BufferPool(5, 20, max);
            server = new WebSocketServer(tcpConfig, maxMessageSize, handshakeMaxSize, sslConfig, bufferPool);
        }

        public void Start(ushort port, bool forceIpv4)
        {
            server.Listen(port, forceIpv4);
            Active = true;
        }

        public void Stop()
        {
            server.Stop();
            Active = false;
        }

        /// <summary>
        /// Sends to a list of connections, use <see cref="List{int}"/> version to avoid foreach allocation
        /// </summary>
        /// <param name="connectionIds"></param>
        /// <param name="source"></param>
        public void SendAll(List<int> connectionIds, ArraySegment<byte> source)
        {
            ArrayBuffer buffer = bufferPool.Take(source.Count);
            buffer.CopyFrom(source);
            buffer.SetReleasesRequired(connectionIds.Count);

            // make copy of array before for each, data sent to each client is the same
            foreach (int id in connectionIds)
                server.Send(id, buffer);
        }

        /// <summary>
        /// Sends to a list of connections, use <see cref="ICollection{int}"/> version when you are using a non-list collection (will allocate in foreach)
        /// </summary>
        /// <param name="connectionIds"></param>
        /// <param name="source"></param>
        public void SendAll(ICollection<int> connectionIds, ArraySegment<byte> source)
        {
            ArrayBuffer buffer = bufferPool.Take(source.Count);
            buffer.CopyFrom(source);
            buffer.SetReleasesRequired(connectionIds.Count);

            // make copy of array before for each, data sent to each client is the same
            foreach (int id in connectionIds)
                server.Send(id, buffer);
        }

        /// <summary>
        /// Sends to a list of connections, use <see cref="IEnumerable{int}"/> version in cases where you want to use LINQ to get connections (will allocate from LINQ functions and foreach)
        /// </summary>
        /// <param name="connectionIds"></param>
        /// <param name="source"></param>
        public void SendAll(IEnumerable<int> connectionIds, ArraySegment<byte> source)
        {
            ArrayBuffer buffer = bufferPool.Take(source.Count);
            buffer.CopyFrom(source);
            buffer.SetReleasesRequired(connectionIds.Count());

            // make copy of array before for each, data sent to each client is the same
            foreach (int id in connectionIds)
                server.Send(id, buffer);
        }

        public void SendOne(int connectionId, ArraySegment<byte> source)
        {
            ArrayBuffer buffer = bufferPool.Take(source.Count);
            buffer.CopyFrom(source);
            server.Send(connectionId, buffer);
        }

        public bool KickClient(int connectionId) => server.CloseConnection(connectionId);

        public string GetClientAddress(int connectionId) => server.GetClientAddress(connectionId);

        public Request GetClientRequest(int connectionId) => server.GetClientRequest(connectionId);

        /// <summary>
        /// Processes all new messages
        /// </summary>
        public void ProcessMessageQueue()
        {
            ProcessMessageQueue(null);
        }

        /// <summary>
        /// Processes all messages while <paramref name="behaviour"/> is enabled
        /// </summary>
        /// <param name="behaviour"></param>
        public void ProcessMessageQueue(MonoBehaviour behaviour)
        {
            int processedCount = 0;
            bool skipEnabled = behaviour == null;
            // check enabled every time in case behaviour was disabled after data
            while (
                (skipEnabled || behaviour.enabled) &&
                processedCount < maxMessagesPerTick &&
                // Dequeue last
                server.receiveQueue.TryDequeue(out Message next)
            )
            {
                processedCount++;

                switch (next.type)
                {
                    case EventType.Connected:
                        onConnect?.Invoke(next.connId);
                        break;
                    case EventType.Data:
                        onData?.Invoke(next.connId, next.data.ToSegment());
                        next.data.Release();
                        break;
                    case EventType.Disconnected:
                        onDisconnect?.Invoke(next.connId);
                        break;
                    case EventType.Error:
                        onError?.Invoke(next.connId, next.exception);
                        break;
                }
            }

            if (server.receiveQueue.Count > 0)
            {
                Log.Warn($"SimpleWebServer ProcessMessageQueue has {server.receiveQueue.Count} remaining.");
            }
        }
    }
}
