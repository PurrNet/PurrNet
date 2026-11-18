#if UNITY_WEBGL && !UNITY_EDITOR
#define PURRREC_WEB
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PurrNet;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace PurrNet.Recording
{
    [AddComponentMenu("")]
    public sealed class RecordingUploader : MonoBehaviour
    {
        const string Extension = ".purrrec";
        const string SentMarker = ".sent";
        const string ClaimMarker = ".lock";
        const float RetryDelaySeconds = 60f;
        const float HeldGapSeconds = 5f;
        const int TicketTimeoutSeconds = 20;
        const string PrefsKey = "PurrDiction.RecordingUploader";

        static RecordingUploader _instance;

        public static bool runningOnCI { get; } = DetectCI();

        static bool DetectCI()
        {
            try
            {
                string ci = Environment.GetEnvironmentVariable("CI");
                if (!string.IsNullOrEmpty(ci) && ci != "0" && !ci.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return true;
                foreach (var name in new[] { "GITHUB_ACTIONS", "GITLAB_CI", "TF_BUILD", "JENKINS_URL", "TEAMCITY_VERSION", "BUILDKITE" })
                {
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                        return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        string _endpoint;
        string _projectKey;
        string _directory;
        long _maxLocalBytes;
        readonly HashSet<string> _writing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A dedicated server sends the recordings it finishes while it keeps running: it may not be offline for
        /// days. Every other machine waits until the game is offline, so sending never competes with play.
        /// </summary>
        public static bool sendWhileServing = true;

        /// <summary>Whether this process is a dedicated server. Tests set it to run a server's sending in the editor.</summary>
        internal static bool dedicatedServer = ApplicationContext.isServerBuild;

        /// <summary>
        /// A recording sent while the server runs is spread over this share of the time it took to record, so
        /// sending takes four times the bandwidth recording produced data at, whatever the game: it always keeps
        /// up, and it never takes the connection for itself.
        /// </summary>
        internal static double sendingShare = 0.25;

        /// <summary>However long a recording was, sending it stays well inside the hour its upload link is good for.</summary>
        const float LongestPacedSeconds = 30 * 60;
#if !PURRREC_WEB
        float _retryAt;
        readonly List<string> _finished = new List<string>();
        readonly Dictionary<string, float> _startedAt = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, float> _recordedSeconds = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _unpaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PacedUpload _paced;
        readonly Dictionary<string, float> _held = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool _blocked;
        bool _uploading;
        string _uploadPath;
#endif
        UnityWebRequest _request;

        public static void Configure(string endpoint, string projectKey, string directory, long maxLocalBytes)
        {
            if (!_instance)
            {
                var go = new GameObject("PurrDiction RecordingUploader") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<RecordingUploader>();
            }

            _instance._endpoint = endpoint.TrimEnd('/');
            _instance._projectKey = projectKey;
            _instance._directory = directory;
            _instance._maxLocalBytes = maxLocalBytes;
#if PURRREC_WEB
            WebRecording.Uploads(_instance._endpoint, projectKey, maxLocalBytes);
#endif

            var saved = JsonUtility.ToJson(new SavedSettings
            {
                endpoint = _instance._endpoint, projectKey = projectKey, directory = directory, maxLocalBytes = maxLocalBytes
            });
            if (PlayerPrefs.GetString(PrefsKey, "") != saved)
            {
                PlayerPrefs.SetString(PrefsKey, saved);
                PlayerPrefs.Save();
            }
        }

        [Serializable]
        struct SavedSettings
        {
            public string endpoint;
            public string projectKey;
            public string directory;
            public long maxLocalBytes;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ResumeFromLastSession()
        {
            if (_instance || runningOnCI || Application.isEditor)
                return;
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
                return;
            var saved = JsonUtility.FromJson<SavedSettings>(json);
            if (!string.IsNullOrEmpty(saved.endpoint) && !string.IsNullOrEmpty(saved.projectKey) && !string.IsNullOrEmpty(saved.directory))
                Configure(saved.endpoint, saved.projectKey, saved.directory, saved.maxLocalBytes);
        }

        /// <summary>A file this process is writing is neither sent nor deleted until it says it is done with it.</summary>
        public static void SetWriting(string path, bool writing)
        {
            if (!_instance)
                return;
            string full = Path.GetFullPath(path);
            if (writing)
            {
                if (_instance._writing.Add(full))
                    _instance.Started(full);
            }
            else if (_instance._writing.Remove(full))
                _instance.Finished(full);
        }

        void Started(string full)
        {
#if !PURRREC_WEB
            if (dedicatedServer)
                _startedAt[full] = Time.realtimeSinceStartup;
#endif
        }

        void Finished(string full)
        {
#if !PURRREC_WEB
            if (!dedicatedServer)
                return;
            _finished.Add(full);
            if (_startedAt.TryGetValue(full, out float started))
                _recordedSeconds[full] = Time.realtimeSinceStartup - started;
            _startedAt.Remove(full);
#endif
        }

        public static void NotifyFinished()
        {
#if !PURRREC_WEB
            if (!_instance)
                return;
            _instance._retryAt = 0;
            _instance.TrimFolder();
#endif
        }

#if PURRREC_WEB
        bool? _webIdle;

        void Update()
        {
            bool idle = IsIdle();
            if (_webIdle != idle)
            {
                _webIdle = idle;
                WebRecording.SetIdle(idle);
            }
        }
#else
        void Update()
        {
            bool idle = IsIdle();
            bool serving = !idle && sendWhileServing && dedicatedServer;
            if (_uploading)
            {
                if (!idle && !serving)
                {
                    _request?.Abort();
                    _paced?.Abort();
                }
                return;
            }

            if (_blocked || Time.unscaledTime < _retryAt || (!idle && !serving) || string.IsNullOrEmpty(_projectKey))
                return;

            float heldUntil;
            string next = idle ? NextPending(out heldUntil) : NextFinished(out heldUntil);
            if (next == null)
            {
                _retryAt = Math.Min(heldUntil, Time.unscaledTime + RetryDelaySeconds);
                return;
            }
            StartCoroutine(Send(next));
        }
#endif

        static bool IsIdle()
        {
            var manager = NetworkManager.main;
            return !manager || manager.isOffline;
        }

#if !PURRREC_WEB
        /// <summary>
        /// While the server runs, only what this process recorded and finished is sent, newest first. Nothing is
        /// looked for on disk: what an earlier run left behind waits until the game is offline, and a file another
        /// server on this machine is still writing is never touched.
        /// </summary>
        string NextFinished(out float heldUntil)
        {
            heldUntil = float.MaxValue;
            for (int i = _finished.Count - 1; i >= 0; i--)
            {
                string full = _finished[i];
                if (_held.TryGetValue(full, out float due) && Time.unscaledTime < due)
                {
                    heldUntil = Math.Min(heldUntil, due);
                    continue;
                }
                if (File.Exists(full) && !IsDone(full))
                    return full;
                _finished.RemoveAt(i);
            }
            return null;
        }

        string NextPending(out float heldUntil)
        {
            heldUntil = float.MaxValue;
            if (string.IsNullOrEmpty(_directory) || !Directory.Exists(_directory))
                return null;

            if (_uploadPath != null)
            {
                if (File.Exists(_uploadPath) && !IsActive(_uploadPath) && !_held.ContainsKey(_uploadPath) && !IsDone(_uploadPath))
                    return _uploadPath;
                _uploadPath = null;
            }

            foreach (var marker in Directory.GetFiles(_directory, "*" + Extension + SentMarker))
                if (!File.Exists(marker.Substring(0, marker.Length - SentMarker.Length)))
                    TryDelete(marker);

            string newest = null;
            DateTime newestTime = DateTime.MinValue;
            foreach (var path in Directory.GetFiles(_directory, "*" + Extension))
            {
                if (IsActive(path))
                    continue;
                string full = Path.GetFullPath(path);
                if (IsDone(full))
                {
                    Finish(full);
                    continue;
                }
                if (_held.TryGetValue(full, out float due) && Time.unscaledTime < due)
                {
                    heldUntil = Math.Min(heldUntil, due);
                    continue;
                }
                var time = File.GetLastWriteTimeUtc(path);
                if (time > newestTime && !BeingWritten(full))
                {
                    newest = path;
                    newestTime = time;
                }
            }
            return newest;
        }

        bool IsDone(string fullPath) => _done.Contains(fullPath) || File.Exists(fullPath + SentMarker);

        void Finish(string path)
        {
            string full = Path.GetFullPath(path);
            _finished.Remove(full);
            _recordedSeconds.Remove(full);
            _unpaced.Remove(full);
            if (TryDelete(full))
            {
                _done.Remove(full);
                _held.Remove(full);
                TryDelete(full + SentMarker);
                return;
            }

            _done.Add(full);
            try
            {
                if (!File.Exists(full + SentMarker))
                    File.WriteAllBytes(full + SentMarker, Array.Empty<byte>());
            }
            catch (Exception)
            {
            }
        }

        static bool BeingWritten(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        static FileStream TryClaim(string fullPath)
        {
            try
            {
                return new FileStream(fullPath + ClaimMarker, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                    FileOptions.DeleteOnClose);
            }
            catch (Exception)
            {
                return null;
            }
        }

        bool IsActive(string path) => _writing.Count > 0 && _writing.Contains(Path.GetFullPath(path));

        void TrimFolder()
        {
            if (_maxLocalBytes <= 0 || string.IsNullOrEmpty(_directory) || !Directory.Exists(_directory))
                return;

            var files = new List<FileInfo>();
            long total = 0;
            foreach (var path in Directory.GetFiles(_directory, "*" + Extension))
            {
                var info = new FileInfo(path);
                total += info.Length;
                if (!IsActive(path) && !string.Equals(info.FullName, _uploadPath, StringComparison.OrdinalIgnoreCase))
                    files.Add(info);
            }
            if (total <= _maxLocalBytes)
                return;

            files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            for (int pass = 0; pass < 2 && total > _maxLocalBytes; pass++)
            {
                for (int i = 0; i < files.Count && total > _maxLocalBytes; i++)
                {
                    var file = files[i];
                    if (file == null || (pass == 0 && ReadLink(file.FullName).part))
                        continue;
                    long length = file.Length;
                    if (TryDelete(file.FullName))
                        total -= length;
                    files[i] = null;
                }
            }
        }

        [Serializable]
        struct TicketRequest
        {
            public string fileName;
            public long size;
            public string recordingId;
            public string kind;
            public long player;
            public string token;
        }

        [Serializable]
        struct HeaderLink
        {
            public string recordingId;
            public bool part;
            public long player;
            public string token;
        }

        static HeaderLink ReadLink(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(10, SeekOrigin.Begin);
                if (stream.ReadByte() != (int)RecordKind.Header)
                    return default;
                long length = 0;
                for (int shift = 0; shift < 64; shift += 7)
                {
                    int b = stream.ReadByte();
                    if (b < 0)
                        return default;
                    length |= (long)(b & 0x7F) << shift;
                    if (b < 0x80)
                        break;
                }
                if (length <= 0 || length > stream.Length - stream.Position)
                    return default;
                var json = new byte[length];
                int read = 0;
                while (read < json.Length)
                {
                    int got = stream.Read(json, read, json.Length - read);
                    if (got <= 0)
                        return default;
                    read += got;
                }
                return JsonUtility.FromJson<HeaderLink>(System.Text.Encoding.UTF8.GetString(json));
            }
            catch (Exception)
            {
                return default;
            }
        }

        [Serializable]
        struct TicketResponse
        {
            public bool skip;
            public string reason;
            public string id;
            public string url;
            public string contentType;
        }

        IEnumerator Send(string path)
        {
            string full = Path.GetFullPath(path);
            var claim = TryClaim(full);
            if (claim == null || !File.Exists(full) || IsDone(full))
            {
                claim?.Dispose();
                _held[full] = Time.unscaledTime + RetryDelaySeconds;
                yield break;
            }

            _uploading = true;
            _uploadPath = full;
            bool answered = false;
            try
            {
                long size = new FileInfo(path).Length;
                var link = ReadLink(path);
                var ticket = new TicketRequest
                {
                    fileName = Path.GetFileName(path), size = size, recordingId = link.recordingId ?? "",
                    kind = link.part ? "part" : "main", player = link.part ? link.player : -1, token = link.token ?? ""
                };

                TicketResponse response = default;
                bool ok = false;
                yield return PostJson(_endpoint + "/uploads", JsonUtility.ToJson(ticket), (code, text) =>
                {
                    if (code == 200 || code == 201)
                    {
                        response = JsonUtility.FromJson<TicketResponse>(text);
                        ok = true;
                    }
                    else
                    {
                        answered = code > 0;
                        HandleRefusal(path, code, text);
                    }
                });
                if (!ok)
                    yield break;

                if (response.skip || string.IsNullOrEmpty(response.url))
                {
                    answered = true;
                    Finish(path);
                    yield break;
                }

                string contentType = string.IsNullOrEmpty(response.contentType) ? "application/octet-stream" : response.contentType;
                bool sent;
                double pacedSeconds = PacedSeconds(full);
                if (pacedSeconds > 0)
                {
                    var paced = _paced = new PacedUpload(full, response.url, contentType, size, size / pacedSeconds);
                    while (!paced.done)
                        yield return null;
                    _paced = null;
                    sent = paced.sent;
                    if (!sent)
                        _unpaced.Add(full);
                }
                else
                {
                    using var put = new UnityWebRequest(response.url, UnityWebRequest.kHttpVerbPUT);
                    _request = put;
                    put.uploadHandler = new UploadHandlerFile(path);
                    put.downloadHandler = new DownloadHandlerBuffer();
                    put.SetRequestHeader("Content-Type", contentType);
                    yield return put.SendWebRequest();
                    _request = null;
                    sent = put.result == UnityWebRequest.Result.Success;
                }

                if (!sent)
                {
                    _retryAt = Time.unscaledTime + RetryDelaySeconds;
                    yield break;
                }

                bool completed = false;
                yield return PostJson($"{_endpoint}/uploads/{UnityWebRequest.EscapeURL(response.id)}/complete", "{}", (code, text) =>
                {
                    if (code == 200)
                        completed = true;
                    else
                    {
                        answered = code > 0;
                        HandleRefusal(path, code, text);
                    }
                });
                if (completed)
                {
                    answered = true;
                    Finish(path);
                }
            }
            finally
            {
                _request = null;
                _paced?.Abort();
                _paced = null;
                _uploading = false;
                if (answered)
                    _uploadPath = null;
                claim.Dispose();
            }
        }

        /// <summary>
        /// Over how many seconds a file is sent, or 0 for as fast as the connection takes it: that is how an
        /// offline game sends, and how a file is sent again whose paced upload did not arrive.
        /// </summary>
        double PacedSeconds(string full)
        {
            if (IsIdle() || _unpaced.Contains(full) || !_recordedSeconds.TryGetValue(full, out float recorded))
                return 0;
            return Math.Min(recorded * sendingShare, LongestPacedSeconds);
        }

        /// <summary>
        /// Sends a file to its upload link at a set pace, from a thread of its own: the engine's own requests can
        /// only send as fast as the connection allows, which a running server must not do next to its players.
        /// </summary>
        sealed class PacedUpload
        {
            const int Slice = 16 * 1024;

            readonly string _path, _url, _contentType;
            readonly long _size;
            readonly double _bytesPerSecond;
            volatile bool _aborted;
            volatile System.Net.HttpWebRequest _request;

            public volatile bool done;
            public bool sent;

            public PacedUpload(string path, string url, string contentType, long size, double bytesPerSecond)
            {
                _path = path;
                _url = url;
                _contentType = contentType;
                _size = size;
                _bytesPerSecond = bytesPerSecond;
                new Thread(Run) { IsBackground = true, Name = "PurrNet recording upload", Priority = System.Threading.ThreadPriority.BelowNormal }.Start();
            }

            public void Abort()
            {
                _aborted = true;
                try
                {
                    _request?.Abort();
                }
                catch (Exception)
                {
                }
            }

            void Run()
            {
                try
                {
                    var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(_url);
                    request.Method = "PUT";
                    request.ContentType = _contentType;
                    request.ContentLength = _size;
                    request.AllowWriteStreamBuffering = false;
                    request.KeepAlive = false;
                    request.ServicePoint.Expect100Continue = false;
                    request.ReadWriteTimeout = 60000;
                    _request = request;

                    var buffer = new byte[Slice];
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    long written = 0;
                    using (var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, Slice))
                    using (var body = request.GetRequestStream())
                    {
                        int read;
                        while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            body.Write(buffer, 0, read);
                            written += read;
                            while (!_aborted)
                            {
                                double ahead = written / _bytesPerSecond - clock.Elapsed.TotalSeconds;
                                if (ahead <= 0)
                                    break;
                                Thread.Sleep((int)Math.Min(250, ahead * 1000 + 1));
                            }
                            if (_aborted)
                                return;
                        }
                    }

                    using var response = (System.Net.HttpWebResponse)request.GetResponse();
                    int status = (int)response.StatusCode;
                    sent = status >= 200 && status < 300;
                }
                catch (Exception)
                {
                    sent = false;
                }
                finally
                {
                    done = true;
                }
            }
        }

        IEnumerator PostJson(string url, string json, Action<long, string> done)
        {
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            _request = request;
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = TicketTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + _projectKey);
            yield return request.SendWebRequest();
            _request = null;
            done(request.responseCode, request.downloadHandler?.text);
        }

        void HandleRefusal(string path, long code, string text)
        {
            bool quota = text != null && text.Contains("quota");
            if (code == 401 || (code == 403 && !quota))
            {
                _blocked = true;
                return;
            }

            if (code > 0)
            {
                _held[Path.GetFullPath(path)] = Time.unscaledTime + RetryDelaySeconds;
                _retryAt = Time.unscaledTime + HeldGapSeconds;
                return;
            }

            _retryAt = Time.unscaledTime + RetryDelaySeconds;
        }

        static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

#endif

        void OnDestroy()
        {
#if !PURRREC_WEB
            _paced?.Abort();
#endif
            if (_request != null)
                _request.Abort();
            if (_instance == this)
                _instance = null;
        }
    }
}
