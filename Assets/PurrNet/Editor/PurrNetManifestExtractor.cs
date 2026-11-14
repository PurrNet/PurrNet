using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using PurrNet.Essentials;
using PurrNet.Utils;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public sealed class PurrNetManifestExtractor : IBuildManifestExtractor
    {
        private const string PackageJsonGuid = "0ec978dbed50a6f4b9a57580867f1fae";

        public string netcode => "purrnet";

        public string netcodeVersion
        {
            get
            {
                if (!string.IsNullOrEmpty(NetworkManager.version))
                    return NetworkManager.version;

                string path = AssetDatabase.GUIDToAssetPath(PackageJsonGuid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                try
                {
                    return (string)JObject.Parse(File.ReadAllText(path))["version"];
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public bool IsNetworkObject(GameObject gameObject)
        {
            return gameObject.GetComponentInParent<NetworkIdentity>(true) != null;
        }

        public void Extract(BuildManifest manifest)
        {
            if (Manifest.Count == 0)
            {
                typeof(NetworkManager).GetMethod("CalculateHashes", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, null);
            }

            manifest.netcodeData["rpcs"] = DescribeRpcs();
            AddPrefabs(manifest);
        }

        private static JArray DescribeRpcs()
        {
            var entries = new List<RPCManifestEntry>(Manifest.Entries);
            entries.Sort((a, b) =>
            {
                int byType = string.CompareOrdinal(a.declaringType?.FullName, b.declaringType?.FullName);
                return byType != 0 ? byType : a.rpcId.CompareTo(b.rpcId);
            });

            var rpcs = new JArray();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.declaringType == null)
                    continue;

                rpcs.Add(new JObject
                {
                    ["type"] = entry.declaringType.FullName,
                    ["typeHash"] = Hasher.Hash(entry.declaringType),
                    ["id"] = entry.rpcId,
                    ["name"] = entry.signature.rpcName,
                    ["kind"] = entry.signature.type.ToString(),
                    ["channel"] = entry.signature.channel.ToString(),
                    ["static"] = entry.signature.isStatic,
                    ["delta"] = entry.signature.deltaPacked
                });
            }

            return rpcs;
        }

        private static void AddPrefabs(BuildManifest manifest)
        {
            var guids = AssetDatabase.FindAssets("t:PrefabProviderScriptable");
            Array.Sort(guids, string.CompareOrdinal);

            for (int g = 0; g < guids.Length; g++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                var provider = AssetDatabase.LoadAssetAtPath<PrefabProviderScriptable>(path);
                if (!provider)
                    continue;

                try
                {
                    provider.Refresh();
                }
                catch (Exception)
                {
                    continue;
                }

                var prefabs = new List<PrefabData>(provider.allPrefabs);
                prefabs.Sort((a, b) => ((int)a.prefabId.value).CompareTo((int)b.prefabId.value));

                for (int i = 0; i < prefabs.Count; i++)
                {
                    var data = prefabs[i];
                    if (!data.prefab)
                        continue;

                    var entry = manifest.AddPrefab((int)data.prefabId.value, data.prefab);
                    entry["provider"] = guids[g];
                    entry["components"] = DescribeIdentities(data.prefab);
                }
            }
        }

        private static JArray DescribeIdentities(GameObject prefab)
        {
            var components = new JArray();
            var identities = prefab.GetComponentsInChildren<NetworkIdentity>(true);

            for (int i = 0; i < identities.Length; i++)
            {
                var type = identities[i].GetType();
                components.Add(new JObject
                {
                    ["index"] = i,
                    ["path"] = PathOf(identities[i].transform, prefab.transform),
                    ["type"] = type.FullName,
                    ["typeHash"] = Hasher.Hash(type)
                });
            }

            return components;
        }

        private static string PathOf(Transform transform, Transform root)
        {
            if (transform == root)
                return "";

            string path = transform.name;
            var current = transform.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
