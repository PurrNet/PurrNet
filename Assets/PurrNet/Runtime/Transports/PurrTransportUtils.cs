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
    public struct HostJoinInfo
    {
        public bool ssl;
        public string secret;
        public string host;
        public int port;
        [Obsolete]
        public int udpPort;
        public int udpPortV2;
    }

    [UsedImplicitly]
    [Serializable]
    public struct ClientJoinInfo
    {
        public bool ssl;
        public string secret;
        public string host;
        public int port;
        [Obsolete]
        public int udpPort;
        public int udpPortV2;
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
                catch (Exception e)
                {
                    lastException = e;
                }
            }

            if (lastException == null)
                throw new Exception("Failed to retry.");
            throw lastException;
        }

        internal static async Task<ClientJoinInfo> Join(string server, string roomName, CancellationTokenSource cts)
        {
            return await Retry<ClientJoinInfo>(10, () => ActualClientJoinInfo(server, roomName), cts);
        }

        internal static async Task<HostMigrationTransportActivationResult> ActivateHostMigration(
            PurrTransport.HostMigrationActivationRequest activation,
            string hostSecret,
            float timeoutSeconds,
            bool mayHaveActivated,
            CancellationToken cancellationToken)
        {
            if (!activation.isValid || string.IsNullOrWhiteSpace(hostSecret))
                return new HostMigrationTransportActivationResult(
                    mayHaveActivated
                        ? HostMigrationTransportActivationStatus.Indeterminate
                        : HostMigrationTransportActivationStatus.Failed,
                    mayHaveActivated
                        ? "The relay activation outcome is unknown and its local fence is incomplete; authoritative reconciliation is required."
                        : "The relay host activation fence is incomplete.");

            if (!TryValidateHostMigrationMasterServerUrl(activation.masterServer,
                    out var masterServerFailure))
                return new HostMigrationTransportActivationResult(
                    mayHaveActivated
                        ? HostMigrationTransportActivationStatus.Indeterminate
                        : HostMigrationTransportActivationStatus.Failed,
                    mayHaveActivated
                        ? $"The previous relay activation outcome is still unknown, and its replay URL is invalid: {masterServerFailure}"
                        : masterServerFailure);

            if (float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0f)
                return new HostMigrationTransportActivationResult(
                    mayHaveActivated
                        ? HostMigrationTransportActivationStatus.Indeterminate
                        : HostMigrationTransportActivationStatus.TimedOut,
                    mayHaveActivated
                        ? "The previous relay activation outcome is still unknown; authoritative reconciliation is required."
                        : "No time remained to activate the provisional relay host.");

            if (HasInvalidActivationHeaderValue(activation, hostSecret))
                return new HostMigrationTransportActivationResult(
                    mayHaveActivated
                        ? HostMigrationTransportActivationStatus.Indeterminate
                        : HostMigrationTransportActivationStatus.Failed,
                    mayHaveActivated
                        ? "The previous relay activation outcome is still unknown and the activation fence contains an invalid HTTP header value."
                        : "The relay activation fence contains an invalid HTTP header value.");

#if UNITY_WEB
            var deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            string lastFailure = null;
            bool requestDispatched = mayHaveActivated;
            bool outcomeMayHaveCommitted = mayHaveActivated;

            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new HostMigrationTransportActivationResult(
                        outcomeMayHaveCommitted
                            ? HostMigrationTransportActivationStatus.Indeterminate
                            : HostMigrationTransportActivationStatus.Cancelled,
                        outcomeMayHaveCommitted
                            ? "Relay host activation may have committed before cancellation; authoritative reconciliation is required."
                            : "Relay host activation was cancelled before dispatch.");

                UnityWebRequest request;
                try
                {
                    var server = activation.masterServer.Trim();
                    if (!server.EndsWith("/"))
                        server += "/";

                    request = CreateEmptyPostRequest($"{server}migration/activate");
                    request.useHttpContinue = false;
                    request.SetRequestHeader("Cache-Control", "no-cache");
                    request.SetRequestHeader("name", activation.roomName);
                    request.SetRequestHeader("migration_secret", hostSecret);
                    request.SetRequestHeader("migration_claim_id", activation.claimId);
                    request.SetRequestHeader("incarnation", activation.incarnation);
                    request.SetRequestHeader("expected_generation",
                        activation.generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    request.SetRequestHeader("promoted_player_id", activation.promotedPlayerId);
                    request.SetRequestHeader("fencing_token", activation.fencingToken);
                    request.timeout = Math.Max(1, (int)Math.Ceiling(Math.Min(10d,
                        deadline - Time.realtimeSinceStartupAsDouble)));
                }
                catch (Exception e)
                {
                    return new HostMigrationTransportActivationResult(
                        requestDispatched
                            ? HostMigrationTransportActivationStatus.Indeterminate
                            : HostMigrationTransportActivationStatus.Failed,
                        requestDispatched
                            ? $"A prior relay activation may have committed, and a later request could not be constructed: {e.Message}"
                            : $"The relay activation request could not be constructed: {e.Message}");
                }

                using (request)
                {
                    try
                    {
                        requestDispatched = true;
                        var response = await request.SendWebRequest();

                        if (response.webRequest.result == UnityWebRequest.Result.Success)
                        {
                            var responseText = response.webRequest.downloadHandler?.text;
                            if (!TryValidateMigrationActivationSuccess(
                                    responseText, activation, out var validationFailure))
                            {
                                lastFailure = validationFailure;
                                outcomeMayHaveCommitted = true;
                            }
                            else
                            {
                                return new HostMigrationTransportActivationResult(
                                    HostMigrationTransportActivationStatus.Succeeded);
                            }
                        }
                        else
                        {
                            lastFailure = response.webRequest.downloadHandler?.text;
                            if (TryGetTerminalMigrationActivationError(
                                    lastFailure, outcomeMayHaveCommitted,
                                    out var terminalFailure))
                                return new HostMigrationTransportActivationResult(
                                    HostMigrationTransportActivationStatus.Failed,
                                    terminalFailure);

                            outcomeMayHaveCommitted = true;
                        }
                    }
                    catch (Exception e)
                    {
                        lastFailure = e.Message;
                        outcomeMayHaveCommitted = true;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    return new HostMigrationTransportActivationResult(
                        HostMigrationTransportActivationStatus.Indeterminate,
                        "Relay host activation may have committed before cancellation; authoritative reconciliation is required.");

                if (Time.realtimeSinceStartupAsDouble < deadline)
                    await UnityLatestUpdate.WaitSeconds(Math.Min(0.25f,
                        (float)(deadline - Time.realtimeSinceStartupAsDouble)));
            }

            return new HostMigrationTransportActivationResult(
                outcomeMayHaveCommitted
                    ? HostMigrationTransportActivationStatus.Indeterminate
                    : HostMigrationTransportActivationStatus.TimedOut,
                outcomeMayHaveCommitted
                    ? string.IsNullOrWhiteSpace(lastFailure)
                        ? "Relay host activation was dispatched but its outcome is unknown; authoritative reconciliation is required."
                        : $"Relay host activation outcome is unknown and requires authoritative reconciliation: {lastFailure}"
                    : requestDispatched
                        ? "Timed out while the relay reported that host activation remained uncommitted."
                        : "Timed out before relay host activation could be dispatched.");
#else
            await Task.Yield();
            return new HostMigrationTransportActivationResult(
                mayHaveActivated
                    ? HostMigrationTransportActivationStatus.Indeterminate
                    : HostMigrationTransportActivationStatus.Failed,
                mayHaveActivated
                    ? "The previous relay activation outcome is unknown and cannot be reconciled without `com.unity.modules.unitywebrequest`."
                    : "You need the `com.unity.modules.unitywebrequest` package to activate relay host migration.");
#endif
        }

        internal static bool TryValidateHostMigrationMasterServerUrl(string value,
            out string failure)
        {
            failure = null;
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                failure = "Relay host activation requires a valid absolute master server URL without a query or fragment.";
                return false;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase))
                return true;

            failure = "Relay host activation requires an HTTP or HTTPS master server URL.";
            return false;
        }

        private static bool HasInvalidActivationHeaderValue(
            PurrTransport.HostMigrationActivationRequest activation,
            string hostSecret)
        {
            return HasHttpHeaderControlCharacter(activation.roomName) ||
                   HasHttpHeaderControlCharacter(hostSecret) ||
                   HasHttpHeaderControlCharacter(activation.claimId) ||
                   HasHttpHeaderControlCharacter(activation.incarnation) ||
                   HasHttpHeaderControlCharacter(activation.promotedPlayerId) ||
                   HasHttpHeaderControlCharacter(activation.fencingToken);
        }

        private static bool HasHttpHeaderControlCharacter(string value)
        {
            if (value == null)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (character == '\r' || character == '\n' || character == '\0')
                    return true;
            }

            return false;
        }

