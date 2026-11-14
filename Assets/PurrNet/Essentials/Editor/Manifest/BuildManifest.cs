using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PurrNet.Essentials
{
    public sealed class BuildManifest
    {
        public const int Version = 1;

        private readonly JArray _types = new JArray();
        private readonly JArray _prefabs = new JArray();
        private readonly JArray _scenes = new JArray();
        private readonly JArray _meshes = new JArray();
        private readonly Dictionary<Mesh, int> _meshIds = new Dictionary<Mesh, int>();
        private readonly Dictionary<string, byte[]> _meshData = new Dictionary<string, byte[]>();

        public string netcode { get; set; }

        public string netcodeVersion { get; set; }

        public int tickRate { get; set; }

        public JObject netcodeData { get; } = new JObject();

        public IReadOnlyDictionary<string, byte[]> meshData => _meshData;

        public int prefabCount => _prefabs.Count;

        public int sceneCount => _scenes.Count;

        public int typeCount => _types.Count;

        public void AddType(JObject type)
        {
            _types.Add(type);
        }

        public JObject AddPrefab(int id, GameObject prefab)
        {
            var entry = new JObject
            {
                ["id"] = id,
                ["name"] = prefab.name,
                ["colliders"] = DescribeColliders(prefab.transform, prefab.transform, null)
            };
            _prefabs.Add(entry);
            return entry;
        }

        public JObject AddScene(int index, string name, IReadOnlyList<GameObject> roots, Func<GameObject, bool> isNetworkObject)
        {
            var colliders = new JArray();
            for (int i = 0; i < roots.Count; i++)
            {
                foreach (var collider in DescribeColliders(roots[i].transform, null, isNetworkObject))
                    colliders.Add(collider);
            }

            var entry = new JObject
            {
                ["index"] = index,
                ["name"] = name,
                ["colliders"] = colliders
            };
            _scenes.Add(entry);
            return entry;
        }

        public string ToJson()
        {
            var root = new JObject
            {
                ["format"] = "purrmanifest",
                ["version"] = Version,
                ["game"] = new JObject
                {
                    ["name"] = Application.productName,
                    ["version"] = Application.version,
                    ["unity"] = Application.unityVersion
                },
                ["netcode"] = new JObject
                {
                    ["name"] = netcode,
                    ["version"] = netcodeVersion
                },
                ["tickRate"] = tickRate,
                ["types"] = _types,
                ["prefabs"] = _prefabs,
                ["scenes"] = _scenes,
                ["meshes"] = _meshes,
                ["netcodeData"] = netcodeData
            };
            return root.ToString(Formatting.None);
        }

        public static string Hash(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var text = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                text.Append(hash[i].ToString("x2"));
            return text.ToString();
        }

        public string ContentHash()
        {
            return Hash(Encoding.UTF8.GetBytes(ToJson()));
        }

        private JArray DescribeColliders(Transform from, Transform relativeTo, Func<GameObject, bool> skip)
        {
            var result = new JArray();
#if UNITY_PHYSICS_3D
            var colliders = from.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (!collider.enabled)
                    continue;
                if (skip != null && skip(collider.gameObject))
                    continue;

                var described = Describe(collider, relativeTo);
                if (described != null)
                    result.Add(described);
            }
#endif
            return result;
        }

#if UNITY_PHYSICS_3D
        private JObject Describe(Collider collider, Transform relativeTo)
        {
            var transform = collider.transform;
            Vector3 position;
            Quaternion rotation;
            Vector3 scale = transform.lossyScale;

            if (relativeTo == null)
            {
                position = transform.position;
                rotation = transform.rotation;
            }
            else
            {
                position = relativeTo.InverseTransformPoint(transform.position);
                rotation = Quaternion.Inverse(relativeTo.rotation) * transform.rotation;
            }

            var entry = new JObject
            {
                ["pose"] = new JObject
                {
                    ["p"] = Vector(position),
                    ["r"] = new JArray(Round(rotation.x), Round(rotation.y), Round(rotation.z), Round(rotation.w)),
                    ["s"] = Vector(scale)
                },
                ["trigger"] = collider.isTrigger,
                ["layer"] = collider.gameObject.layer
            };

            switch (collider)
            {
                case BoxCollider box:
                    entry["shape"] = "box";
                    entry["center"] = Vector(box.center);
                    entry["size"] = Vector(box.size);
                    break;
                case SphereCollider sphere:
                    entry["shape"] = "sphere";
                    entry["center"] = Vector(sphere.center);
                    entry["radius"] = Round(sphere.radius);
                    break;
                case CapsuleCollider capsule:
                    entry["shape"] = "capsule";
                    entry["center"] = Vector(capsule.center);
                    entry["radius"] = Round(capsule.radius);
                    entry["height"] = Round(capsule.height);
                    entry["direction"] = capsule.direction;
                    break;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    entry["shape"] = "mesh";
                    entry["mesh"] = MeshId(mesh.sharedMesh);
                    entry["convex"] = mesh.convex;
                    break;
                default:
                    var bounds = collider.bounds;
                    entry["shape"] = "bounds";
                    entry["center"] = Vector(transform.InverseTransformPoint(bounds.center));
                    entry["size"] = Vector(bounds.size);
                    break;
            }

            return entry;
        }

        private int MeshId(Mesh mesh)
        {
            if (_meshIds.TryGetValue(mesh, out int id))
                return id;

            id = _meshIds.Count + 1;
            _meshIds[mesh] = id;

            var entry = new JObject { ["id"] = id, ["name"] = mesh.name };
            var data = Serialize(mesh);
            if (data != null)
            {
                string hash = Hash(data);
                _meshData[hash] = data;
                entry["hash"] = hash;
            }

            _meshes.Add(entry);
            return id;
        }

        private static byte[] Serialize(Mesh mesh)
        {
            try
            {
                var vertices = mesh.vertices;
                var indices = mesh.triangles;

                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                var name = Encoding.UTF8.GetBytes(mesh.name ?? "");
                WriteVarUInt(writer, (uint)name.Length);
                writer.Write(name);
                WriteVarUInt(writer, (uint)vertices.Length);
                for (int i = 0; i < vertices.Length; i++)
                {
                    writer.Write(vertices[i].x);
                    writer.Write(vertices[i].y);
                    writer.Write(vertices[i].z);
                }

                WriteVarUInt(writer, (uint)indices.Length);
                for (int i = 0; i < indices.Length; i++)
                    writer.Write((uint)indices[i]);
                writer.Flush();
                return stream.ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WriteVarUInt(BinaryWriter writer, uint value)
        {
            while (value >= 0x80)
            {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }

            writer.Write((byte)value);
        }
#endif

        private static double Round(float value)
        {
            return Math.Round(value, 5);
        }

        private static JArray Vector(Vector3 value)
        {
            return new JArray(Round(value.x), Round(value.y), Round(value.z));
        }
    }
}
