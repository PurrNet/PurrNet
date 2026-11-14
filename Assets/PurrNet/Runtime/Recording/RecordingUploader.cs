#if UNITY_WEBGL && !UNITY_EDITOR
#define PURRREC_WEB
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PurrNet;
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
        string _activePath;
#if !PURRREC_WEB
        float _retryAt;
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

        public static void SetActiveRecording(string path)
        {
            if (_instance)
                _instance._activePath = path;
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
            if (_uploading)
            {
                if (!IsIdle() && _request != null)
                    _request.Abort();
                return;
            }

            if (_blocked || Time.unscaledTime < _retryAt || !IsIdle() || string.IsNullOrEmpty(_projectKey))
                return;

            string next = NextPending(out float heldUntil);
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

        bool IsActive(string path) =>
            !string.IsNullOrEmpty(_activePath) &&
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(_activePath), StringComparison.OrdinalIgnoreCase);

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

                using (var put = new UnityWebRequest(response.url, UnityWebRequest.kHttpVerbPUT))
                {
                    _request = put;
                    put.uploadHandler = new UploadHandlerFile(path);
                    put.downloadHandler = new DownloadHandlerBuffer();
                    put.SetRequestHeader("Content-Type", string.IsNullOrEmpty(response.contentType)
                        ? "application/octet-stream" : response.contentType);
                    yield return put.SendWebRequest();
                    _request = null;
                    if (put.result != UnityWebRequest.Result.Success)
                    {
                        _retryAt = Time.unscaledTime + RetryDelaySeconds;
                        yield break;
                    }
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
                _uploading = false;
                if (answered)
                    _uploadPath = null;
                claim.Dispose();
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
            if (_request != null)
                _request.Abort();
            if (_instance == this)
                _instance = null;
        }
    }
}
