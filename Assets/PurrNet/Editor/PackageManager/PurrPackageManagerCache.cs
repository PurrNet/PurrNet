using UnityEditor;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerCache
    {
        private const float TTL = 300f; // 5 minutes

        private static PackagesResponse _packages;
        private static EntitlementsResponse _entitlements;
        private static double _packagesTime;
        private static double _entitlementsTime;

        public static bool TryGetPackages(out PackagesResponse packages)
        {
            if (_packages != null && EditorApplication.timeSinceStartup - _packagesTime < TTL)
            {
                packages = _packages;
                return true;
            }

            packages = null;
            return false;
        }

        public static void SetPackages(PackagesResponse packages)
        {
            _packages = packages;
            _packagesTime = EditorApplication.timeSinceStartup;
        }

        public static bool TryGetEntitlements(out EntitlementsResponse entitlements)
        {
            if (_entitlements != null && EditorApplication.timeSinceStartup - _entitlementsTime < TTL)
            {
                entitlements = _entitlements;
                return true;
            }

            entitlements = null;
            return false;
        }

        public static void SetEntitlements(EntitlementsResponse entitlements)
        {
            _entitlements = entitlements;
            _entitlementsTime = EditorApplication.timeSinceStartup;
        }

        public static void Invalidate()
        {
            _packages = null;
            _entitlements = null;
        }
    }
}
