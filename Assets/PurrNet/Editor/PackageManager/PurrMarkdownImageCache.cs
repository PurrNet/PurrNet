using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace PurrNet.Editor
{
    /// <summary>
    /// Small, bounded cache for raster images referenced by package README markdown.
    /// GetOrRequest is safe to call from IMGUI on every pass: it only performs dictionary work
    /// and queues new work. Disk I/O and network completion happen asynchronously.
    /// </summary>
    internal sealed class PurrMarkdownImageCache : IDisposable
    {
        internal enum ImageStatus
        {
            Missing,
            Queued,
            Loading,
            Ready,
            Failed,
            Unsupported
        }

        internal readonly struct ImageSnapshot
        {
            public ImageStatus status { get; }
            public Texture2D texture { get; }
            public string error { get; }
            public int width { get; }
            public int height { get; }

            internal ImageSnapshot(ImageStatus status, Texture2D texture, string error, int width, int height)
            {
                this.status = status;
                this.texture = texture;
                this.error = error;
                this.width = width;
                this.height = height;
            }
        }

        private sealed class CacheEntry
        {
            public readonly string Url;
            public ImageStatus Status;
            public Texture2D Texture;
            public string Error;
            public int Width;
            public int Height;
            public long EstimatedBytes;
            public long LastAccess;
            public DateTime LastFailureUtc;
            public UnityWebRequest Request;

            public CacheEntry(string url, long lastAccess)
            {
                Url = url;
                Status = ImageStatus.Missing;
                LastAccess = lastAccess;
            }

            public ImageSnapshot snapshot => new ImageSnapshot(Status, Texture, Error, Width, Height);
        }

        private enum ImageFormat
        {
            Unknown,
            Png,
            Jpeg
        }

        private readonly struct InspectionResult
        {
            public readonly bool Success;
            public readonly ImageFormat Format;
            public readonly int Width;
            public readonly int Height;
            public readonly string Error;
            public readonly bool Unsupported;

            public InspectionResult(bool success, ImageFormat format, int width, int height, string error,
                bool unsupported)
            {
                Success = success;
                Format = format;
                Width = width;
                Height = height;
                Error = error;
                Unsupported = unsupported;
            }
        }

        private readonly struct DownloadResult
        {
            public readonly bool Success;
            public readonly byte[] Bytes;
            public readonly string Error;
            public readonly string RedirectLocation;

            private DownloadResult(bool success, byte[] bytes, string error, string redirectLocation)
            {
                Success = success;
                Bytes = bytes;
                Error = error;
                RedirectLocation = redirectLocation;
            }

            public static DownloadResult Ok(byte[] bytes) => new DownloadResult(true, bytes, null, null);
            public static DownloadResult Fail(string error) => new DownloadResult(false, null, error, null);
            public static DownloadResult Redirect(string location) =>
                new DownloadResult(false, null, null, location);
        }

        /// <summary>
        /// Unity's buffer/texture download handlers may retain an arbitrarily large response before
        /// callers can inspect downloadedBytes. This handler rejects both declared and chunked bodies
        /// as soon as they cross the configured limit.
        /// </summary>
        private sealed class BoundedDownloadHandler : DownloadHandlerScript
        {
            private readonly long _maxBytes;
            private readonly MemoryStream _stream;

            public bool exceededLimit { get; private set; }

            public BoundedDownloadHandler(long maxBytes) : base(new byte[ReceiveBufferBytes])
            {
                _maxBytes = maxBytes;
                _stream = new MemoryStream();
            }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > (ulong)_maxBytes)
                    exceededLimit = true;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                    return true;

                if (exceededLimit || dataLength > _maxBytes - _stream.Length)
                {
                    exceededLimit = true;
                    return false;
                }

                _stream.Write(data, 0, dataLength);
                return true;
            }

            public byte[] GetBytes()
            {
                return _stream.ToArray();
            }

            public override void Dispose()
            {
                _stream.Dispose();
                base.Dispose();
            }
        }

        private const int MaxConcurrentRequests = 3;
        private const int MaxQueuedEntries = 64;
        private const long MaxDownloadBytes = 8L * 1024L * 1024L;
        private const int ReceiveBufferBytes = 64 * 1024;
        private const int MaxDimension = 4096;
        private const long MaxPixels = 8L * 1024L * 1024L;
        private const int MaxMemoryEntries = 48;
        private const int MaxTrackedEntries = 256;
        private const long MaxEstimatedTextureBytes = 96L * 1024L * 1024L;
        private const long MaxDiskBytes = 128L * 1024L * 1024L;
        private const int RequestTimeoutSeconds = 15;
        private const int RedirectLimit = 3;
        private const int FailedRetryMinutes = 5;
        private const int MaxDiskAgeDays = 1;

        private static readonly ImageSnapshot MissingSnapshot =
            new ImageSnapshot(ImageStatus.Missing, null, null, 0, 0);

        private readonly Dictionary<string, CacheEntry> _entries =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        private readonly Queue<CacheEntry> _queue = new Queue<CacheEntry>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly object _diskIoLock = new object();
        private readonly int _mainThreadId;
        private readonly SynchronizationContext _mainContext;
        private readonly string _diskCacheRoot;
        private readonly long _maxDiskBytes;

        private int _activeRequests;
        private int _readyEntries;
        private long _estimatedTextureBytes;
        private long _accessCounter;
        private bool _changeScheduled;
        private bool _disposed;

        public event Action Changed;

        public PurrMarkdownImageCache() : this(null, MaxDiskBytes, true)
        {
        }

        internal PurrMarkdownImageCache(string diskCacheRoot, long maxDiskBytes, bool pruneOnStart)
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _mainContext = SynchronizationContext.Current;
            if (_mainContext == null)
                throw new InvalidOperationException(
                    "PurrMarkdownImageCache must be created on the Unity Editor main thread.");
            _diskCacheRoot = string.IsNullOrWhiteSpace(diskCacheRoot)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PurrNet",
                    "PackageManager", "MarkdownImages", "v1"))
                : Path.GetFullPath(diskCacheRoot);
            _maxDiskBytes = Math.Max(0, maxDiskBytes);

            // Pruning is intentionally fire-and-forget and never reports progress to IMGUI.
            if (pruneOnStart)
                _ = Task.Run(PruneDiskCacheSafe);
        }

        /// <summary>
        /// Returns the current state for a URL and, when requested, queues a missing image.
        /// Callers should pass shouldRequest=false for off-screen images to keep long READMEs responsive.
        /// </summary>
        public ImageSnapshot GetOrRequest(string absoluteUrl, bool shouldRequest)
        {
            if (_disposed)
                return new ImageSnapshot(ImageStatus.Failed, null, "The image cache has been disposed.", 0, 0);

            if (!PurrMarkdownUrl.TryNormalizeImageUrl(absoluteUrl, out var normalized, out var validationError))
                return new ImageSnapshot(ImageStatus.Unsupported, null, validationError, 0, 0);

            if (!_entries.TryGetValue(normalized, out var entry))
            {
                if (!shouldRequest)
                    return MissingSnapshot;

                entry = new CacheEntry(normalized, ++_accessCounter);
                _entries.Add(normalized, entry);
                QueueEntry(entry);
                TrimTrackedEntries();
                return entry.snapshot;
            }

            entry.LastAccess = ++_accessCounter;

            if (shouldRequest && entry.Status == ImageStatus.Missing)
            {
                QueueEntry(entry);
            }
            else if (shouldRequest && entry.Status == ImageStatus.Failed &&
                     DateTime.UtcNow - entry.LastFailureUtc >= TimeSpan.FromMinutes(FailedRetryMinutes))
            {
                QueueEntry(entry);
            }

            return entry.snapshot;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId && _mainContext != null)
            {
                _mainContext.Post(_ =>
                {
                    _lifetime.Cancel();
                    DisposeUnityResources();
                }, null);
                return;
            }

            _lifetime.Cancel();
            DisposeUnityResources();
        }

        private void DisposeUnityResources()
        {
            EditorApplication.delayCall -= RaiseChanged;
            _changeScheduled = false;

            foreach (var entry in _entries.Values)
            {
                try
                {
                    entry.Request?.Abort();
                }
                catch
                {
                    // Best effort during window/domain shutdown.
                }

                entry.Request = null;
                DestroyTexture(entry);
            }

            _queue.Clear();
            _entries.Clear();
            _readyEntries = 0;
            _estimatedTextureBytes = 0;
            Changed = null;
            _lifetime.Dispose();
        }

        private void QueueEntry(CacheEntry entry)
        {
            if (_disposed || entry.Status == ImageStatus.Queued || entry.Status == ImageStatus.Loading)
                return;

            // A malicious or image-heavy README must not create an unbounded pending-work list.
            // Leaving the entry Missing lets a later visible GetOrRequest retry after the queue drains.
            if (_queue.Count >= MaxQueuedEntries)
                return;

            entry.Status = ImageStatus.Queued;
            entry.Error = null;
            _queue.Enqueue(entry);
            PumpQueue();
        }

        private void PumpQueue()
        {
            while (!_disposed && _activeRequests < MaxConcurrentRequests && _queue.Count > 0)
            {
                var entry = _queue.Dequeue();
                if (entry.Status != ImageStatus.Queued)
                    continue;

                _activeRequests++;
                _ = ProcessEntryAsync(entry);
            }
        }

        private async Task ProcessEntryAsync(CacheEntry entry)
        {
            try
            {
                byte[] diskBytes = null;
                try
                {
                    diskBytes = await Task.Run(() => ReadDiskEntrySafe(entry.Url), _lifetime.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (_disposed || _lifetime.IsCancellationRequested)
                    return;

                if (diskBytes != null)
                {
                    var diskInspection = InspectImage(diskBytes);
                    if (diskInspection.Success && TryDecode(entry, diskBytes, diskInspection, out _))
                    {
                        SetReady(entry, diskInspection.Width, diskInspection.Height);
                        return;
                    }

                    // Corrupt or obsolete cache data should not make the URL permanently fail.
                    _ = Task.Run(() => DeleteDiskEntrySafe(entry.Url));
                }

                entry.Status = ImageStatus.Loading;
                NotifyChanged();

                var download = await DownloadBytesAsync(entry, _lifetime.Token);
                if (_disposed || _lifetime.IsCancellationRequested)
                    return;

                if (!download.Success)
                {
                    SetFailed(entry, download.Error);
                    return;
                }

                var inspection = InspectImage(download.Bytes);
                if (!inspection.Success)
                {
                    if (inspection.Unsupported)
                        SetUnsupported(entry, inspection.Error);
                    else
                        SetFailed(entry, inspection.Error);
                    return;
                }

                if (!TryDecode(entry, download.Bytes, inspection, out var decodeError))
                {
                    SetFailed(entry, decodeError);
                    return;
                }

                SetReady(entry, inspection.Width, inspection.Height);
                _ = Task.Run(() => WriteDiskEntrySafe(entry.Url, download.Bytes));
            }
            catch (OperationCanceledException)
            {
                // Closing the window/domain is not a user-visible image failure.
            }
            catch (Exception e)
            {
                if (!_disposed)
                    SetFailed(entry, $"Could not load image: {e.Message}");
            }
            finally
            {
                _activeRequests = Math.Max(0, _activeRequests - 1);
                PumpQueue();
            }
        }

        private async Task<DownloadResult> DownloadBytesAsync(CacheEntry entry, CancellationToken cancellationToken)
        {
            string currentUrl = entry.Url;
            for (int redirectCount = 0; ; redirectCount++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!PurrMarkdownUrl.TryNormalizeImageUrl(currentUrl, out string normalizedUrl,
                        out string validationError))
                    return DownloadResult.Fail(validationError);
                currentUrl = normalizedUrl;

                string dnsError = await ValidateResolvedHostAsync(currentUrl, cancellationToken);
                if (!string.IsNullOrEmpty(dnsError))
                    return DownloadResult.Fail(dnsError);

                var result = await DownloadSingleRequestAsync(entry, currentUrl, cancellationToken);
                if (string.IsNullOrEmpty(result.RedirectLocation))
                    return result;

                if (redirectCount >= RedirectLimit)
                    return DownloadResult.Fail("Image redirect limit exceeded.");
                if (!PurrMarkdownUrl.TryResolveImageRedirect(currentUrl, result.RedirectLocation,
                        out string redirectedUrl, out string redirectError))
                    return DownloadResult.Fail(redirectError);
                currentUrl = redirectedUrl;
            }
        }

        private static async Task<string> ValidateResolvedHostAsync(string absoluteUrl,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
                return "Image URL is invalid.";

            // DNS validation closes the normal private-address path, but UnityWebRequest
            // performs its own resolution afterward. A first-party proxy that pins the
            // validated destination is still needed to eliminate DNS-rebinding TOCTOU fully.
            try
            {
                Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(uri.DnsSafeHost);
                Task timeout = Task.Delay(TimeSpan.FromSeconds(RequestTimeoutSeconds), cancellationToken);
                if (await Task.WhenAny(lookup, timeout) != lookup)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return "Image host DNS lookup timed out.";
                }

                var addresses = await lookup;
                cancellationToken.ThrowIfCancellationRequested();
                return PurrMarkdownUrl.TryValidatePublicAddresses(addresses, out string error) ? null : error;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return $"Image host DNS lookup failed: {e.Message}";
            }
        }

        private async Task<DownloadResult> DownloadSingleRequestAsync(CacheEntry entry, string url,
            CancellationToken cancellationToken)
        {
            BoundedDownloadHandler handler = null;
            UnityWebRequest request = null;

            try
            {
                handler = new BoundedDownloadHandler(MaxDownloadBytes);
                request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET)
                {
                    downloadHandler = handler,
                    timeout = RequestTimeoutSeconds,
                    // Redirect targets must go back through URL normalization and DNS checks.
                    redirectLimit = 0
                };
                request.SetRequestHeader("Accept", "image/png,image/jpeg;q=0.9,*/*;q=0.1");
                entry.Request = request;

                var completion = new TaskCompletionSource<bool>();
                var operation = request.SendWebRequest();
                operation.completed += _ => completion.TrySetResult(true);
                if (operation.isDone)
                    completion.TrySetResult(true);

                using (cancellationToken.Register(() =>
                       {
                           try
                           {
                               request.Abort();
                           }
                           catch
                           {
                               // Request may already have completed or been disposed.
                           }

                           completion.TrySetCanceled();
                       }))
                {
                    await completion.Task;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (handler.exceededLimit)
                    return DownloadResult.Fail("Image exceeds the 8 MiB download limit.");

                if (request.responseCode >= 300 && request.responseCode <= 399)
                {
                    string location = request.GetResponseHeader("Location");
                    return string.IsNullOrWhiteSpace(location)
                        ? DownloadResult.Fail($"Image redirect (HTTP {request.responseCode}) has no Location header.")
                        : DownloadResult.Redirect(location);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    var response = request.responseCode > 0 ? $" (HTTP {request.responseCode})" : string.Empty;
                    return DownloadResult.Fail($"Image request failed{response}: {request.error}");
                }

                var bytes = handler.GetBytes();
                if (bytes.Length == 0)
                    return DownloadResult.Fail("Image response was empty.");

                return DownloadResult.Ok(bytes);
            }
            finally
            {
                entry.Request = null;
                request?.Dispose();
                // UnityWebRequest owns and disposes its download handler. Dispose it directly only
                // when request construction failed before ownership was transferred.
                if (request == null)
                    handler?.Dispose();
            }
        }

        private bool TryDecode(CacheEntry entry, byte[] bytes, InspectionResult inspection, out string error)
        {
            error = null;
            Texture2D texture = null;

            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "PurrNet README Image"
                };

                if (!ImageConversion.LoadImage(texture, bytes, true))
                {
                    error = "Unity could not decode this PNG/JPEG image.";
                    return false;
                }

                if (texture.width <= 0 || texture.height <= 0 || texture.width > MaxDimension ||
                    texture.height > MaxDimension || (long)texture.width * texture.height > MaxPixels)
                {
                    error = "Decoded image dimensions exceed the cache safety limit.";
                    return false;
                }

                DestroyTexture(entry);
                entry.Texture = texture;
                texture = null;
                entry.Width = inspection.Width;
                entry.Height = inspection.Height;
                return true;
            }
            catch (Exception e)
            {
                error = $"Unity could not decode this image: {e.Message}";
                return false;
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private void SetReady(CacheEntry entry, int width, int height)
        {
            entry.Status = ImageStatus.Ready;
            entry.Error = null;
            entry.Width = width;
            entry.Height = height;
            entry.EstimatedBytes = Math.Max(1L, (long)width * height * 4L);
            entry.LastAccess = ++_accessCounter;
            _readyEntries++;
            _estimatedTextureBytes += entry.EstimatedBytes;
            TrimMemory(entry);
            NotifyChanged();
        }

        private void SetFailed(CacheEntry entry, string error)
        {
            DestroyTexture(entry);
            entry.Status = ImageStatus.Failed;
            entry.Error = string.IsNullOrEmpty(error) ? "Could not load image." : error;
            entry.LastFailureUtc = DateTime.UtcNow;
            NotifyChanged();
        }

        private void SetUnsupported(CacheEntry entry, string error)
        {
            DestroyTexture(entry);
            entry.Status = ImageStatus.Unsupported;
            entry.Error = string.IsNullOrEmpty(error) ? "Unsupported image." : error;
            NotifyChanged();
        }

        private void TrimMemory(CacheEntry newest)
        {
            while (_readyEntries > MaxMemoryEntries || _estimatedTextureBytes > MaxEstimatedTextureBytes)
            {
                CacheEntry oldest = null;
                foreach (var candidate in _entries.Values)
                {
                    if (candidate == newest || candidate.Texture == null || candidate.Status != ImageStatus.Ready)
                        continue;

                    if (oldest == null || candidate.LastAccess < oldest.LastAccess)
                        oldest = candidate;
                }

                // A single accepted image is always smaller than the memory cap; this only occurs
                // when newest is the sole ready entry, so keep it rather than destroying it instantly.
                if (oldest == null)
                    break;

                DestroyTexture(oldest);
                oldest.Status = ImageStatus.Missing;
                oldest.Error = null;
            }
        }

        private void TrimTrackedEntries()
        {
            while (_entries.Count > MaxTrackedEntries)
            {
                CacheEntry oldest = null;
                foreach (var candidate in _entries.Values)
                {
                    if (candidate.Status == ImageStatus.Queued || candidate.Status == ImageStatus.Loading ||
                        candidate.Status == ImageStatus.Ready)
                        continue;

                    if (oldest == null || candidate.LastAccess < oldest.LastAccess)
                        oldest = candidate;
                }

                if (oldest == null)
                    break;
                _entries.Remove(oldest.Url);
            }
        }

        private void DestroyTexture(CacheEntry entry)
        {
            if (entry.Texture == null)
                return;

            UnityEngine.Object.DestroyImmediate(entry.Texture);
            entry.Texture = null;
            _readyEntries = Math.Max(0, _readyEntries - 1);
            _estimatedTextureBytes = Math.Max(0L, _estimatedTextureBytes - entry.EstimatedBytes);
            entry.EstimatedBytes = 0;
        }

        private void NotifyChanged()
        {
            if (_disposed || _changeScheduled)
                return;

            _changeScheduled = true;
            EditorApplication.delayCall += RaiseChanged;
        }

        private void RaiseChanged()
        {
            EditorApplication.delayCall -= RaiseChanged;
            _changeScheduled = false;
            if (!_disposed)
                Changed?.Invoke();
        }

        private static InspectionResult InspectImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return Invalid("Image response was empty.");
            if (bytes.LongLength > MaxDownloadBytes)
                return Unsupported("Image exceeds the 8 MiB download limit.");

            if (TryReadPngDimensions(bytes, out var pngWidth, out var pngHeight, out var pngRecognized))
                return ValidateDimensions(ImageFormat.Png, pngWidth, pngHeight);
            if (pngRecognized)
                return Invalid("PNG header is corrupt or incomplete.");

            if (TryReadJpegDimensions(bytes, out var jpegWidth, out var jpegHeight, out var jpegRecognized))
                return ValidateDimensions(ImageFormat.Jpeg, jpegWidth, jpegHeight);
            if (jpegRecognized)
                return Invalid("JPEG header is corrupt or incomplete.");

            return Unsupported("Only PNG and JPEG README images are supported.");
        }

        private static InspectionResult ValidateDimensions(ImageFormat format, int width, int height)
        {
            if (width <= 0 || height <= 0)
                return Invalid("Image has invalid dimensions.");
            if (width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
                return Unsupported($"Image is too large ({width}x{height}); maximum is {MaxDimension}px and 8M pixels.");

            return new InspectionResult(true, format, width, height, null, false);
        }

        private static InspectionResult Invalid(string error)
        {
            return new InspectionResult(false, ImageFormat.Unknown, 0, 0, error, false);
        }

        private static InspectionResult Unsupported(string error)
        {
            return new InspectionResult(false, ImageFormat.Unknown, 0, 0, error, true);
        }

        private static bool TryReadPngDimensions(byte[] bytes, out int width, out int height, out bool recognized)
        {
            width = 0;
            height = 0;
            recognized = bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e &&
                         bytes[3] == 0x47 && bytes[4] == 0x0d && bytes[5] == 0x0a && bytes[6] == 0x1a &&
                         bytes[7] == 0x0a;
            if (!recognized || bytes.Length < 24 || bytes[12] != 0x49 || bytes[13] != 0x48 ||
                bytes[14] != 0x44 || bytes[15] != 0x52)
                return false;

            width = ReadBigEndianInt32(bytes, 16);
            height = ReadBigEndianInt32(bytes, 20);
            return width > 0 && height > 0;
        }

        private static bool TryReadJpegDimensions(byte[] bytes, out int width, out int height, out bool recognized)
        {
            width = 0;
            height = 0;
            recognized = bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xd8;
            if (!recognized)
                return false;

            var offset = 2;
            while (offset + 3 < bytes.Length)
            {
                while (offset < bytes.Length && bytes[offset] != 0xff)
                    offset++;
                while (offset < bytes.Length && bytes[offset] == 0xff)
                    offset++;
                if (offset >= bytes.Length)
                    return false;

                var marker = bytes[offset++];
                if (marker == 0xd9 || marker == 0xda)
                    return false;
                if (marker == 0x01 || marker >= 0xd0 && marker <= 0xd7)
                    continue;
                if (offset + 1 >= bytes.Length)
                    return false;

                var segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
                if (segmentLength < 2 || offset > bytes.Length - segmentLength)
                    return false;

                if (IsJpegStartOfFrame(marker) && segmentLength >= 7)
                {
                    height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                    width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                    return width > 0 && height > 0;
                }

                offset += segmentLength;
            }

            return false;
        }

        private static bool IsJpegStartOfFrame(byte marker)
        {
            return marker >= 0xc0 && marker <= 0xc3 || marker >= 0xc5 && marker <= 0xc7 ||
                   marker >= 0xc9 && marker <= 0xcb || marker >= 0xcd && marker <= 0xcf;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            var value = (uint)bytes[offset] << 24 | (uint)bytes[offset + 1] << 16 |
                        (uint)bytes[offset + 2] << 8 | bytes[offset + 3];
            return value > int.MaxValue ? -1 : (int)value;
        }

        private byte[] ReadDiskEntrySafe(string url)
        {
            lock (_diskIoLock)
            {
                try
                {
                    var path = GetDiskPath(url);
                    if (!File.Exists(path))
                        return null;

                    var info = new FileInfo(path);
                    if (info.Length <= 0 || info.Length > MaxDownloadBytes ||
                        DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(MaxDiskAgeDays))
                    {
                        File.Delete(path);
                        return null;
                    }

                    var bytes = File.ReadAllBytes(path);
                    try
                    {
                        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                    }
                    catch
                    {
                        // Timestamp is only an LRU hint.
                    }

                    return bytes;
                }
                catch
                {
                    return null;
                }
            }
        }

        internal void WriteDiskEntrySafe(string url, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.LongLength > MaxDownloadBytes)
                return;

            lock (_diskIoLock)
            {
                string tempPath = null;
                try
                {
                    Directory.CreateDirectory(_diskCacheRoot);
                    var path = GetDiskPath(url);
                    tempPath = Path.Combine(_diskCacheRoot,
                        $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
                    File.WriteAllBytes(tempPath, bytes);

                    if (File.Exists(path))
                        File.Replace(tempPath, path, null);
                    else
                        File.Move(tempPath, path);

                    tempPath = null;
                    // Enforce the cap after every successful write. Constructor-only pruning
                    // allowed one image-heavy browsing session to grow without a bound.
                    PruneDiskCacheCore();
                }
                catch
                {
                    // Disk caching is optional and must never affect rendering.
                }
                finally
                {
                    if (tempPath != null)
                        TryDeleteFile(tempPath);
                }
            }
        }

        private void DeleteDiskEntrySafe(string url)
        {
            lock (_diskIoLock)
            {
                try
                {
                    var path = GetDiskPath(url);
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // A bad disk entry simply remains until the next prune.
                }
            }
        }

        private void PruneDiskCacheSafe()
        {
            lock (_diskIoLock)
            {
                try
                {
                    PruneDiskCacheCore();
                }
                catch
                {
                    // Disk caching is optional.
                }
            }
        }

        private void PruneDiskCacheCore()
        {
            if (!Directory.Exists(_diskCacheRoot))
                return;

            var files = new List<FileInfo>();
            long totalBytes = 0;
            var now = DateTime.UtcNow;
            foreach (var path in Directory.GetFiles(_diskCacheRoot, "*.img", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxDownloadBytes ||
                    now - info.LastWriteTimeUtc > TimeSpan.FromDays(MaxDiskAgeDays))
                {
                    TryDeleteFile(path);
                    continue;
                }

                files.Add(info);
                totalBytes += info.Length;
            }

            if (totalBytes <= _maxDiskBytes)
                return;

            files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            foreach (var file in files)
            {
                if (totalBytes <= _maxDiskBytes)
                    break;
                if (TryDeleteFile(file.FullName))
                    totalBytes -= file.Length;
            }
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string GetDiskPath(string url)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
            var name = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
                name.Append(value.ToString("x2"));
            return Path.Combine(_diskCacheRoot, name + ".img");
        }
    }
}
