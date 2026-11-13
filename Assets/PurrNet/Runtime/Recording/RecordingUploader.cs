#if UNITY_WEBGL && !UNITY_EDITOR
// In a browser the recording worker stores and uploads the files (see WebRecording); this only says when it may.
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
    /// <summary>
    /// Sends finished recordings to the PurrNet recordings service and deletes them once they are sent.
    /// Uploads run only while this game has no network session, one file at a time, so they never compete
    /// with match traffic; an upload cut off by a new session is retried later. The service answers each file
    /// with a short-lived upload URL (the bytes go straight to storage) or with "skip", in which case the file
    /// is deleted unsent. Recordings that cannot be sent are kept up to a size cap, oldest deleted first. The newest
    /// file is sent first, so when recordings are made faster than they can be sent, the folder is trimmed at one
    /// end and sent from the other: what is deleted is what would have waited longest, never the file being sent.
    /// The settings are remembered, so files left by an earlier session are sent from the next launch on,
    /// before any match scene loads.
    /// On WebGL the recordings live in the browser (IndexedDB) and the recording worker sends them with the same
    /// protocol and rules; the service and its storage must allow the game's origin (CORS).
    /// </summary>
    [AddComponentMenu("")]
    public sealed class RecordingUploader : MonoBehaviour
    {
        const string Extension = ".purrrec";
        // Next to a recording: ".sent" once the service has it (it only waits to be deleted), ".lock" while a game
        // is sending it (held open, so it goes away with that game).
        const string SentMarker = ".sent";
        const string ClaimMarker = ".lock";
        const float RetryDelaySeconds = 60f;
        // After the service said "not now" to one file, the pause before the next file is tried.
        const float HeldGapSeconds = 5f;
        const int TicketTimeoutSeconds = 20;
        const string PrefsKey = "PurrDiction.RecordingUploader";

        static RecordingUploader _instance;

        /// <summary>
        /// True on CI machines (GitHub Actions, GitLab, Azure Pipelines, Jenkins, TeamCity, Buildkite and anything that
        /// sets CI). Test and benchmark matches there are not gameplay: the recorder does not start on its own and
        /// nothing is uploaded. An explicit <see cref="RecordingSession.Start"/> still records.
        /// </summary>
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
                // No environment on this platform (a browser): not CI.
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
        // Files the service said "not now" to (a part whose recording has not arrived, a quota), and when each may be
        // tried again: the other files are sent meanwhile.
        readonly Dictionary<string, float> _held = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        // Files the service has answered for that could not be deleted yet: never sent again.
        readonly HashSet<string> _done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool _blocked;
        bool _uploading;
        // The file being sent, or whose sending was cut off by a new session: it goes first next time and is never
        // trimmed.
        string _uploadPath;
#endif
        UnityWebRequest _request;

        /// <summary>
        /// Starts uploading the recordings in <paramref name="directory"/> (and every file finished later).
        /// The first call creates the uploader; later calls update its settings.
        /// </summary>
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

        /// <summary>The recording being written right now, which must not be sent or deleted.</summary>
        public static void SetActiveRecording(string path)
        {
            if (_instance)
                _instance._activePath = path;
        }

        /// <summary>A recording was finished: try sending it soon, and keep the folder under its cap.</summary>
        public static void NotifyFinished()
        {
#if !PURRREC_WEB // the worker trims and sends recordings as they are finished
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
                // A session started: get off the network, the file is sent again later.
                if (!IsIdle() && _request != null)
                    _request.Abort();
                return;
            }

            if (_blocked || Time.unscaledTime < _retryAt || !IsIdle() || string.IsNullOrEmpty(_projectKey))
                return;

            string next = NextPending(out float heldUntil);
            if (next == null)
            {
                // Nothing to send, or only held files: back when the first of them is due.
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
        /// The file to send next: the one whose sending was cut off, else the newest finished file that is not held.
        /// <paramref name="heldUntil"/> is when the first held one is due.
        /// </summary>
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

            // Markers whose recording is gone.
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
                    // The service has it; all that is left is to delete it.
                    Finish(full);
                    continue;
                }
                if (_held.TryGetValue(full, out float due) && Time.unscaledTime < due)
                {
                    heldUntil = Math.Min(heldUntil, due);
                    continue;
                }
                var time = File.GetLastWriteTimeUtc(path);
                // Another game on this machine may share the folder: what it is still writing is not ours to send.
                if (time > newestTime && !BeingWritten(full))
                {
                    newest = path;
                    newestTime = time;
                }
            }
            return newest;
        }

        bool IsDone(string fullPath) => _done.Contains(fullPath) || File.Exists(fullPath + SentMarker);

        /// <summary>
        /// The service has answered for this file: it is deleted. When it cannot be deleted right now (another program
        /// has it open), it is marked as sent, here and next to the file, so it is never sent again; deleting it is
        /// tried again whenever the folder is looked at.
        /// </summary>
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
                // Remembered for this run of the game at least.
            }
        }

        /// <summary>Whether some program has the file open for writing.</summary>
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

        /// <summary>
        /// Claims a file for sending, against other games on this machine that share the folder: a lock file held
        /// open for as long as the sending lasts. Null when another game holds it.
        /// </summary>
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

        /// <summary>
        /// Deletes the oldest finished recordings until the folder fits its cap: never the one being written or the
        /// one being sent, and a client's parts (tiny next to a recording, and of no use once deleted) only when
        /// deleting every other file was not enough.
        /// </summary>
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
            // From the file's HEADER, so the service can tie a client's part to the server's recording.
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

        /// <summary>Reads what links a file to its match from its HEADER record (the first one, never compressed).</summary>
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
                // Another game is sending it, or has just sent it.
                claim?.Dispose();
                _held[full] = Time.unscaledTime + RetryDelaySeconds;
                yield break;
            }

            _uploading = true;
            _uploadPath = full;
            // Cleared when the service has answered for this file; a sending that was cut off keeps it.
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
                    // Not wanted (sampling, a cap, a rejected file): sending it later would get the same answer.
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
            // Wrong key or the service is not enabled for this project: stop until the next launch.
            // A quota is a "not now" (it is also a 403).
            bool quota = text != null && text.Contains("quota");
            if (code == 401 || (code == 403 && !quota))
            {
                if (!_blocked)
                    Debug.LogWarning($"[RecordingUploader] Uploads refused ({code}): {text}");
                _blocked = true;
                return;
            }

            // The service answered "not now" for this file (a part whose recording has not arrived yet, a quota, a
            // busy server): it waits its turn again while the next file is tried after a short pause.
            if (code > 0)
            {
                _held[Path.GetFullPath(path)] = Time.unscaledTime + RetryDelaySeconds;
                _retryAt = Time.unscaledTime + HeldGapSeconds;
                return;
            }

            // No answer at all (no network, the request was cut off): nothing is tried for a while.
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
