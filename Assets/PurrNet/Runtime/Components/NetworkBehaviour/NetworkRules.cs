using System;
using JetBrains.Annotations;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.Serialization;

namespace PurrNet
{
    public enum SceneCleanupMode
    {
        /// <summary>
        /// Do not cleanup any scenes on disconnect.
        /// </summary>
        Off,

        /// <summary>
        /// Cleanup scenes that were loaded through the network, and load the starting scene additively.
        /// </summary>
        OnlineScenesOnly,

        /// <summary>
        /// Cleanup all scenes and load the start scene in single mode.
        /// </summary>
        AllScenes
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

        [Tooltip("On disconnect, unload scenes based on the cleanup mode and load the starting scene")]
        public SceneCleanupMode sceneCleanupModeOnDisconnect;

        public bool alwaysIncludeDontDestroyOnLoadScene;

        [Obsolete("Use sceneCleanupModeOnDisconnect instead.")]
        public bool cleanupScenesOnDisconnect
        {
            readonly get => sceneCleanupModeOnDisconnect == SceneCleanupMode.OnlineScenesOnly;
            set => sceneCleanupModeOnDisconnect = value ? SceneCleanupMode.OnlineScenesOnly : SceneCleanupMode.Off;
        }

        /// <summary>
        /// No-op callback invoked before Unity serializes this object.
        /// </summary>
        /// <remarks>
        /// Implements ISerializationCallbackReceiver.OnBeforeSerialize; intentionally does nothing.
        /// Present to satisfy the serialization callback contract and preserve compatibility.
        /// </remarks>
        public readonly void OnBeforeSerialize()
        {
            return;
        }

        /// <summary>
        /// Runs after Unity deserializes the object and migrates legacy scene-cleanup state.
        /// </summary>
        /// <remarks>
        /// If the obsolete backing field for the old boolean cleanup flag (_cleanupScenesOnDisconnect)
        /// is true but the new SceneCleanupMode (sceneCleanupModeOnDisconnect) is not set to
        /// OnlineScenesOnly, this method sets the mode to OnlineScenesOnly and clears the obsolete flag.
        /// This preserves compatibility with data serialized using the previous boolean field.
        /// </remarks>
        public void OnAfterDeserialize()
        {
            if (_cleanupScenesOnDisconnect && sceneCleanupModeOnDisconnect != SceneCleanupMode.OnlineScenesOnly)
            {
                sceneCleanupModeOnDisconnect = SceneCleanupMode.OnlineScenesOnly;
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
            ignoreRequireOwnerAttribute = false
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
            sceneCleanupModeOnDisconnect = SceneCleanupMode.OnlineScenesOnly,
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

        /// <summary>
        /// Returns whether scenes should be cleaned up when a player disconnects.
        /// </summary>
        /// <returns>True if the configured scene cleanup mode on disconnect is not <see cref="SceneCleanupMode.Off"/>.</returns>
        public bool ShouldCleanupScenesOnDisconnect()
        {
            return _defaultSceneRules.sceneCleanupModeOnDisconnect != SceneCleanupMode.Off;
        }

        /// <summary>
        /// Returns the configured scene cleanup mode to apply when a player disconnects.
        /// </summary>
        /// <returns>The <see cref="SceneCleanupMode"/> currently set for disconnect-time scene cleanup.</returns>
        public SceneCleanupMode SceneCleanupModeOnDisconnect()
        {
            return _defaultSceneRules.sceneCleanupModeOnDisconnect;
        }

        /// <summary>
        /// Returns whether the DontDestroyOnLoad scene should always be included when cleaning up scenes on disconnect.
        /// </summary>
        /// <returns>True if the DontDestroyOnLoad scene is always included; otherwise false.</returns>
        public bool ShouldAlwaysIncludeDontDestroyOnLoadScene()
        {
            return _defaultSceneRules.alwaysIncludeDontDestroyOnLoadScene;
        }
    }
}
