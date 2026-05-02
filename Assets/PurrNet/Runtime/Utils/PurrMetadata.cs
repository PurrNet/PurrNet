#if UNITY_EDITOR
using Newtonsoft.Json.Linq;
using UnityEditor;
#else
using UnityEngine.Scripting;
#endif
using UnityEngine;

namespace PurrNet.Utils
{
    public static class PurrMetadata
    {
        public const string PACKAGE_JSON_GUID = "0ec978dbed50a6f4b9a57580867f1fae";

        private static string _version = "v?";

        public static string version => _version;

#if UNITY_EDITOR

        static PurrMetadata()
        {
            _version = ReadFromPackageJson() ?? "v?";
        }

        static string ReadFromPackageJson()
        {
            var path = AssetDatabase.GUIDToAssetPath(PACKAGE_JSON_GUID);

            if (string.IsNullOrEmpty(path))
                return null;

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);

            if (asset == null)
                return null;

            var json = JObject.Parse(asset.text);
            return 'v' + (json["version"]?.ToString() ?? "?");
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration), Preserve]
        static void Init() { }

        static void SetBakedVersion(string version) => _version = version;
#endif
    }
}
