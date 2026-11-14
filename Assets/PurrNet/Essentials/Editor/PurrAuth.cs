using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace PurrNet.Essentials
{
    public static class PurrAuth
    {
        private const string PrefKey = "PurrNet_PackageManager_ApiKey";
        private const string LoginBaseUrl = "https://purrnet.dev/auth/unity";
        private const string UserUrl = "https://purrnet.dev/api/me";

        private static HttpListener _listener;
        private static int _loginAttempt;

        internal static int loginAttempt => _loginAttempt;

        public static event Action onAuthChanged;

        public static string GetApiKey()
        {
            return EditorPrefs.GetString(PrefKey, "");
        }

        public static void SetApiKey(string key)
        {
            CancelLogin();
            EditorPrefs.SetString(PrefKey, key);
            onAuthChanged?.Invoke();
        }

        public static void ClearApiKey()
        {
            CancelLogin();
            EditorPrefs.DeleteKey(PrefKey);
            onAuthChanged?.Invoke();
        }

        public static bool HasApiKey()
        {
            return !string.IsNullOrEmpty(GetApiKey());
        }

        public static bool isLoggedIn => HasApiKey();

        public static bool TryGetApiKey(out string apiKey)
        {
            apiKey = GetApiKey();
            return !string.IsNullOrEmpty(apiKey);
        }

        public static async Task<Result<PurrUserInfo>> GetUser(string apiKey, int timeoutSeconds = 0)
        {
            try
            {
                using var request = UnityWebRequest.Get(UserUrl);
                request.timeout = timeoutSeconds;

                if (!string.IsNullOrEmpty(apiKey))
                    request.SetRequestHeader("Authorization", "Bearer " + apiKey);

                var tcs = new TaskCompletionSource<bool>();
                var operation = request.SendWebRequest();
                operation.completed += _ => tcs.TrySetResult(true);
                await tcs.Task;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.error;
                    try
                    {
                        var apiError = JsonConvert.DeserializeObject<ApiError>(request.downloadHandler.text);
                        string serverError = apiError?.error ?? apiError?.message;
                        if (!string.IsNullOrEmpty(serverError))
                            errorMsg = serverError;
                    }
                    catch
                    {
                        // use the original error
                    }

                    return Result<PurrUserInfo>.Fail(errorMsg);
                }

                var result = JsonConvert.DeserializeObject<PurrUserInfo>(request.downloadHandler.text);
                return Result<PurrUserInfo>.Ok(result);
            }
            catch (Exception e)
            {
                return Result<PurrUserInfo>.Fail(e.Message);
            }
        }

        public static void Login()
        {
            CancelLogin();
            int attempt = _loginAttempt;

            int port = GetAvailablePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                listener.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Failed to start auth listener: {e.Message}");
                listener.Close();
                return;
            }

            _listener = listener;
            var listenerThread = new Thread(() => ListenForCallback(listener, attempt))
            {
                IsBackground = true,
                Name = "PurrNet Auth Listener"
            };
            listenerThread.Start();

            var url = $"{LoginBaseUrl}?port={port}";
            Application.OpenURL(url);
        }

        [MenuItem("Tools/PurrNet/Paste API Key", false, -100)]
        public static void PasteApiKey()
        {
            PurrLoginWindow.Open();
        }

        public static void Logout()
        {
            ClearApiKey();
        }

        public static void DrawLoginButton()
        {
            DrawLoginButton(60, 20);
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
                if (GUILayout.Button(new GUIContent("Paste API Key", "Open a field to enter your Unity API key"),
                        GUILayout.Height(height), GUILayout.Width(Math.Max(width, 104))))
                    PasteApiKey();
            }
        }

        private static void ListenForCallback(HttpListener listener, int attempt)
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = listener.GetContext();
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

                    var body = success ? "ok" : "missing key";
                    var buffer = System.Text.Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = success ? 200 : 400;
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();

                    if (success)
                    {
                        // Finish the HTTP response before SetApiKey can close the listener.
                        EditorApplication.delayCall += () =>
                        {
                            if (attempt == _loginAttempt)
                                SetApiKey(key);
                        };
                        break;
                    }
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
                StopListener(listener);
            }
        }

        public static void CancelLogin()
        {
            Interlocked.Increment(ref _loginAttempt);
            var listener = Interlocked.Exchange(ref _listener, null);
            if (listener != null)
                StopListener(listener);
        }

        private static void StopListener(HttpListener listener)
        {
            // An older listener must never close a newer login's listener.
            Interlocked.CompareExchange(ref _listener, null, listener);
            try
            {
                listener.Stop();
                listener.Close();
            }
            catch
            {
                // ignore cleanup errors
            }
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
