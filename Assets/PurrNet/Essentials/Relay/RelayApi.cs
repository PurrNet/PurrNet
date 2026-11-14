using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PurrNet.Essentials.Relay
{
    public static class RelayApi
    {
        [Serializable]
        private struct EndpointList
        {
            public RelayEndpoint[] servers;
        }

        [Serializable]
        private class ErrorBody
        {
            public string error;
            public string code;
            public RelayBudget usage;
        }

        private struct Response
        {
            public bool success;
            public string text;
            public string error;
            public long status;
        }

        public static async Task<RelayEndpoint[]> GetServers(string masterServer)
        {
            var response = await Get(Url(masterServer, "servers"), null, 0);
            if (!response.success)
                throw new Exception($"Failed to list relay servers: {Describe(response)}");

            var list = JsonUtility.FromJson<EndpointList>(response.text);
            if (list.servers == null || list.servers.Length == 0)
                throw new Exception("The master server returned no relay servers.");
            return list.servers;
        }

        public static async Task<float> Ping(RelayEndpoint server)
        {
            string url = $"{server.apiEndpoint}/ping";
            await TimedPing(url);
            float first = await TimedPing(url);
            float second = await TimedPing(url);
            return Math.Min(first, second) * 1000f;
        }

        public static async Task<RelayEndpoint> FindClosest(string masterServer)
        {
            var servers = await GetServers(masterServer);
            var pings = new List<Task<float>>(servers.Length);
            for (int i = 0; i < servers.Length; i++)
                pings.Add(Ping(servers[i]));

            float best = float.MaxValue;
            RelayEndpoint result = default;
            Exception lastError = null;

            for (int i = 0; i < servers.Length; i++)
            {
                try
                {
                    float milliseconds = await pings[i];
                    if (milliseconds < best)
                    {
                        best = milliseconds;
                        result = servers[i];
                    }
                }
                catch (Exception e)
                {
                    lastError = e;
                }
            }

            if (best == float.MaxValue)
                throw new Exception("No relay server answered a ping.", lastError);
            return result;
        }

        public static async Task<RelayJoinDetails> Allocate(string masterServer, string region, string roomName,
            string projectKey)
        {
            var headers = new Dictionary<string, string> { ["region"] = region, ["name"] = roomName };
            AddProjectKey(headers, projectKey);

            var response = await Get(Url(masterServer, "allocate_ws"), headers, 0);
            if (!response.success)
                throw Failure("Failed to allocate room", response);
            return JsonUtility.FromJson<RelayJoinDetails>(response.text);
        }

        public static async Task<RelayJoinDetails> Join(string masterServer, string roomName, string projectKey)
        {
            var headers = new Dictionary<string, string> { ["name"] = roomName };
            AddProjectKey(headers, projectKey);

            var response = await Get(Url(masterServer, "join"), headers, 0);
            if (!response.success)
                throw Failure("Failed to join room", response);
            return JsonUtility.FromJson<RelayJoinDetails>(response.text);
        }

        private static void AddProjectKey(Dictionary<string, string> headers, string projectKey)
        {
            if (!string.IsNullOrWhiteSpace(projectKey))
                headers["project_key"] = projectKey.Trim();
            foreach (var header in RelayRequestHeaders.all)
                headers[header.Key] = header.Value;
        }

        private static string Url(string masterServer, string path)
        {
            return masterServer.EndsWith("/") ? masterServer + path : masterServer + "/" + path;
        }

        private static string Describe(Response response)
        {
            return string.IsNullOrEmpty(response.text) ? response.error : response.text;
        }

        private static Exception Failure(string what, Response response)
        {
            string text = Describe(response);

            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
            {
                try
                {
                    var body = JsonUtility.FromJson<ErrorBody>(text);
                    if (body != null && !string.IsNullOrEmpty(body.error))
                    {
                        if (body.code == "players_exceeded" || body.code == "traffic_exceeded")
                            return new RelayRefusedException(body.code, $"{what}: {body.error}", body.usage);
                        return new Exception($"{what}: {body.error}");
                    }
                }
                catch
                {
                }
            }

            return new Exception($"{what}: {text}");
        }

        private static async Task<float> TimedPing(string url)
        {
            var watch = Stopwatch.StartNew();
            var response = await Get(url, null, 5);
            watch.Stop();
            if (!response.success)
                throw new Exception($"Failed to ping relay '{url}': {response.error} (HTTP {response.status}).");
            return (float)watch.Elapsed.TotalSeconds;
        }

        private static Task<Response> Get(string url, Dictionary<string, string> headers, int timeoutSeconds)
        {
#if UNITY_WEB
            var completion = new TaskCompletionSource<Response>();
            var request = UnityWebRequest.Get(url);
            request.useHttpContinue = false;
            request.timeout = timeoutSeconds;
            request.SetRequestHeader("Cache-Control", "no-cache");

            if (headers != null)
            {
                foreach (var header in headers)
                    request.SetRequestHeader(header.Key, header.Value);
            }

            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                var response = new Response
                {
                    success = request.result == UnityWebRequest.Result.Success,
                    text = request.downloadHandler?.text,
                    error = request.error,
                    status = request.responseCode
                };
                request.Dispose();
                completion.TrySetResult(response);
            };
            return completion.Task;
#else
            throw new NotSupportedException("You need the `com.unity.modules.unitywebrequest` package to use this.");
#endif
        }
    }
}
