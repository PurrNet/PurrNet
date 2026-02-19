using UnityEditor;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerAuth
    {
        private const string PrefKey = "PurrNet_PackageManager_ApiKey";

        public static string GetApiKey()
        {
            return EditorPrefs.GetString(PrefKey, "");
        }

        public static void SetApiKey(string key)
        {
            EditorPrefs.SetString(PrefKey, key);
        }

        public static void ClearApiKey()
        {
            EditorPrefs.DeleteKey(PrefKey);
        }

        public static bool HasApiKey()
        {
            return !string.IsNullOrEmpty(GetApiKey());
        }
    }
}
