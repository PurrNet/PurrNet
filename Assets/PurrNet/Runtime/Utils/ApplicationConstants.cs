using System.Collections.Generic;

namespace PurrNet.Utils
{
    public static class ApplicationConstants
    {
        public static IReadOnlyDictionary<string, string> constants => global::PurrNet.Essentials.ApplicationConstants.constants;

        public static bool TryGet(string key, out string value)
        {
            return global::PurrNet.Essentials.ApplicationConstants.TryGet(key, out value);
        }

        public static void Set(string key, string value)
        {
            global::PurrNet.Essentials.ApplicationConstants.Set(key, value);
        }

        public static bool Delete(string key)
        {
            return global::PurrNet.Essentials.ApplicationConstants.Delete(key);
        }
    }
}
