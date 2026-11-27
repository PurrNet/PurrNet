using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace JamesFrowen.SimpleWeb
{
    internal sealed class Connection : IDisposable
    {
        public const int IdNotSet = -1;
        private readonly object disposedLock = new object();

        public TcpClient client;

        public int connId = IdNotSet;

        /// <summary>
        /// Connect request, sent from client to start handshake
        /// <para>Only valid on server</para>
        /// </summary>
        public Request request;

        /// <summary>
        /// RemoteEndpoint address or address from request header
        /// <para>Only valid on server</para>
        /// </summary>
        public string remoteAddress;

        public Stream stream;
        public Thread receiveThread;
        public Thread sendThread;

        public ManualResetEventSlim sendPending = new ManualResetEventSlim(false);
        public ConcurrentQueue<ArrayBuffer> sendQueue = new ConcurrentQueue<ArrayBuffer>();

        public Action<Connection> onDispose;
        private volatile bool hasDisposed;
        public bool IsDisposed => hasDisposed;

        public Connection(TcpClient client, Action<Connection> onDispose)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.onDispose = onDispose;
        }

        /// <summary>
        /// disposes client and stops threads
        /// </summary>
        public void Dispose()
        {
            Log.Verbose($"Dispose {ToString()}");

            // check hasDisposed first to stop ThreadInterruptedException on lock
            if (hasDisposed) return;

            Log.Info($"Connection Close: {ToString()}");

            lock (disposedLock)
            {
                // check hasDisposed again inside lock to make sure no other object has called this
                if (hasDisposed) return;

                hasDisposed = true;

                // Wake the threads, never Interrupt them. An interrupt lands in whatever lock or wait the thread
                // reaches next (logging the IOException of a client that just vanished, this Dispose, the
                // disconnect event); escaping its handlers it is an unhandled exception, and caught in the wrong
                // place it lost the disconnect event (WebSocketServerDisconnectTests). The send loop waits on
                // sendPending and sees IsDisposed; the receive loop's blocking read ends when the stream is
                // disposed below.
                sendPending.Set();

                try
                {
                    // stream 
                    stream?.Dispose();
                    stream = null;
                    client.Dispose();
                    client = null;
                }
                catch (Exception e)
                {
                    Log.Exception(e);
                }

                // sendPending is not disposed: the send loop and WebSocketServer.Send may still set or wait on it
                // after this, and a disposed one throws there.

                // release all buffers in send queue
                while (sendQueue.TryDequeue(out ArrayBuffer buffer))
                    buffer.Release();

                onDispose.Invoke(this);
            }
        }

        public override string ToString()
        {
            if (hasDisposed)
                return $"[Conn:{connId}, Disposed]";
            else
                try
                {
                    System.Net.EndPoint endpoint = client?.Client?.RemoteEndPoint;
                    return $"[Conn:{connId}, endPoint:{endpoint}]";
                }
                catch (SocketException)
                {
                    return $"[Conn:{connId}, endPoint:n/a]";
                }
        }

        /// <summary>
        /// Gets the address based on the <see cref="request"/> and RemoteEndPoint
        /// <para>Called after ServerHandShake is accepted</para>
        /// </summary>
        internal string CalculateAddress()
        {
            if (request.Headers.TryGetValue("X-Forwarded-For", out string forwardFor))
            {
                string actualClientIP = forwardFor.ToString().Split(',').First();
                // Remove the port number from the address
                return actualClientIP.Split(':').First();
            }
            else
            {
                IPEndPoint ipEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
                IPAddress ipAddress = ipEndPoint.Address;
                if (ipAddress.IsIPv4MappedToIPv6)
                    ipAddress = ipAddress.MapToIPv4();

                return ipAddress.ToString();
            }
        }
    }
}