using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PurrNet.Editor;
using PurrNet.Essentials;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Manifest.Tests
{
    public class PurrNetManifestTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i])
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        static BuildManifest Extract()
        {
            var extractor = new PurrNetManifestExtractor();
            var manifest = new BuildManifest { netcode = extractor.netcode, netcodeVersion = extractor.netcodeVersion };
            extractor.Extract(manifest);
            return manifest;
        }

        GameObject Child(GameObject parent, string name, Vector3 position)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = position;
            return child;
        }

        [Test]
        public void ExtractorIsDiscoverable()
        {
            var types = TypeCache.GetTypesDerivedFrom<IBuildManifestExtractor>();
            Assert.Contains(typeof(PurrNetManifestExtractor), types.ToList());
        }

        [Test]
        public void ExtractorDescribesRpcsAndPrefabs()
        {
            var json = JObject.Parse(Extract().ToJson());

            Assert.AreEqual("purrmanifest", (string)json["format"]);
            Assert.AreEqual("purrnet", (string)json["netcode"]["name"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)json["netcode"]["version"]), "no PurrNet version");

            var rpcs = (JArray)json["netcodeData"]["rpcs"];
            Assert.Greater(rpcs.Count, 0, "no RPCs were described");
            foreach (var rpc in rpcs)
            {
                Assert.IsFalse(string.IsNullOrEmpty((string)rpc["type"]));
                Assert.AreNotEqual(0u, (uint)rpc["typeHash"]);
                Assert.IsNotNull(rpc["id"]);
            }

            var prefabs = (JArray)json["prefabs"];
            Assert.Greater(prefabs.Count, 0, "no prefabs were described");
            foreach (var prefab in prefabs)
            {
                Assert.IsNotNull(prefab["id"]);
                Assert.IsFalse(string.IsNullOrEmpty((string)prefab["name"]));
                Assert.IsInstanceOf<JArray>(prefab["colliders"]);
                Assert.IsInstanceOf<JArray>(prefab["components"]);
            }

            Assert.IsTrue(prefabs.Any(prefab => ((JArray)prefab["components"]).Count > 0),
                "no prefab lists a network identity");

            int colliders = prefabs.Sum(prefab => ((JArray)prefab["colliders"]).Count);
            Debug.Log($"[Manifest] {rpcs.Count} RPCs, {prefabs.Count} prefabs, {colliders} colliders, " +
                      $"{((JArray)json["meshes"]).Count} meshes, {json.ToString(Newtonsoft.Json.Formatting.None).Length} bytes");
        }

        [Test]
        public void SameProjectGivesSameManifest()
        {
            Assert.AreEqual(Extract().ContentHash(), Extract().ContentHash());
        }

        [Test]
        public void ColliderShapesAreDescribedRelativeToThePrefab()
        {
            var root = new GameObject("ManifestTestPrefab");
            _created.Add(root);
            root.transform.position = new Vector3(10, 20, 30);

            var box = Child(root, "Box", new Vector3(1, 0, 0)).AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.5f, 0);
            box.size = new Vector3(1, 2, 3);
            box.isTrigger = true;

            var sphere = Child(root, "Sphere", new Vector3(0, 2, 0)).AddComponent<SphereCollider>();
            sphere.radius = 0.75f;

            var capsule = Child(root, "Capsule", new Vector3(0, 0, 3)).AddComponent<CapsuleCollider>();
            capsule.radius = 0.5f;
            capsule.height = 2f;
            capsule.direction = 1;

            var disabled = Child(root, "Disabled", Vector3.zero).AddComponent<BoxCollider>();
            disabled.enabled = false;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(cube);
            var meshChild = Child(root, "Mesh", new Vector3(-4, 0, 0));
            var meshCollider = meshChild.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            meshCollider.convex = true;

            var manifest = new BuildManifest();
            var entry = manifest.AddPrefab(7, root);
            var colliders = (JArray)entry["colliders"];

            Assert.AreEqual(7, (int)entry["id"]);
            Assert.AreEqual(4, colliders.Count, "the disabled collider must be left out");

            var described = colliders.ToDictionary(collider => (string)collider["shape"]);

            Assert.AreEqual(new[] { 1.0, 0.0, 0.0 }, described["box"]["pose"]["p"].Select(v => (double)v).ToArray());
            Assert.AreEqual(new[] { 1.0, 2.0, 3.0 }, described["box"]["size"].Select(v => (double)v).ToArray());
            Assert.AreEqual(new[] { 0.0, 0.5, 0.0 }, described["box"]["center"].Select(v => (double)v).ToArray());
            Assert.IsTrue((bool)described["box"]["trigger"]);

            Assert.AreEqual(0.75, (double)described["sphere"]["radius"]);
            Assert.AreEqual(new[] { 0.0, 2.0, 0.0 }, described["sphere"]["pose"]["p"].Select(v => (double)v).ToArray());

            Assert.AreEqual(2.0, (double)described["capsule"]["height"]);
            Assert.AreEqual(1, (int)described["capsule"]["direction"]);

            Assert.IsTrue((bool)described["mesh"]["convex"]);
            int meshId = (int)described["mesh"]["mesh"];
            var json = JObject.Parse(manifest.ToJson());
            var mesh = json["meshes"].Single(m => (int)m["id"] == meshId);
            string hash = (string)mesh["hash"];
            Assert.IsTrue(manifest.meshData.ContainsKey(hash));
            Assert.AreEqual(hash, BuildManifest.Hash(manifest.meshData[hash]));
        }

        [Test]
        public void ScenesLeaveNetworkObjectsOut()
        {
            var world = new GameObject("World");
            _created.Add(world);
            Child(world, "Floor", Vector3.zero).AddComponent<BoxCollider>();

            var player = new GameObject("NetworkedPlayer");
            _created.Add(player);
            player.AddComponent<CapsuleCollider>();

            var manifest = new BuildManifest();
            var scene = manifest.AddScene(0, "Test", new[] { world, player },
                gameObject => gameObject.name.StartsWith("Networked"));

            var colliders = (JArray)scene["colliders"];
            Assert.AreEqual(1, colliders.Count);
            Assert.AreEqual("box", (string)colliders[0]["shape"]);
        }
    }
}
