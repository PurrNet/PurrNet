using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#if UNITY_WEB
using UnityEngine.Networking;
#endif
using System.Diagnostics;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PurrNet.Transports
{
    [UsedImplicitly]
    [Serializable]
    public struct RelayServer
    {
        public string apiEndpoint;
        public string host;
        public int restPort;
        [Obsolete("Use `udpPortV2` instead.")]
        public int udpPort;
        public int udpPortV2;
        public int webSocketsPort;
        public string region;
    }

    [UsedImplicitly]
    [Serializable]
    public struct Relayers
    {
        public RelayServer[] servers;
    }

    [UsedImplicitly]
    [Serializable]
    public class RelayUsage
    {
        [Serializable] public class Players { public int used; public int allowed; }
        [Serializable] public class Traffic { public long usedBytes; public long allowedBytes; public string month; }

        public Players players;
        public Traffic traffic;

        public bool isValid => players != null && traffic != null && !string.IsNullOrEmpty(traffic.month);

        public override string ToString()
        {
            if (!isValid) return "no budget";
            var playersText = players.allowed > 0 ? $"{players.used}/{players.allowed} players" : $"{players.used} players";
            var trafficText = traffic.allowedBytes > 0
                ? $"{Gigabytes(traffic.usedBytes)} / {Gigabytes(traffic.allowedBytes)} GB ({traffic.month})"
                : $"{Gigabytes(traffic.usedBytes)} GB ({traffic.month})";
            return $"{playersText} · {trafficText}";
        }

        static string Gigabytes(long bytes) => (bytes / 1073741824.0).ToString(bytes < 10L * 1073741824L ? "0.00" : "0.0");
    }

    public sealed class RelayRefusedException : Exception
    {
        public readonly string code;
        public readonly RelayUsage usage;

        public RelayRefusedException(string code, string message, RelayUsage usage) : base(message)
        {
            this.code = code;
            this.usage = usage;
        }
    }

    [UsedImplicitly]
    [Serializable]
    public struct HostJoinInfo
    {
        public bool ssl;
        public string secret;
        public int port;
        public string roomName;
        public RelayUsage usage;
        /// <summary>Optional WebRTC signaling endpoint. Older relays leave this empty.</summary>
        public string webRtcUrl;
        [Obsolete]
        public int udpPort;
        public int udpPortV2;
        /// <summary>The room's host migration generation; see <see cref="hasMigrationGeneration"/>.</summary>
        public int migrationGeneration;
        /// <summary>False for relays that do not report a generation.</summary>
        [NonSerialized] public bool hasMigrationGeneration;
    }

    /// <summary>
    /// The relay refused a host migration request for a reason the caller can act on. Known codes:
    /// <c>host_connected</c> (a claim without authorization while the host is still connected; for a lost host the
    /// relay has not noticed yet), <c>generation_mismatch</c> (someone else claimed first), <c>invalid_secret</c>
    /// (not a secret of the room, or an authorization that was used, expired or issued for another player) and
    /// <c>invalid_host_secret</c>.
    /// </summary>
    public sealed class RelayMigrationRejectedException : Exception
    {
        public readonly string code;

        /// <summary>The room's migration generation when the relay refused, when it reported one.</summary>
        public readonly int? generation;

        public RelayMigrationRejectedException(string code, string message, int? generation = null) : base(message)
        {
            this.code = code;
            this.generation = generation;
        }
    }

    [UsedImplicitly]
    [Serializable]
    public struct ClientJoinInfo
    {
        public bool ssl;
        public string secret;
        public string host;
        public int port;
        /// <summary>See <see cref="HostJoinInfo.roomName"/>.</summary>
        public string roomName;
        public RelayUsage usage;
        /// <summary>Optional WebRTC signaling endpoint. Older relays leave this empty.</summary>
        public string webRtcUrl;
        [Obsolete]
        public int udpPort;
        public int udpPortV2;
        /// <summary>The room's host migration generation; see <see cref="hasMigrationGeneration"/>.</summary>
        public int migrationGeneration;
        /// <summary>False for relays that do not report a generation.</summary>
        [NonSerialized] public bool hasMigrationGeneration;
    }

    /// <summary>A relay room's host migration state, as the relay reports it.</summary>
    [UsedImplicitly]
    [Serializable]
    public struct MigrationJoinInfo
    {
        public bool ssl;
        /// <summary>The host secret after a claim; the client secret when read with the current state.</summary>
        public string secret;
        public string host;
        public int port;
        public int udpPortV2;
        public string roomName;
        /// <summary>Increases with every claim; pass the value you saw to make a claim fail if someone else claimed first.</summary>
        public int generation;
        public string fencingToken;
        public string promotedPlayerId;
    }

    public static class PurrTransportUtils
    {
        static async Task<string> Get([UsedImplicitly] string url)
        {
#if UNITY_WEB
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Cache-Control", "no-cache");
            request.useHttpContinue = false;
            var response = await request.SendWebRequest();
            return response.webRequest.downloadHandler.text;

#else
            throw new NotSupportedException("You need the `com.unity.modules.unitywebrequest` package to use this.");
#endif
        }

        public static async Task<T> Retry<T>(int count, Func<Task<T>> action, CancellationTokenSource cts = null)
        {
            Exception lastException = null;
            for (var i = 0; i < count; i++)
            {
                if (cts is { IsCancellationRequested: true })
                    throw new OperationCanceledException(cts.Token);

                if (i > 0)
                    await UnityLatestUpdate.WaitSeconds(1f);
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
                    lastException = e;
                }
            }

            if (lastException == null)
                throw new Exception("Failed to retry.");
            throw lastException;
        }

        [Serializable]
        private class BalancerErrorBody
        {
            public string error;
            public string code;
            public RelayUsage usage;
        }

#if UNITY_WEB
        private static string FailureText(UnityWebRequest request)
        {
            string text = request.downloadHandler?.text;
            return string.IsNullOrEmpty(text) ? request.error : text;
        }
#endif

        private static Exception BalancerFailure(string what, string text)
        {
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
            {
                try
                {
                    var body = JsonUtility.FromJson<BalancerErrorBody>(text);
                    if (body != null && !string.IsNullOrEmpty(body.error))
                    {
                        if (body.code == "players_exceeded" || body.code == "traffic_exceeded")
                            return new RelayRefusedException(body.code, $"{what}: {body.error}", body.usage);
                        return new Exception($"{what}: {body.error}");
                    }
                }
                catch
                {
                    // ignore
                }
            }
            return new Exception($"{what}: {text}");
        }

        internal static async Task<ClientJoinInfo> Join(string server, string roomName, CancellationTokenSource cts, string projectKey = null)
        {
            return await Retry<ClientJoinInfo>(10, () => ActualClientJoinInfo(server, roomName, projectKey), cts);
        }

        internal static async Task<bool> RoomExistsAsync(string server, string roomName, string projectKey = null)
        {
            try
            {
                await ActualClientJoinInfo(server, roomName, projectKey);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void SetProjectKey([UsedImplicitly] object request, string projectKey)
        {
#if UNITY_WEB
            if (!string.IsNullOrWhiteSpace(projectKey) && request is UnityWebRequest webRequest)
                webRequest.SetRequestHeader("project_key", projectKey.Trim());
#endif
        }

        private static async Task<ClientJoinInfo> ActualClientJoinInfo(string server, string roomName, string projectKey = null)
        {
#if UNITY_WEB
            if (!server.EndsWith("/"))
                server += "/";

            var url = $"{server}join";
            var request = UnityWebRequest.Get(url);
            request.useHttpContinue = false;
            request.SetRequestHeader("name", roomName);
            request.SetRequestHeader("Cache-Control", "no-cache");
            SetProjectKey(request, projectKey);
            var response = await request.SendWebRequest();

            if (response.webRequest.result != UnityWebRequest.Result.Success)
                throw BalancerFailure("Failed to join room", FailureText(response.webRequest));

            var text = response.webRequest.downloadHandler.text;
            var res = JsonUtility.FromJson<ClientJoinInfo>(text);
            res.hasMigrationGeneration = text.Contains("\"migrationGeneration\"");
            return res;
#else
            throw new NotSupportedException("You need the `com.unity.modules.unitywebrequest` package to use this.");
#endif
        }

        /// <summary>
        /// Claims a relay room whose host is gone: the claimer becomes its host. Every secret of the room rotates, so
        /// the other clients rejoin with fresh join details. Fails when <paramref name="expectedGeneration"/> is
        /// given and another client claimed first.
        /// </summary>
        internal static Task<MigrationJoinInfo> ClaimMigration(string server, string relayRoomName, string claimSecret,
            string promotedPlayerId, int? expectedGeneration, CancellationToken cancellationToken = default,
            string projectKey = null)
        {
            return MigrationRequest<MigrationJoinInfo>(server, "migration/claim", relayRoomName, claimSecret, null,
                promotedPlayerId, expectedGeneration, cancellationToken, projectKey);
        }

        /// <summary>Reads a relay room's migration state.</summary>
        internal static Task<MigrationJoinInfo> GetMigrationState(string server, string relayRoomName, string projectKey = null)
        {
            return MigrationRequest<MigrationJoinInfo>(server, "migration/current", relayRoomName, null, null, null, null,
                default, projectKey);
        }

        [Serializable]
        internal struct MigrationAuthorization
        {
            public string authorization;
            public int generation;
            public int expiresInSeconds;
        }

        /// <summary>
        /// The room's host lets <paramref name="promotedPlayerId"/> claim the room once while the host is still
        /// connected (a graceful handoff). Relays without host migration support reject it.
        /// </summary>
        internal static Task<MigrationAuthorization> AuthorizeMigration(string server, string relayRoomName,
            string hostSecret, string promotedPlayerId, CancellationToken cancellationToken = default,
            string projectKey = null)
        {
            return MigrationRequest<MigrationAuthorization>(server, "migration/authorize", relayRoomName, null, hostSecret,
                promotedPlayerId, null, cancellationToken, projectKey);
        }

        [Serializable]
        private class MigrationErrorBody
        {
            public string error;
            public string code;
            public int generation;
        }

        private static Exception MigrationFailure(string path, string text)
        {
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
            {
                try
                {
                    var body = JsonUtility.FromJson<MigrationErrorBody>(text);
                    if (body != null && !string.IsNullOrEmpty(body.code))
                        return new RelayMigrationRejectedException(body.code, body.error ?? body.code,
                            text.Contains("\"generation\"") ? body.generation : (int?)null);
                }
                catch
                {
                    // ignore
                }
            }

            return BalancerFailure($"Relay {path} failed", text);
        }

        private static async Task<T> MigrationRequest<T>(string server, string path, string relayRoomName,
            string claimSecret, string hostSecret, string promotedPlayerId, int? expectedGeneration,
            CancellationToken cancellationToken = default, string projectKey = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_WEB
            if (!server.EndsWith("/"))
                server += "/";

            var request = UnityWebRequest.Get($"{server}{path}");
            request.useHttpContinue = false;
            request.SetRequestHeader("Cache-Control", "no-cache");
            request.SetRequestHeader("name", relayRoomName);
            SetProjectKey(request, projectKey);
            if (!string.IsNullOrEmpty(claimSecret))
                request.SetRequestHeader("migration_secret", claimSecret);
            if (!string.IsNullOrEmpty(hostSecret))
                request.SetRequestHeader("host_secret", hostSecret);
            if (!string.IsNullOrEmpty(promotedPlayerId))
                request.SetRequestHeader("promoted_player_id", promotedPlayerId);
            if (expectedGeneration.HasValue)
                request.SetRequestHeader("expected_generation", expectedGeneration.Value.ToString());

            // Aborting only stops waiting: a request the relay already received still takes effect there.
            UnityWebRequestAsyncOperation response;
            using (cancellationToken.Register(() => request.Abort()))
                response = await request.SendWebRequest();
            cancellationToken.ThrowIfCancellationRequested();

            if (response.webRequest.result != UnityWebRequest.Result.Success)
                throw MigrationFailure(path, response.webRequest.downloadHandler.text);

            return JsonUtility.FromJson<T>(response.webRequest.downloadHandler.text);
#else
            await Task.CompletedTask;
            throw new NotSupportedException("You need the `com.unity.modules.unitywebrequest` package to use this.");
#endif
        }

        internal static async Task<HostJoinInfo> Alloc(string server, string region, string roomName, CancellationTokenSource cts, string projectKey = null)
        {
            return await Retry<HostJoinInfo>(10, () => ActualAlloc(server, region, roomName, projectKey), cts);
        }

        private static async Task<HostJoinInfo> ActualAlloc(string server, string region, string roomName, string projectKey = null)
        {
            if (!server.EndsWith("/"))
                server += "/";
#if UNITY_WEB
            var url = $"{server}allocate_ws";

            var request = UnityWebRequest.Get(url);
            request.useHttpContinue = false;
            request.SetRequestHeader("Cache-Control", "no-cache");
            request.SetRequestHeader("region", region);
            request.SetRequestHeader("name", roomName);
            SetProjectKey(request, projectKey);
            var response = await request.SendWebRequest();

            if (response.webRequest.result != UnityWebRequest.Result.Success)
                throw BalancerFailure("Failed to allocate room", FailureText(response.webRequest));

            var text = response.webRequest.downloadHandler.text;
            var res = JsonUtility.FromJson<HostJoinInfo>(text);
            res.hasMigrationGeneration = text.Contains("\"migrationGeneration\"");
            return res;
#else
            throw new NotSupportedException("You need the `com.unity.modules.unitywebrequest` package to use this.");
#endif
        }

        static async Task<float> PingInMS([UsedImplicitly] string url)
        {
            return await Retry<float>(10, () => ActualPing(url));
        }

        /// <summary>
        /// HTTP round trip to a relay's API endpoint in milliseconds, for comparing
        /// regions without allocating or joining a room.
        /// </summary>
        public static async Task<float> PingRelayAsync(RelayServer server)
        {
            var url = $"{server.apiEndpoint}/ping";
            await ActualPing(url);
            var first = await ActualPing(url);
            var second = await ActualPing(url);
            return Math.Min(first, second) * 1000f;
        }

        private static async Task<float> ActualPing(string url)
        {
#if UNITY_WEB
            using var request = UnityWebRequest.Get(url);
            request.timeout = 5;
            request.SetRequestHeader("Cache-Control", "no-cache");
            request.useHttpContinue = false;
            var watch = Stopwatch.StartNew();
            await request.SendWebRequest();
            watch.Stop();
            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"Failed to ping relay '{url}': {request.error} (HTTP {request.responseCode}).");
            return (float)watch.Elapsed.TotalSeconds;
#else
            throw new NotSupportedException("You need the `com.unity.modules.unitywebrequest` package to use this.");
#endif
        }

        public static async Task<Relayers> GetRelayServersAsync(string server)
        {
            return await Retry<Relayers>(10, () => ActualGetRelayServersAsync(server));
        }

        public static async Task<Relayers> ActualGetRelayServersAsync(string server)
        {
            if (!server.EndsWith("/"))
                server += "/";

            string master = $"{server}servers";
            var response = await Get(master);
            if (string.IsNullOrEmpty(response))
                return default;
            return JsonUtility.FromJson<Relayers>(response);
        }

        public static async Task<RelayServer> GetRelayServerAsync(string masterServer, CancellationTokenSource cts)
        {
            return await Retry<RelayServer>(10, () => ActualGetRelayServerAsync(masterServer), cts);
        }

        public static async Task<RelayServer> ActualGetRelayServerAsync(string masterServer)
        {
            var measured = await MeasureRelayServersAsync(masterServer);
            float minPing = float.MaxValue;
            RelayServer result = default;
            Exception lastError = null;

            foreach (var entry in measured)
            {
                if (entry.error != null)
                {
                    lastError = entry.error;
                    continue;
                }
                if (entry.milliseconds < minPing)
                {
                    minPing = entry.milliseconds;
                    result = entry.server;
                }
            }

            if (minPing == float.MaxValue)
                throw new Exception("No relay server answered a ping.", lastError);

            return result;
        }

        public struct RelayMeasurement
        {
            public RelayServer server;
            public float milliseconds;
            public Exception error;
        }

        public static async Task<List<RelayMeasurement>> MeasureRelayServersAsync(string masterServer)
        {
            if (!masterServer.EndsWith("/"))
                masterServer += "/";

            var servers = await GetRelayServersAsync(masterServer);
            if (servers.servers == null || servers.servers.Length == 0)
                throw new Exception("The master server returned no relay servers.");

#if !UNITY_WEBGL || UNITY_EDITOR
            var tasks = new List<Task<RelayMeasurement>>(servers.servers.Length);
            foreach (var server in servers.servers)
                tasks.Add(MeasureOneAsync(server));
            return new List<RelayMeasurement>(await Task.WhenAll(tasks));
#else
            return await MeasureInBrowserAsync(servers.servers);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int PurrRelayPing_Start(string urlsJson);
        [DllImport("__Internal")] private static extern IntPtr PurrRelayPing_Poll(int id);
        [DllImport("__Internal")] private static extern void PurrRelayPing_Free(IntPtr ptr);

        [Serializable] private class BrowserPingUrls { public string[] urls; }
        [Serializable] private class BrowserPingResult { public float ms; public string error; }
        [Serializable] private class BrowserPingResults { public BrowserPingResult[] results; }

        private static async Task<List<RelayMeasurement>> MeasureInBrowserAsync(RelayServer[] servers)
        {
            var urls = new string[servers.Length];
            for (var i = 0; i < servers.Length; i++)
                urls[i] = $"{servers[i].apiEndpoint}/ping";

            var id = PurrRelayPing_Start(JsonUtility.ToJson(new BrowserPingUrls { urls = urls }));
            const float TIMEOUT_SECONDS = 15f;
            var waited = 0f;
            IntPtr ptr;
            while ((ptr = PurrRelayPing_Poll(id)) == IntPtr.Zero)
            {
                if (waited > TIMEOUT_SECONDS)
                    throw new TimeoutException("Relay measurement did not complete in the browser.");
                await UnityLatestUpdate.WaitSeconds(0.05f);
                waited += 0.05f;
            }

            string json;
            try { json = Marshal.PtrToStringUTF8(ptr); }
            finally { PurrRelayPing_Free(ptr); }

            var parsed = JsonUtility.FromJson<BrowserPingResults>(json)?.results ?? Array.Empty<BrowserPingResult>();
            var results = new List<RelayMeasurement>(servers.Length);
            for (var i = 0; i < servers.Length; i++)
            {
                var entry = new RelayMeasurement { server = servers[i] };
                var result = i < parsed.Length ? parsed[i] : null;
                if (result == null || !string.IsNullOrEmpty(result.error) || result.ms < 0)
                    entry.error = new Exception(result?.error ?? "no measurement");
                else
                    entry.milliseconds = result.ms;
                results.Add(entry);
            }
            return results;
        }
#endif

        static async Task<RelayMeasurement> MeasureOneAsync(RelayServer server)
        {
            var entry = new RelayMeasurement { server = server };
            try
            {
                entry.milliseconds = await PingRelayAsync(server);
            }
            catch (Exception e)
            {
                entry.error = e;
            }
            return entry;
        }
    }
}
