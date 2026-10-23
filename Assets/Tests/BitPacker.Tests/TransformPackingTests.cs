using System.Text.RegularExpressions;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.TestTools;

public struct TransformReferenceState : IPackedAuto
{
    public Transform root;
    public Vector3 gravity;
    public Transform camera;
    public GameObject frame;
    public float stepHeight;
}

public class TransformPackingTests
{
    GameObject _root;

    [SetUp]
    public void Setup()
    {
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();

        // A plain scene hierarchy: no spawned NetworkIdentity, not a prefab, not a network asset.
        _root = new GameObject("Character");
        var camera = new GameObject("Camera");
        camera.transform.SetParent(_root.transform);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
    }

    [Test]
    public void UnresolvableTransformsReadBackAsNullWithoutShiftingLaterFields()
    {
        var state = new TransformReferenceState
        {
            root = _root.transform,
            gravity = new Vector3(0f, -9.81f, 0f),
            camera = _root.transform.GetChild(0),
            frame = _root,
            stepHeight = 0.35f
        };

        LogAssert.Expect(LogType.Warning, new Regex("packed as null"));
        var copy = PurrCopy<TransformReferenceState>.Copy(state);

        Assert.That(copy.root, Is.Null);
        Assert.That(copy.camera, Is.Null);
        Assert.That(copy.frame, Is.Null);
        Assert.That(copy.gravity, Is.EqualTo(state.gravity));
        Assert.That(copy.stepHeight, Is.EqualTo(state.stepHeight));
    }

    [Test]
    public void UnresolvableTransformUsesTheNullEncoding()
    {
        using var written = BitPackerPool.Get();
        using var nulled = BitPackerPool.Get();

        Packer<Transform>.Write(written, _root.transform);
        Packer<Transform>.Write(nulled, null);

        Assert.That(written.positionInBits, Is.EqualTo(nulled.positionInBits));
    }
}
