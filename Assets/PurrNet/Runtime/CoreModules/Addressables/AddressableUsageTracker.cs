#if ADDRESSABLES_PURRNET_SUPPORT
using System.Collections.Generic;
using PurrNet.Utils;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

namespace PurrNet.Modules
{
    /// <summary>
    /// Knows which addressable network prefabs this peer still needs and releases the others.
    /// A prefab is needed while a spawned identity uses it or while someone holds it through
    /// <see cref="Retain"/>. Once neither is true it is released after the delay set in the network
    /// rules, provided <c>releaseWhenLastDespawned</c> is on.
    /// <para>
    /// A connected client doesn't release on its own: it first tells the server, and waits for the
    /// answer. Any spawn the server sent while it still believed the prefab was loaded arrives before
    /// that answer, so a spawn never finds its prefab gone.
    /// </para>
    /// </summary>
    public sealed class AddressableUsageTracker
    {
        // A prefab loaded on request (the server announcing a spawn, or game code warming it up) has no
        // instance yet. Give the spawn time to arrive before treating the prefab as unused.
        private const float MIN_UNUSED_LOAD_GRACE = 10f;

        private readonly NetworkManager _manager;
        private readonly Dictionary<(ObjectId instance, bool asServer), string> _guidByInstance = new();
        private readonly Dictionary<string, int> _instances = new();
        private readonly Dictionary<string, int> _holds = new();
        private readonly Dictionary<string, float> _releaseAt = new();
        private readonly HashSet<string> _awaitingServer = new();
        private readonly List<string> _due = new();
        private IAddressablePrefabProvider _provider;

        /// <summary>Seconds since startup. Replaced by tests.</summary>
        internal System.Func<float> clock = () => Time.unscaledTime;

        internal AddressableUsageTracker(NetworkManager manager)
        {
            _manager = manager;
        }

        internal void Bind(IPrefabProvider provider)
        {
            if (_provider != null)
                _provider.loadStateChanged -= OnLoadStateChanged;

            _provider = provider as IAddressablePrefabProvider;

            if (_provider != null)
                _provider.loadStateChanged += OnLoadStateChanged;
        }

        internal void Track(HierarchyFactory hierarchy, bool asServer)
        {
            hierarchy.onIdentityAdded += identity => OnIdentityAdded(identity, asServer);
            hierarchy.onIdentityRemoved += identity => OnIdentityRemoved(identity, asServer);
        }

        /// <summary>
        /// True while a spawned identity uses the prefab or someone holds it.
        /// </summary>
        public bool IsInUse(string assetGuid)
        {
            return !string.IsNullOrEmpty(assetGuid) &&
                   (_instances.ContainsKey(assetGuid) || _holds.ContainsKey(assetGuid));
        }

        /// <summary>
        /// Keeps the prefab from being released until <see cref="Release"/> is called as many times.
        /// It doesn't load the prefab.
        /// </summary>
        public void Retain(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid))
                return;

            _holds.TryGetValue(assetGuid, out int holds);
            _holds[assetGuid] = holds + 1;
            _releaseAt.Remove(assetGuid);
        }

        public void Release(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid) || !_holds.TryGetValue(assetGuid, out int holds))
                return;

            if (holds > 1)
            {
                _holds[assetGuid] = holds - 1;
                return;
            }

            _holds.Remove(assetGuid);
            ScheduleIfUnused(assetGuid, 0f);
        }

        internal void OnIdentityAdded(NetworkIdentity identity, bool asServer)
        {
            if (_provider == null || !identity ||
                !_manager.prefabResolver.TryGetAddressableGuid(identity.scopedPrefabId, out var guid))
                return;

            if (!_guidByInstance.TryAdd((PurrObjectId.Of(identity), asServer), guid))
                return;

            _instances.TryGetValue(guid, out int count);
            _instances[guid] = count + 1;
            _releaseAt.Remove(guid);
        }

        internal void OnIdentityRemoved(NetworkIdentity identity, bool asServer)
        {
            if (_guidByInstance.Count == 0 || ReferenceEquals(identity, null) ||
                !_guidByInstance.Remove((PurrObjectId.Of(identity), asServer), out var guid))
                return;

            if (_instances.TryGetValue(guid, out int count) && count > 1)
            {
                _instances[guid] = count - 1;
                return;
            }

            _instances.Remove(guid);
            ScheduleIfUnused(guid, 0f);
        }

        private void OnLoadStateChanged(string guid, bool loaded)
        {
            if (loaded)
                ScheduleIfUnused(guid, MIN_UNUSED_LOAD_GRACE);
            else
                _releaseAt.Remove(guid);
        }

        private void ScheduleIfUnused(string guid, float minDelay)
        {
            if (IsInUse(guid) || _awaitingServer.Contains(guid))
                return;

            var rules = _manager.networkRules;
            if (!rules || !rules.AddressablesReleaseWhenLastDespawned)
                return;

            _releaseAt[guid] = clock() + Mathf.Max(minDelay, rules.AddressablesReleaseDelay);
        }

        internal void Update()
        {
            if (_releaseAt.Count == 0)
                return;

            float now = clock();

            foreach (var (guid, time) in _releaseAt)
            {
                if (time <= now)
                    _due.Add(guid);
            }

            for (int i = 0; i < _due.Count; i++)
            {
                var guid = _due[i];
                _releaseAt.Remove(guid);

                if (IsInUse(guid) || _provider == null)
                    continue;

                if (_manager.TryGetModule<AddressablesSyncModule>(false, out var sync) &&
                    sync.TryAnnounceRelease(guid))
                {
                    _awaitingServer.Add(guid);
                    continue;
                }

                _provider.ReleasePrefabByGuid(guid);
            }

            _due.Clear();
        }

        /// <summary>
        /// The server stopped counting on this prefab. Returns true when the prefab should now be
        /// released, false when it came back into use meanwhile and must be reported as loaded again.
        /// </summary>
        internal bool OnServerAcknowledgedRelease(string guid, out bool wasExpected)
        {
            wasExpected = _awaitingServer.Remove(guid);
            return wasExpected && !IsInUse(guid);
        }

        internal void ReleaseNow(string guid)
        {
            _provider?.ReleasePrefabByGuid(guid);
        }

        /// <summary>
        /// The connection to the server is gone, so nothing is left to wait for.
        /// </summary>
        internal void OnClientSyncStopped()
        {
            foreach (var guid in _awaitingServer)
                _releaseAt[guid] = 0f;

            _awaitingServer.Clear();
        }

        internal void Dispose()
        {
            Bind(null);
            _guidByInstance.Clear();
            _instances.Clear();
            _holds.Clear();
            _releaseAt.Clear();
            _awaitingServer.Clear();
        }
    }
}
#endif
