using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace PurrNet.Modules
{
    public enum CookieScope
    {
        /// <summary>
        /// Nothing is stored once the connection stops
        /// </summary>
        LiveWithConnection,

        /// <summary>
        /// Nothing is stored once the game/process stops
        /// </summary>
        LiveWithProcess,

        /// <summary>
        /// Cookies are stored in the player prefs of your system
        /// </summary>
        StorePersistently
    }

    [System.Serializable]
    internal struct CookiePair
    {
        public string key;
        public string value;
    }

    [System.Serializable]
    internal struct CookieData
    {
        public List<CookiePair> cookies;
    }

    public class CookiesModule : INetworkModule, IPromoteToServerModule
    {
        private const string SAVE_KEY = "rabsi_cookies";

        readonly List<CookiePair> _cookies = new List<CookiePair>();
        readonly CookieScope _scope;
        bool _asServer;

        public CookiesModule(CookieScope scope, bool asServer)
        {
            _scope = scope;
            _asServer = asServer;
        }

        public void PromoteToServerModule()
        {
            _asServer = true;
        }

        public void PostPromoteToServerModule() { }

        public void Enable(bool asServer)
        {
            Load(asServer);
        }

        public void Disable(bool asServer)
        {
            Save(asServer);
        }

        public string GetOrSet(string key, string defaultValue)
        {
            var value = Get(key);
            if (value == null)
            {
                Set(key, defaultValue, _asServer);
                return defaultValue;
            }

            return value;
        }

        /// <summary>
        /// Unset a cookie by key.
        /// </summary>
        public bool Unset(string key)
        {
            for (int i = 0; i < _cookies.Count; i++)
            {
                if (_cookies[i].key == key)
                {
                    _cookies.RemoveAt(i);
                    Save(_asServer);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Set a cookie by key.
        /// </summary>
        public void Set(string key, string value, bool asServer)
        {
            for (int i = 0; i < _cookies.Count; i++)
            {
                if (_cookies[i].key == key)
                {
                    _cookies[i] = new CookiePair { key = key, value = value };
                    Save(asServer);
                    return;
                }
            }

            _cookies.Add(new CookiePair { key = key, value = value });
            Save(asServer);
        }

        /// <summary>
        /// Get a cookie by key.
        /// </summary>
        [CanBeNull]
        public string Get(string key)
        {
            for (int i = 0; i < _cookies.Count; i++)
            {
                if (_cookies[i].key == key)
                    return _cookies[i].value;
            }

            return null;
        }

        public void Clear(bool asServer)
        {
            _cookies.Clear();
            Save(asServer);
        }

        void Load(bool asServer)
        {
            try
            {
                var data = LoadData(asServer);

                if (!string.IsNullOrEmpty(data))
                {
                    _cookies.Clear();
                    var loaded = JsonUtility.FromJson<CookieData>(data);
                    _cookies.AddRange(loaded.cookies);
                }
            }
            catch
            {
                Debug.LogWarning("Failed to load cookies, clearing.");
                _cookies.Clear();
                Save(asServer);
            }
        }

        string SaveData()
        {
            return JsonUtility.ToJson(new CookieData
            {
                cookies = _cookies
            });
        }

        void Save(bool asServer)
        {
            var saveKey = $"{SAVE_KEY}_{asServer}";

            switch (_scope)
            {
                case CookieScope.LiveWithConnection: break;
                case CookieScope.LiveWithProcess:
                {
                    var data = SaveData();
                    if (data != null)
                        ProcessPrefs.Set(saveKey, data);
                    else ProcessPrefs.Unset(saveKey);
                    break;
                }
                case CookieScope.StorePersistently:
                {
                    var data = SaveData();
                    if (data != null)
                        PlayerPrefs.SetString(saveKey, data);
                    else PlayerPrefs.DeleteKey(saveKey);
                    break;
                }
            }
        }

        [CanBeNull]
        string LoadData(bool asServer)
        {
            var saveKey = $"{SAVE_KEY}_{asServer}";
            return _scope switch
            {
                CookieScope.LiveWithProcess => ProcessPrefs.Get(saveKey, null),
                CookieScope.StorePersistently => PlayerPrefs.GetString(saveKey, null),
                _ => null
            };
        }
    }
}
