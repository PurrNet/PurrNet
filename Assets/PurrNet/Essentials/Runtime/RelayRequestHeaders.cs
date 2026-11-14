using System.Collections.Generic;

namespace PurrNet.Essentials
{
    /// <summary>Extra HTTP headers every relay transport adds to its room requests to the relay balancer.</summary>
    public static class RelayRequestHeaders
    {
        private static readonly Dictionary<string, string> _headers = new Dictionary<string, string>();

        public static IReadOnlyDictionary<string, string> all => _headers;

        public static void Set(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                _headers.Remove(name);
            else
                _headers[name] = value.Trim();
        }

        public static void Remove(string name)
        {
            _headers.Remove(name);
        }
    }
}
