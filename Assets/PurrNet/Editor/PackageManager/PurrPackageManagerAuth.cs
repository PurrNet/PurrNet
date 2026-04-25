using System;
using System.Net;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerAuth
    {
        private const string PrefKey = "PurrNet_PackageManager_ApiKey";
        private const string LoginBaseUrl = "https://purrnet.dev/auth/unity";

        private static HttpListener _listener;
        private static Thread _listenerThread;

        public static event Action onAuthChanged;

        public static string GetApiKey()
        {
            return EditorPrefs.GetString(PrefKey, "");
        }

        public static void SetApiKey(string key)
        {
            EditorPrefs.SetString(PrefKey, key);
            onAuthChanged?.Invoke();
        }

        public static void ClearApiKey()
        {
            EditorPrefs.DeleteKey(PrefKey);
            onAuthChanged?.Invoke();
        }

        public static bool HasApiKey()
        {
            return !string.IsNullOrEmpty(GetApiKey());
        }

        public static bool IsLoggedIn => HasApiKey();

        public static bool TryGetApiKey(out string apiKey)
        {
            apiKey = GetApiKey();
            return !string.IsNullOrEmpty(apiKey);
        }

        public static void Login()
        {
            if (_listener != null)
            {
                StopListener();
            }

            int port = GetAvailablePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                _listener.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Failed to start auth listener: {e.Message}");
                _listener = null;
                return;
            }

            _listenerThread = new Thread(() => ListenForCallback(port))
            {
                IsBackground = true,
                Name = "PurrNet Auth Listener"
            };
            _listenerThread.Start();

            var url = $"{LoginBaseUrl}?port={port}";
            Application.OpenURL(url);
        }

        public static void Logout()
        {
            ClearApiKey();
        }

        public static void DrawLoginButton()
        {
            if (HasApiKey())
            {
                if (GUILayout.Button("Logout", GUILayout.Height(20), GUILayout.Width(60)))
                    Logout();
            }
            else
            {
                if (GUILayout.Button("Login", GUILayout.Height(20), GUILayout.Width(60)))
                    Login();
            }
        }

        public static void DrawLoginButton(float width, float height)
        {
            if (HasApiKey())
            {
                if (GUILayout.Button("Logout", GUILayout.Height(height), GUILayout.Width(width)))
                    Logout();
            }
            else
            {
                if (GUILayout.Button("Login", GUILayout.Height(height), GUILayout.Width(width)))
                    Login();
            }
        }

        private static void ListenForCallback(int port)
        {
            try
            {
                while (_listener is { IsListening: true })
                {
                    var context = _listener.GetContext();
                    var request = context.Request;

                    // Add CORS headers for the website fetch
                    context.Response.AddHeader("Access-Control-Allow-Origin", "*");
                    context.Response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");

                    // Handle CORS preflight
                    if (request.HttpMethod == "OPTIONS")
                    {
                        context.Response.StatusCode = 204;
                        context.Response.OutputStream.Close();
                        continue;
                    }

                    var key = request.QueryString["key"];
                    bool success = !string.IsNullOrEmpty(key);

                    if (success)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            SetApiKey(key);
                        };
                    }

                    var body = success ? "ok" : "missing key";
                    var buffer = System.Text.Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = success ? 200 : 400;
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();

                    if (success)
                        break;
                }
            }
            catch (HttpListenerException)
            {
                // Listener was stopped, expected
            }
            catch (ObjectDisposedException)
            {
                // Listener was disposed, expected
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Auth listener error: {e.Message}");
            }
            finally
            {
                StopListener();
            }
        }

        private static void StopListener()
        {
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
                // ignore cleanup errors
            }
            _listener = null;
            _listenerThread = null;
        }

        private static int GetAvailablePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
