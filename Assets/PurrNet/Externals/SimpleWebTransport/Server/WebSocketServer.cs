using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace JamesFrowen.SimpleWeb
{
    public class WebSocketServer
    {
        public readonly ConcurrentQueue<Message> receiveQueue = new ConcurrentQueue<Message>();

        readonly TcpConfig tcpConfig;
        readonly int maxMessageSize;

        TcpListener listener;
        Thread acceptThread;
        volatile bool serverStopped;
        readonly ServerHandshake handShake;
        readonly ServerSslHelper sslHelper;
        readonly BufferPool bufferPool;
        readonly ConcurrentDictionary<int, Connection> connections = new ConcurrentDictionary<int, Connection>();

        // Accepted connections that have not completed the handshake yet
        readonly ConcurrentDictionary<Connection, byte> pendingConnections = new ConcurrentDictionary<Connection, byte>();

        // guards the serverStopped check and Connected publish against Stop()
        readonly object publishLock = new object();

        int _idCounter = 0;

        public WebSocketServer(TcpConfig tcpConfig, int maxMessageSize, int handshakeMaxSize, SslConfig sslConfig,
            BufferPool bufferPool)
        {
            this.tcpConfig = tcpConfig;
            this.maxMessageSize = maxMessageSize;
            sslHelper = new ServerSslHelper(sslConfig);
            this.bufferPool = bufferPool;
            handShake = new ServerHandshake(this.bufferPool, handshakeMaxSize);
        }

        public void Listen(int port, bool forceIpv4)
        {
            listener = forceIpv4 ? new TcpListener(IPAddress.Parse("0.0.0.0"), port) : TcpListener.Create(port);
            listener.Start();

            Log.Info($"Server has started on port {port}");

            acceptThread = new Thread(acceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Start();
        }

        public void Stop()
        {
            lock (publishLock)
            {
                serverStopped = true;
            }

            // Interrupt then stop so that Exception is handled correctly
            acceptThread?.Interrupt();
            listener?.Stop();
            acceptThread = null;

            Log.Info("Server stopped, Closing all connections...");
            // make copy so that foreach doesn't break if values are removed
            Connection[] connectionsCopy = connections.Values.ToArray();
            foreach (Connection conn in connectionsCopy)
                conn.Dispose();

            // connections that are still handshaking, closing the socket unblocks their receive thread
            Connection[] pendingCopy = pendingConnections.Keys.ToArray();
            foreach (Connection conn in pendingCopy)
                conn.Dispose();

            connections.Clear();
            pendingConnections.Clear();
        }

        void acceptLoop()
        {
            try
            {
                try
                {
                    while (true)
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        tcpConfig.ApplyTo(client);

                        // the connection is tracked in pendingConnections by HandshakeAndReceiveLoop
                        var conn = new Connection(client, AfterConnectionDisposed);
                        Log.Info($"A client connected {conn}");

                        // handshake needs its own thread as it needs to wait for message from client
                        var receiveThread = new Thread(() => HandshakeAndReceiveLoop(conn));

                        conn.receiveThread = receiveThread;

                        receiveThread.IsBackground = true;
                        receiveThread.Start();
                    }
                }
                catch (SocketException)
                {
                    // check for Interrupted/Abort
                    Utils.CheckForInterupt();
                    throw;
                }
            }
            catch (ThreadInterruptedException e)
            {
                Log.InfoException(e);
            }
            catch (ThreadAbortException e)
            {
                Log.InfoException(e);
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
        }

        void HandshakeAndReceiveLoop(Connection conn)
        {
            try
            {
                pendingConnections.TryAdd(conn, 0);

                // Stop() was called while this connection was being accepted
                if (serverStopped)
                    return;

                bool success = sslHelper.TryCreateStream(conn);
                if (!success)
                {
                    Log.Error($"Failed to create SSL Stream {conn}");
                    conn.Dispose();
                    return;
                }

                success = handShake.TryHandshake(conn);

                if (success)
                {
                    Log.Info($"Sent Handshake {conn}");
                }
                else
                {
                    Log.Error($"Handshake Failed {conn}");
                    conn.Dispose();
                    return;
                }

                lock (publishLock)
                {
                    if (serverStopped) return;
                    conn.connId = Interlocked.Increment(ref _idCounter);
                    connections.TryAdd(conn.connId, conn);
                    pendingConnections.TryRemove(conn, out _);
                    receiveQueue.Enqueue(new Message(conn.connId, EventType.Connected));
                }

                if (serverStopped)
                    return;

                var sendThread = new Thread(() =>
                {
                    var sendConfig = new SendLoop.Config(
                        conn,
                        bufferSize: Constants.HeaderSize + maxMessageSize,
                        setMask: false);

                    SendLoop.Loop(sendConfig);
                });

                conn.sendThread = sendThread;
                sendThread.IsBackground = true;
                sendThread.Name = $"SendLoop {conn.connId}";
                sendThread.Start();

                var receiveConfig = new ReceiveLoop.Config(
                    conn,
                    maxMessageSize,
                    expectMask: true,
                    receiveQueue,
                    bufferPool);

                ReceiveLoop.Loop(receiveConfig);
            }
            catch (ThreadInterruptedException e)
            {
                Log.InfoException(e);
            }
            catch (ThreadAbortException e)
            {
                Log.InfoException(e);
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
            finally
            {
                // close here in case connect fails
                conn.Dispose();
            }
        }

        void AfterConnectionDisposed(Connection conn)
        {
            pendingConnections.TryRemove(conn, out _);

            if (conn.connId != Connection.IdNotSet)
            {
                receiveQueue.Enqueue(new Message(conn.connId, EventType.Disconnected));
                connections.TryRemove(conn.connId, out Connection _);
            }
        }

        public void Send(int id, ArrayBuffer buffer)
        {
            if (connections.TryGetValue(id, out Connection conn))
            {
                conn.sendQueue.Enqueue(buffer);
                conn.sendPending.Set();
            }
            else
            {
                Log.Warn(
                    $"Cant send message to {id} because connection was not found in dictionary. Maybe it disconnected.");
            }
        }

        public bool CloseConnection(int id)
        {
            if (connections.TryGetValue(id, out Connection conn))
            {
                Log.Info($"Kicking connection {id}");
                conn.Dispose();
                return true;
            }
            else
            {
                Log.Warn($"Failed to kick {id} because id not found");

                return false;
            }
        }

        public string GetClientAddress(int id)
        {
            if (!connections.TryGetValue(id, out Connection conn))
            {
                Log.Error($"Cant get address of connection {id} because connection was not found in dictionary");
                return null;
            }

            return conn.remoteAddress;
        }

        public Request GetClientRequest(int id)
        {
            if (!connections.TryGetValue(id, out Connection conn))
            {
                Log.Error($"Cant get request of connection {id} because connection was not found in dictionary");
                return null;
            }

            return conn.request;
        }
    }
}
