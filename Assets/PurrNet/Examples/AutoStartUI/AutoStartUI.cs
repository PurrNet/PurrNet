using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PurrNet.Examples
{
    public class AutoStartUI : MonoBehaviour
    {
        [SerializeField] private bool _disappearAfterClientStart = false;
        [SerializeField] private Image _serverButtonImage, _clientButtonImage;

        [SerializeField, HideInInspector] private bool _alreadyInitialized;

        private void OnEnable()
        {
            if (!NetworkManager.main)
                return;

            NetworkManager.main.onServerConnectionState += OnServerConnectionStateChanged;
            NetworkManager.main.onClientConnectionState += OnClientConnectionStateChanged;
        }

        private void OnDisable()
        {
            if (!NetworkManager.main)
                return;

            NetworkManager.main.onServerConnectionState -= OnServerConnectionStateChanged;
            NetworkManager.main.onClientConnectionState -= OnClientConnectionStateChanged;
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!EditorApplication.isPlayingOrWillChangePlaymode && gameObject.scene.IsValid())
            {
                if (!_alreadyInitialized &&
                    PrefabUtility.GetPrefabInstanceStatus(gameObject) == PrefabInstanceStatus.Connected &&
                    PrefabUtility.IsPartOfPrefabInstance(gameObject))
                {
                    _alreadyInitialized = true;

                    var nm = FindFirstObjectByType<NetworkManager>();
                    if (!nm)
                    {
                        PurrLogger.LogError($"Failed to auto remove start flags from NetworkManager: No NetworkManager found in scene.", this);
                        return;
                    }

                    nm.startClientFlags = StartFlags.None;
                    nm.startServerFlags = StartFlags.None;
                    PurrLogger.Log($"Cleared start flags from NetworkManager", this);
                }
            }
#endif
        }

        private void RunResetLogic()
        {
            var nm = FindFirstObjectByType<NetworkManager>();
            if (!nm)
            {
                PurrLogger.LogError($"Failed to auto remove start flags from NetworkManager: No NetworkManager found in scene.", this);
                return;
            }

            nm.startClientFlags = StartFlags.None;
            nm.startServerFlags = StartFlags.None;
            PurrLogger.Log($"Cleared start flags from NetworkManager", this);
        }

        private void OnServerConnectionStateChanged(ConnectionState state)
        {
            if (!this || !gameObject)
                return;

            var color = state == ConnectionState.Connected ? Color.green : Color.white;
            color = state == ConnectionState.Connecting || state == ConnectionState.Disconnecting ? Color.yellow : color;
            if(_serverButtonImage)
                _serverButtonImage.color = color;
        }

        private void OnClientConnectionStateChanged(ConnectionState state)
        {
            if (!this || !gameObject)
                return;

            var color = state == ConnectionState.Connected ? Color.green : Color.white;
            color = state == ConnectionState.Connecting || state == ConnectionState.Disconnecting ? Color.yellow : color;
            if(_clientButtonImage)
                _clientButtonImage.color = color;

            if(state == ConnectionState.Connected && _disappearAfterClientStart)
                gameObject.SetActive(false);
            else
                gameObject.SetActive(true);
        }

        public void ClickServer()
        {
            if (!CanStart())
                return;

            if (NetworkManager.main.serverState != ConnectionState.Disconnected)
            {
                NetworkManager.main.StopServer();
                return;
            }

            NetworkManager.main.StartServer();
        }

        public void ClickClient()
        {
            if (!CanStart())
                return;

            if (NetworkManager.main.clientState != ConnectionState.Disconnected)
            {
                NetworkManager.main.StopClient();
                return;
            }

            NetworkManager.main.StartClient();
        }

        private bool CanStart()
        {
            if (!NetworkManager.main)
            {
                PurrLogger.LogError($"Failed to start server: NetworkManager.main is not set. Please ensure the NetworkManager is present in the scene.", this);
                return false;
            }

            return true;
        }
    }
}
