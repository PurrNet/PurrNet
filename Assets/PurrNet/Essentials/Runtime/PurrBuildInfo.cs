using UnityEngine;

namespace PurrNet.Essentials
{
    public static class PurrBuildInfo
    {
        public const string Editor = "editor";
        public const string Development = "development";

        private static string _baked;

        public static string buildId
        {
            get
            {
#if UNITY_EDITOR
                return Editor;
#else
                if (Debug.isDebugBuild)
                    return Development;

                return _baked;
#endif
            }
        }

        public static bool isRelease => buildId != null && buildId != Editor && buildId != Development;

        internal static void SetBaked(string id)
        {
            _baked = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }
    }

    [AddComponentMenu("")]
    public sealed class PurrBuildMarker : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string _buildId;

        public string bakedBuildId
        {
            get => _buildId;
            set => _buildId = value;
        }

        private void Awake()
        {
            PurrBuildInfo.SetBaked(_buildId);
            Destroy(gameObject);
        }
    }
}
