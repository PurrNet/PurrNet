using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace PurrNet.Tests
{
    public class WebSocketServerDisconnectTests
    {
        /// <summary>
        /// Closing a connection interrupted its send and receive threads (Thread.Interrupt), the calling thread
        /// included. The interrupt is thrown at the thread's next wait or contended lock: inside the catch block
        /// logging the IOException of a client that just vanished, or inside Dispose while it queues the
        /// disconnect event. It then escaped the thread as an unhandled exception (which ends a .NET process),
        /// or swallowed the disconnect event and left a connection the server never reports gone.
        /// Here clients drop while the server is writing to them.
        /// </summary>
        [Test, Timeout(120000)]
        public void ClientsDroppingWhileTheServerWritesToThemAreAllReportedAndNothingEscapes()
        {
            // The IOExceptions of vanished clients are logged; they are expected here.
            LogAssert.ignoreFailingMessages = true;
            using var fixture = new WebSocketServerFixture { payloadToEveryone = new byte[8 * 1024] };

            for (var round = 0; round < 25; round++)
            {
                var sockets = Enumerable.Range(0, 24).Select(_ => new ClientWebSocket()).ToList();
                WebSocketServerFixture.Run(async () =>
                {
                    await Task.WhenAll(sockets.Select(s => s.ConnectAsync(fixture.uri, CancellationToken.None)));
                    return true;
                });
                Assert.IsTrue(WebSocketServerFixture.WaitUntil(() => fixture.connected.Count >= sockets.Count, 10),
                    $"round {round}: {fixture.connected.Count} of {sockets.Count} connections reported");
                Thread.Sleep(20); // let the server's writes pile up

                foreach (var s in sockets)
                {
                    s.Abort(); // the client's network goes away mid-write
                    s.Dispose();
                }

                Assert.IsTrue(WebSocketServerFixture.WaitUntil(() => fixture.connected.IsEmpty, 10),
                    $"round {round}: {fixture.connected.Count} dropped connections were never reported gone");
                Assert.IsEmpty(fixture.unhandled, string.Join("\n", fixture.unhandled));
            }
        }
    }
}
