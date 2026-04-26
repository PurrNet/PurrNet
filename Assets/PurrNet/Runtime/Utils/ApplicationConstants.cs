using System.Collections.Generic;
#if UNITY_EDITOR
using System.IO;
using Newtonsoft.Json.Linq;
#else
using UnityEngine;
using UnityEngine.Scripting;
#endif

namespace PurrNet.Utils
{
    public static class ApplicationConstants
    {
        private static readonly Dictionary<string, string> _constants = new();

        public static IReadOnlyDictionary<string, string> constants => _constants;

#if UNITY_EDITOR
        const string SAVE_PATH = "./ProjectSettings/ApplicationConstants.json";

        static ApplicationConstants()
        {
            Load();
        }

        static void Load()
        {
            _constants.Clear();

            if (!File.Exists(SAVE_PATH))
                return;

            string jsonString = File.ReadAllText(SAVE_PATH);
            var json = JObject.Parse(jsonString);

            foreach (var (key, val) in json)
            {
                if (val == null)
                    continue;
                _constants[key] = val.ToString();
            }
        }

        private static void SaveChanges()
        {
            var json = new JObject();
            foreach (var pair in _constants)
                json[pair.Key] = pair.Value;
            File.WriteAllText(SAVE_PATH, json.ToString());
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration), Preserve]
        static void Init() { }

        private static void SaveChanges() {}
#endif

        public static bool TryGet(string key, out string value)
        {
            return _constants.TryGetValue(key, out value);
        }

        public static void Set(string key, string value)
        {
            if (_constants.TryGetValue(key, out var v) && v == value)
                return;

            _constants[key] = value;
#if UNITY_EDITOR
            SaveChanges();
#endif
        }

        public static bool Delete(string key)
        {
            bool success = _constants.Remove(key);
#if UNITY_EDITOR
            if (success)
                SaveChanges();
#endif
            return success;
        }
    }
}
