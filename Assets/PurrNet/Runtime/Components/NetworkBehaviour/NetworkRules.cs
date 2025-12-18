using System;
using JetBrains.Annotations;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.Serialization;

namespace PurrNet
{

    [Serializable]
    public struct HostMigrationRules
    {
        [UsedImplicitly] public bool enabled;
        [Tooltip("If enabled, new server will also start as client (server+client)")]
        [UsedImplicitly] public bool migrateAsHost;
        [UsedImplicitly] public bool identitiesAlwaysVisible;
        [UsedImplicitly] public bool scenesAlwaysPublic;
    }

    public enum SceneCleanupMode
    {
        /// <summary>
        /// Do not cleanup any scenes or reload the starting scene on disconnect.
        /// </summary>
        Off,

        /// <summary>
        /// Cleanup scenes that were loaded through the network, and reload the starting scene additively.
        /// </summary>
        OnlineOnly,

        /// <summary>
        /// Cleanup all scenes and reload the starting scene in single mode.
        /// </summary>
        All
    }

    [Serializable]
    public struct VisibilityRules
    {
        [UsedImplicitly] public VisibilityMode visibilityMode;
    }

    [Serializable]
    public struct RpcRules
    {
        [UsedImplicitly]
        [Tooltip(
            "This allows client to call any ObserversRpc or TargetRpc without the need to set requireServer to false")]
        public bool ignoreRequireServerAttribute;

        [UsedImplicitly]
        [Tooltip("This allows client to call any OwnerRpc without the need to set requireOwner to false")]
        public bool ignoreRequireOwnerAttribute;

        [UsedImplicitly]
        [Tooltip("This allows client to use a TargetRpc as a ServerRpc")]
        public bool targetRpcsCanTargetServer;
    }

    [Serializable]
    public struct SpawnRules
    {
        public ConnectionAuth spawnAuth;
        public ActionAuth despawnAuth;

        [Tooltip("Who gains ownership upon spawning of the identity")]
        public DefaultOwner defaultOwner;

        [Tooltip("Propagate ownership to all children of the object")]
        public bool propagateOwnershipByDefault;

        [Tooltip("If owner disconnects, should the object despawn or stay in the scene?")]
        public bool despawnIfOwnerDisconnects;

        [Tooltip("On disconnect, despawn all objects that were spawned during the session")]
        public bool cleanupSpawnedObjects;
    }

    [Serializable]
    public struct OwnershipRules
    {
        [Tooltip("Who can assign ownership to objects")]
        public ConnectionAuth assignAuth;

        [Tooltip("Who can transfer existing ownership from objects")]
        public ActionAuth transferAuth;

        [Tooltip("Who can remove ownership from objects")]
        public ActionAuth removeAuth;

        [Tooltip("If object already has an owner, should the new owner override the existing owner?")]
        public bool overrideWhenPropagating;
    }

    [Serializable]
    public struct NetworkSceneRules : ISerializationCallbackReceiver
    {
        [FormerlySerializedAs("cleanupScenesOnDisconnect")]
        [SerializeField, HideInInspector]
        private bool _cleanupScenesOnDisconnect;

        public bool removePlayerFromSceneOnDisconnect;

        [Tooltip("On disconnect, unload scenes and reload the starting scene based on the selected cleanup mode")]
        public SceneCleanupMode sceneCleanupModeOnDisconnect;

        public bool alwaysIncludeDontDestroyOnLoadScene;

        [Obsolete("Use sceneCleanupModeOnDisconnect instead.")]
        public bool cleanupScenesOnDisconnect
        {
            readonly get => sceneCleanupModeOnDisconnect == SceneCleanupMode.OnlineOnly;
            set => sceneCleanupModeOnDisconnect = value ? SceneCleanupMode.OnlineOnly : SceneCleanupMode.Off;
        }