#if UNITY_WEB
        [Serializable]
        private sealed class MigrationActivationError
        {
            public string code;
            public string error;
            public bool retryable;
        }

        [Serializable]
        private sealed class MigrationActivationSuccess
        {
            public string roomName;
            public string incarnation;
            public int generation;
            public string fencingToken;
            public string promotedPlayerId;
            public string claimId;
            public string hostPhase;
            public bool hostActive;
            public bool claimPending;
        }

        internal static bool TryValidateMigrationActivationSuccess(
            string text,
            PurrTransport.HostMigrationActivationRequest expected,
            out string failure)
        {
            failure = null;
            MigrationActivationSuccess response;
            try
            {
                response = string.IsNullOrWhiteSpace(text)
                    ? null
                    : JsonUtility.FromJson<MigrationActivationSuccess>(text);
            }
            catch
            {
                response = null;
            }

            if (response == null ||
                !response.hostActive ||
                response.claimPending ||
                !string.Equals(response.hostPhase, "active", StringComparison.Ordinal) ||
                !string.Equals(response.roomName, expected.roomName, StringComparison.Ordinal) ||
                !string.Equals(response.incarnation, expected.incarnation, StringComparison.Ordinal) ||
                response.generation != expected.generation ||
                !string.Equals(response.fencingToken, expected.fencingToken, StringComparison.Ordinal) ||
                !string.Equals(response.promotedPlayerId, expected.promotedPlayerId, StringComparison.Ordinal) ||
                !string.Equals(response.claimId, expected.claimId, StringComparison.Ordinal))
            {
                failure = "The relay returned a successful activation response for an incomplete or different migration fence.";
                return false;
            }

            return true;
        }

        private static MigrationActivationError TryParseMigrationActivationError(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                return JsonUtility.FromJson<MigrationActivationError>(text);
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryGetTerminalMigrationActivationError(
            string text,
            bool outcomeMayHaveCommitted,
            out string failure)
        {
            failure = null;
            var error = TryParseMigrationActivationError(text);
            if (error == null || !IsTerminalActivationFenceError(error.code))
                return false;

            if (outcomeMayHaveCommitted &&
                string.Equals(error.code, "room_not_found", StringComparison.OrdinalIgnoreCase))
                return false;

            failure = string.IsNullOrWhiteSpace(error.error) ? error.code : error.error;
            return true;
        }

        internal static bool IsTerminalActivationFenceError(string code)
        {
            return string.Equals(code, "host_activation_expired", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "host_connection_lost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "activation_fence_mismatch", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "incarnation_changed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "room_not_found", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "invalid_host_credential", StringComparison.OrdinalIgnoreCase);
        }
#endif

        private static async Task<ClientJoinInfo> ActualClientJoinInfo(string server, string roomName)
        {
#if UNITY_WEB
            if (!server.EndsWith("/"))
                server += "/";

            var url = $"{server}join";
            var request = UnityWebRequest.Get(url);
            request.useHttpContinue = false;
            request.SetRequestHeader("name", roomName);
            request.SetRequestHeader("Cache-Control", "no-cache");
            request.timeout = 10;
            var response = await request.SendWebRequest();

            if (response.webRequest.result != UnityWebRequest.Result.Success)
                throw new Exception($"Failed to join room: {response.webRequest.downloadHandler.text}");

            var text = response.webRequest.downloadHandler.text;
            var res = JsonUtility.FromJson<ClientJoinInfo>(text);
            if (!HasCompleteJoinCapability(res))
                throw new Exception("The room join did not return a complete relay connection capability.");
            return res;
#else
            throw new NotSupportedException("You need the `com.unity.modules.unitywebrequest` package to use this.");
#endif
        }

        internal static async Task<HostJoinInfo> Alloc(string server, string region, string roomName, CancellationTokenSource cts)
        {
            var allocationId = Guid.NewGuid().ToString("N");
            return await Retry<HostJoinInfo>(
                10, () => ActualAlloc(server, region, roomName, allocationId), cts);
        }

        private static async Task<HostJoinInfo> ActualAlloc(
            string server,
            string region,
            string roomName,
            string allocationId)
        {
            if (!server.EndsWith("/"))
                server += "/";
#if UNITY_WEB
            var url = $"{server}allocate_ws";

            var request = CreateEmptyPostRequest(url);
            request.useHttpContinue = false;
            request.SetRequestHeader("Cache-Control", "no-cache");
            request.SetRequestHeader("region", region);
            request.SetRequestHeader("name", roomName);
            request.SetRequestHeader("allocation_id", allocationId);
            request.timeout = 10;
            var response = await request.SendWebRequest();

            if (response.webRequest.result != UnityWebRequest.Result.Success)
                throw new Exception($"Failed to allocate room: {response.webRequest.downloadHandler.text}");

            var text = response.webRequest.downloadHandler.text;
            var res = JsonUtility.FromJson<HostJoinInfo>(text);
            if (!HasCompleteAllocationCapability(res))
                throw new Exception("The room allocation did not return a complete relay connection capability.");
            return res;
#else
            throw new NotSupportedException("You need the `com.unity.modules.unitywebrequest` package to use this.");
#endif
        }

        internal static bool HasCompleteAllocationCapability(HostJoinInfo allocation)
        {
            return HasCompleteConnectionCapability(
                allocation.host, allocation.secret, allocation.port, allocation.udpPortV2);
        }

        internal static bool HasCompleteJoinCapability(ClientJoinInfo join)
        {
            return HasCompleteConnectionCapability(join.host, join.secret, join.port, join.udpPortV2);
        }

        private static bool HasCompleteConnectionCapability(
            string host,
            string secret,
            int webSocketPort,
            int udpPort)
        {
            return !string.IsNullOrWhiteSpace(host) &&
                   !string.IsNullOrWhiteSpace(secret) &&
                   webSocketPort > 0 && webSocketPort <= ushort.MaxValue &&
                   udpPort > 0 && udpPort <= ushort.MaxValue;
        }

#if UNITY_WEB
        private static UnityWebRequest CreateEmptyPostRequest(string url)
        {
            return new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                uploadHandler = new UploadHandlerRaw(Array.Empty<byte>())
            };
        }
