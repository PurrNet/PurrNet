using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using JamesFrowen.SimpleWeb;

namespace PurrNet.Tests
{
    /// <summary>
    /// A SimpleWebServer on a free port, its message queue pumped on a thread of its own, the way a game server's
    /// update loop pumps it. Records every unhandled exception the process sees while it runs.
    /// </summary>
    internal sealed class WebSocketServerFixture : IDisposable
    {
        public readonly SimpleWebServer server;
        public readonly int port;
        public readonly ConcurrentDictionary<int, bool> connected = new ConcurrentDictionary<int, bool>();
        public readonly ConcurrentQueue<string> unhandled = new ConcurrentQueue<string>();
        public byte[] payloadToEveryone;

        readonly CancellationTokenSource _stop = new CancellationTokenSource();
        readonly Thread _pump;

        public WebSocketServerFixture()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;

            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();

            server = new SimpleWebServer(int.MaxValue, new TcpConfig(true, 0, 0), ushort.MaxValue, 5000,
                new SslConfig(false, null, null, SslProtocols.None));
            server.onConnect += id => connected[id] = true;
            server.onDisconnect += id => connected.TryRemove(id, out _);
            server.Start((ushort)port, true);

            _pump = new Thread(Pump) { IsBackground = true, Name = "WebSocketServerFixture pump" };
            _pump.Start();
        }

        public Uri uri => new Uri($"ws://127.0.0.1:{port}/");

        void OnUnhandled(object sender, UnhandledExceptionEventArgs e) =>
            unhandled.Enqueue($"{Thread.CurrentThread.Name}: {e.ExceptionObject}");

        void Pump()
        {
            while (!_stop.IsCancellationRequested)
            {
                server.ProcessMessageQueue();
                var payload = payloadToEveryone;
                if (payload != null)
                {
                    foreach (var id in connected.Keys)
                        server.SendOne(id, new ArraySegment<byte>(payload));
                }
                Thread.Sleep(1);
            }
        }

        public static bool WaitUntil(Func<bool> condition, double seconds)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed.TotalSeconds > seconds)
                    return false;
                Thread.Sleep(10);
            }
            return true;
        }

        /// <summary>Runs async client code off the test's synchronization context and waits for it.</summary>
        public static T Run<T>(Func<Task<T>> body) => Task.Run(body).GetAwaiter().GetResult();

        public void Dispose()
        {
            _stop.Cancel();
            _pump.Join();
            server.Stop();
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        }
    }
}