        public readonly void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            if (_cleanupScenesOnDisconnect && sceneCleanupModeOnDisconnect != SceneCleanupMode.OnlineOnly)
            {
                sceneCleanupModeOnDisconnect = SceneCleanupMode.OnlineOnly;
                _cleanupScenesOnDisconnect = false;
            }
        }
    }

    [Serializable]
    public struct NetworkIdentityRules
    {
        public bool receiveRpcsWhenDisabled;
    }

    [Serializable]
    public struct NetworkTransformRules
    {
        public ActionAuth changeParentAuth;
    }

    [Serializable]
    public struct MiscRules
    {
        [Range(1, 10)] public int syncedTickUpdateInterval;
    }

    [CreateAssetMenu(fileName = "NetworkRules", menuName = "PurrNet/Network Rules", order = -201)]
    public class NetworkRules : ScriptableObject
    {
        [SerializeField]
        private HostMigrationRules _hostMigrationRules = new HostMigrationRules
        {
            enabled = false,
            migrateAsHost = true,
            identitiesAlwaysVisible = true,
            scenesAlwaysPublic = true
        };

        [SerializeField]
        private SpawnRules _defaultSpawnRules = new SpawnRules
        {
            despawnAuth = ActionAuth.Server | ActionAuth.Owner,
            spawnAuth = ConnectionAuth.Server,
            defaultOwner = DefaultOwner.SpawnerIfClientOnly,
            propagateOwnershipByDefault = true,
            despawnIfOwnerDisconnects = true,
            cleanupSpawnedObjects = true
        };

        [SerializeField]
        private RpcRules _defaultRpcRules = new RpcRules
        {
            ignoreRequireServerAttribute = false,
            ignoreRequireOwnerAttribute = false,
            targetRpcsCanTargetServer = false
        };

        [PurrReadOnly, UsedImplicitly]
        [SerializeField]
        private VisibilityRules _defaultVisibilityRules = new VisibilityRules
        {
            visibilityMode = VisibilityMode.SpawnDespawn
        };

        [SerializeField]
        private OwnershipRules _defaultOwnershipRules = new OwnershipRules
        {
            assignAuth = ConnectionAuth.Server,
            transferAuth = ActionAuth.Owner | ActionAuth.Server,
            overrideWhenPropagating = true
        };

        [SerializeField]
        private NetworkSceneRules _defaultSceneRules = new NetworkSceneRules
        {
            removePlayerFromSceneOnDisconnect = false,
            sceneCleanupModeOnDisconnect = SceneCleanupMode.OnlineOnly,
            alwaysIncludeDontDestroyOnLoadScene = false
        };

        [SerializeField]
        private NetworkIdentityRules _defaultIdentityRules = new NetworkIdentityRules
        {
            receiveRpcsWhenDisabled = true
        };

        [SerializeField]
        private NetworkTransformRules _defaultTransformRules = new NetworkTransformRules
        {
            changeParentAuth = ActionAuth.Server | ActionAuth.Owner
        };

        [SerializeField]
        private MiscRules _defaultMiscRules = new MiscRules
        {
            syncedTickUpdateInterval = 1
        };

        public bool HasDespawnAuthority(NetworkIdentity identity, PlayerID player, bool asServer)
        {
            return HasAuthority(_defaultSpawnRules.despawnAuth, identity, player, asServer);
        }

        [UsedImplicitly]
        public bool HasSpawnAuthority(NetworkManager manager, bool asServer)
        {
            return HasAuthority(_defaultSpawnRules.spawnAuth, asServer);
        }

        [UsedImplicitly]
        public bool HasPropagateOwnershipAuthority(NetworkIdentity identity)
        {
            return true;
        }

        public bool HasChangeParentAuthority(NetworkIdentity identity, PlayerID? player, bool asServer)
        {
            return HasAuthority(_defaultTransformRules.changeParentAuth, identity, player, asServer);
        }

        static bool HasAuthority(ConnectionAuth connAuth, bool asServer)
        {
            return connAuth == ConnectionAuth.Everyone || asServer;
        }

        static bool HasAuthority(ActionAuth action, NetworkIdentity identity, PlayerID? player, bool asServer)
        {
            if (action.HasFlag(ActionAuth.Server) && asServer)
                return true;

            if (action.HasFlag(ActionAuth.Owner) && player.HasValue && identity.owner == player)
                return true;

            return identity.owner != player && action.HasFlag(ActionAuth.Observer);
        }

        public bool HasTransferOwnershipAuthority(NetworkIdentity networkIdentity, PlayerID? localPlayer, bool asServer)
        {
            return HasAuthority(_defaultOwnershipRules.transferAuth, networkIdentity, localPlayer, asServer);
        }

        public bool HasGiveOwnershipAuthority(NetworkIdentity networkIdentity, bool asServer)
        {
            return HasAuthority(_defaultOwnershipRules.assignAuth, asServer);
        }

        public bool HasRemoveOwnershipAuthority(NetworkIdentity networkIdentity, PlayerID? localPlayer, bool asServer)
        {
            return HasAuthority(_defaultOwnershipRules.removeAuth, networkIdentity, localPlayer, asServer);
        }

        public bool ShouldPropagateToChildren()
        {
            return _defaultSpawnRules.propagateOwnershipByDefault;
        }

        public bool ShouldOverrideExistingOwnership(NetworkIdentity networkIdentity, bool asServer)
        {
            return _defaultOwnershipRules.overrideWhenPropagating;
        }

        public bool ShouldRemovePlayerFromSceneOnLeave()
        {
            return _defaultSceneRules.removePlayerFromSceneOnDisconnect;
        }

        public bool ShouldDespawnOnOwnerDisconnect()
        {
            return _defaultSpawnRules.despawnIfOwnerDisconnects;
        }

        public bool ShouldClientGiveOwnershipOnSpawn()
        {
            return _defaultSpawnRules.defaultOwner == DefaultOwner.SpawnerIfClientOnly;
        }

        public bool ShouldPlayRPCsWhenDisabled()
        {
            return _defaultIdentityRules.receiveRpcsWhenDisabled;
        }

        public bool ShouldIgnoreRequireServer()
        {
            return _defaultRpcRules.ignoreRequireServerAttribute;
        }

        public bool ShouldIgnoreRequireOwner()
        {
            return _defaultRpcRules.ignoreRequireOwnerAttribute;
        }

        public float GetSyncedTickUpdateInterval()
        {
            return _defaultMiscRules.syncedTickUpdateInterval;
        }

        public bool ShouldCleanupSpawnedObjectsOnDisconnect()
        {
            return _defaultSpawnRules.cleanupSpawnedObjects;
        }

        public bool ShouldCleanupScenesOnDisconnect()
        {
            return _defaultSceneRules.sceneCleanupModeOnDisconnect != SceneCleanupMode.Off;
        }

        public SceneCleanupMode SceneCleanupModeOnDisconnect()
        {
            return _defaultSceneRules.sceneCleanupModeOnDisconnect;
        }

        public bool ShouldAlwaysIncludeDontDestroyOnLoadScene()
        {
            return _defaultSceneRules.alwaysIncludeDontDestroyOnLoadScene;
        }

        public bool CanTargetServerWithTargetRpc()
        {
            return _defaultRpcRules.targetRpcsCanTargetServer;
        }

        public bool IsHostMigrationEnabled()
        {
            return _hostMigrationRules.enabled;
        }

        public bool ShouldForceVisibilityToAlwaysVisible()
        {
            return _hostMigrationRules.identitiesAlwaysVisible;
        }

        public bool ShouldForceSceneToAlwaysPublic()
        {
            return _hostMigrationRules.scenesAlwaysPublic;
        }

        public bool ShouldMigrateAsHost()
        {
            return _hostMigrationRules.migrateAsHost;
        }
    }
}