#endif

        static async Task<float> PingInMS([UsedImplicitly] string url)
        {
            return await Retry<float>(10, () => ActualPing(url));
        }

        private static async Task<float> ActualPing(string url)
        {
#if UNITY_WEB
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Cache-Control", "no-cache");
            request.useHttpContinue = false;
            var sent = DateTime.Now;
            await request.SendWebRequest();

            var received = DateTime.Now;
            return (float)(received - sent).TotalSeconds;
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
            if (!masterServer.EndsWith("/"))
                masterServer += "/";

            var servers = await GetRelayServersAsync(masterServer);
            float minPing = float.MaxValue;
            RelayServer result = default;

            var pings = new List<Task<float>>();

            for (var i = 0; i < servers.servers.Length; i++)
            {
                var pingUrl = $"{servers.servers[i].apiEndpoint}/ping";
                pings.Add(PingInMS(pingUrl));
            }

            await Task.WhenAny(pings);

            for (var i = 0; i < pings.Count; i++)
            {
                var ping = pings[i];

                if (ping.Status != TaskStatus.RanToCompletion)
                    continue;

                var resultPing = ping.Result;

                if (resultPing < minPing)
                {
                    minPing = resultPing;
                    result = servers.servers[i];
                }
            }

            return result;
        }
    }
}
