using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using NUnit.Framework;

namespace PurrNet.Relay.Tests
{
    public static class LocalRelay
    {
        const int BalancerPort = 18080;
        const int RelayPort = 18081;
        const int UdpPort = 17777;
        const int UdpPortV2 = 17778;
        const int WebSocketsPort = 16942;
        const string PathVariable = "PURRLAY_PATH";

        static readonly List<Process> _started = new List<Process>();

        public static string balancerUrl => $"http://127.0.0.1:{BalancerPort}/";

        public static void EnsureRunning()
        {
            if (HasRelay())
                return;

            string root = FindRoot();
            if (root == null)
                Assert.Ignore($"No local relay: set {PathVariable} to a PurrLay checkout or place it next to the project.");

            string balancer = Path.Combine(root, "PurrBalancer", "bin", "Debug", "net8.0", "PurrBalancer.dll");
            string relay = Path.Combine(root, "PurrLay", "bin", "Debug", "net8.0", "PurrLay.dll");

            if (!File.Exists(balancer) || !File.Exists(relay))
            {
                var build = Start(root, "build PurrLay.sln -c Debug -nologo -v:q", null);
                if (build == null || !build.WaitForExit(300000) || build.ExitCode != 0)
                    Assert.Ignore("No local relay: building PurrLay failed.");
            }

            var balancerProcess = Start(Path.GetDirectoryName(balancer), $"\"{balancer}\"", new Dictionary<string, string>
            {
                ["HOST"] = "127.0.0.1",
                ["PORT"] = BalancerPort.ToString()
            });

            if (balancerProcess == null)
                Assert.Ignore("No local relay: dotnet could not be started.");

            _started.Add(balancerProcess);

            if (!WaitFor(() => Get("ping") != null, 20f))
            {
                Stop();
                Assert.Ignore("No local relay: the balancer did not start.");
            }

            var relayProcess = Start(Path.GetDirectoryName(relay), $"\"{relay}\"", new Dictionary<string, string>
            {
                ["HOST"] = "127.0.0.1",
                ["PORT"] = RelayPort.ToString(),
                ["BALANCER_URL"] = $"http://127.0.0.1:{BalancerPort}",
                ["HOST_SSL"] = "false",
                ["HOST_REGION"] = "local",
                ["HOST_DOMAIN"] = "127.0.0.1",
                ["HOST_ENDPOINT"] = $"http://127.0.0.1:{RelayPort}",
                ["UDP_PORT"] = UdpPort.ToString(),
                ["UDP_PORT_V2"] = UdpPortV2.ToString(),
                ["WEBSOCKETS_PORT"] = WebSocketsPort.ToString()
            });

            if (relayProcess != null)
                _started.Add(relayProcess);

            if (!WaitFor(HasRelay, 30f))
            {
                Stop();
                Assert.Ignore("No local relay: the relay did not register with the balancer.");
            }
        }

        public static void Stop()
        {
            for (int i = _started.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (!_started[i].HasExited)
                        _started[i].Kill();
                    _started[i].Dispose();
                }
                catch (Exception)
                {
                }
            }

            _started.Clear();
        }

        static string FindRoot()
        {
            string configured = Environment.GetEnvironmentVariable(PathVariable);
            if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "PurrLay.sln")))
                return configured;

            string sibling = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "PurrLay"));
            return File.Exists(Path.Combine(sibling, "PurrLay.sln")) ? sibling : null;
        }

        static Process Start(string workingDirectory, string arguments, Dictionary<string, string> environment)
        {
            try
            {
                var info = new ProcessStartInfo("dotnet", arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                if (environment != null)
                {
                    foreach (var pair in environment)
                        info.EnvironmentVariables[pair.Key] = pair.Value;
                }

                var process = Process.Start(info);
                if (process == null)
                    return null;

                process.OutputDataReceived += (_, __) => { };
                process.ErrorDataReceived += (_, __) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return process;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool HasRelay()
        {
            string servers = Get("servers");
            return servers != null && servers.Contains("\"apiEndpoint\"");
        }

        static bool WaitFor(Func<bool> condition, float seconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                Thread.Sleep(250);
            }

            return condition();
        }

        static string Get(string path)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var response = client.GetAsync(balancerUrl + path).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode ? response.Content.ReadAsStringAsync().GetAwaiter().GetResult() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
