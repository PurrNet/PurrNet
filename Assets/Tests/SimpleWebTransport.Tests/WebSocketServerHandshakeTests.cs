using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PurrNet.Tests
{
    public class WebSocketServerHandshakeTests
    {
        /// <summary>
        /// Every connection is handshaken on its own thread, and they shared one SHA1 instance, which is not
        /// thread-safe: clients connecting at the same moment got a wrong Sec-WebSocket-Accept (often the hash of
        /// nothing, 2jmj7l5rSw0yVb/vlWAYkK/YBwk=) and refused the server.
        /// </summary>
        [Test, Timeout(60000)]
        public void ClientsConnectingAtTheSameMomentAllCompleteTheHandshake()
        {
            using var fixture = new WebSocketServerFixture();
            var sockets = new List<ClientWebSocket>();
            try
            {
                var failures = WebSocketServerFixture.Run(async () =>
                {
                    var failed = new List<string>();
                    for (var round = 0; round < 5; round++)
                    {
                        var batch = Enumerable.Range(0, 48).Select(_ => new ClientWebSocket()).ToList();
                        sockets.AddRange(batch);
                        await Task.WhenAll(batch.Select(async socket =>
                        {
                            try
                            {
                                await socket.ConnectAsync(fixture.uri, CancellationToken.None);
                            }
                            catch (WebSocketException e)
                            {
                                lock (failed) failed.Add(e.Message);
                            }
                        }));
                    }
                    return failed;
                });

                Assert.IsEmpty(failures,
                    $"{failures.Count} of {sockets.Count} handshakes failed, e.g. {failures.FirstOrDefault()}");
                Assert.IsEmpty(fixture.unhandled, string.Join("\n", fixture.unhandled));
            }
            finally
            {
                foreach (var socket in sockets)
                {
                    socket.Abort();
                    socket.Dispose();
                }
            }
        }
    }
}
