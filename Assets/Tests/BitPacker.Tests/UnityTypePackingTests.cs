using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

public class UnityTypePackingTests
{
    private BitPacker packer;

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    [SetUp]
    public void Setup()
    {
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    // ───────────────────────────────────────────────
    //  Vector2
    // ───────────────────────────────────────────────

    [Test]
    public void Vector2_Zero()
    {
        var value = Vector2.zero;
        packer.ResetPositionAndMode(false);
        Packer<Vector2>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector2 read = default;
        Packer<Vector2>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Vector2_Typical()
    {
        var value = new Vector2(1.5f, -3.7f);
        packer.ResetPositionAndMode(false);
        Packer<Vector2>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector2 read = default;
        Packer<Vector2>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Vector2_Extremes()
    {
        var value = new Vector2(float.MinValue, float.MaxValue);
        packer.ResetPositionAndMode(false);
        Packer<Vector2>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector2 read = default;
        Packer<Vector2>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Vector2_Sequential()
    {
        var a = new Vector2(1f, 2f);
        var b = new Vector2(-5.5f, 100.1f);

        packer.ResetPositionAndMode(false);
        Packer<Vector2>.Write(packer, a);
        Packer<Vector2>.Write(packer, b);

        packer.ResetPositionAndMode(true);
        Vector2 readA = default, readB = default;
        Packer<Vector2>.Read(packer, ref readA);
        Packer<Vector2>.Read(packer, ref readB);

        Assert.That(readA, Is.EqualTo(a));
        Assert.That(readB, Is.EqualTo(b));
    }

    // ───────────────────────────────────────────────
    //  Vector3
    // ───────────────────────────────────────────────

    [Test]
    public void Vector3_Zero()
    {
        var value = Vector3.zero;
        packer.ResetPositionAndMode(false);
        Packer<Vector3>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector3 read = default;
        Packer<Vector3>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Vector3_Typical()
    {
        var value = new Vector3(1.5f, -3.7f, 42.0f);
        packer.ResetPositionAndMode(false);
        Packer<Vector3>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector3 read = default;
        Packer<Vector3>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Vector3_Extremes()
    {
        var value = new Vector3(float.MinValue, float.MaxValue, float.Epsilon);
        packer.ResetPositionAndMode(false);
        Packer<Vector3>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector3 read = default;
        Packer<Vector3>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Vector3_Sequential()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(-5.5f, 100.1f, 0f);

        packer.ResetPositionAndMode(false);
        Packer<Vector3>.Write(packer, a);
        Packer<Vector3>.Write(packer, b);

        packer.ResetPositionAndMode(true);
        Vector3 readA = default, readB = default;
        Packer<Vector3>.Read(packer, ref readA);
        Packer<Vector3>.Read(packer, ref readB);

        Assert.That(readA, Is.EqualTo(a));
        Assert.That(readB, Is.EqualTo(b));
    }

    // ───────────────────────────────────────────────
    //  Vector4
    // ───────────────────────────────────────────────

    [Test]
    public void Vector4_Zero()
    {
        var value = Vector4.zero;
        packer.ResetPositionAndMode(false);
        Packer<Vector4>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector4 read = default;
        Packer<Vector4>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Vector4_Typical()
    {
        var value = new Vector4(1.5f, -3.7f, 42.0f, 0.001f);
        packer.ResetPositionAndMode(false);
        Packer<Vector4>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector4 read = default;
        Packer<Vector4>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Vector4_Extremes()
    {
        var value = new Vector4(float.MinValue, float.MaxValue, float.Epsilon, float.NegativeInfinity);
        packer.ResetPositionAndMode(false);
        Packer<Vector4>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Vector4 read = default;
        Packer<Vector4>.Read(packer, ref read);
        Assert.That(read.x, Is.EqualTo(value.x));
        Assert.That(read.y, Is.EqualTo(value.y));
        Assert.That(read.z, Is.EqualTo(value.z));
        Assert.That(read.w, Is.EqualTo(value.w));
    }

    [Test]
    public void Vector4_Sequential()
    {
        var a = new Vector4(1f, 2f, 3f, 4f);
        var b = new Vector4(-5.5f, 100.1f, 0f, -0f);

        packer.ResetPositionAndMode(false);
        Packer<Vector4>.Write(packer, a);
        Packer<Vector4>.Write(packer, b);

        packer.ResetPositionAndMode(true);
        Vector4 readA = default, readB = default;
        Packer<Vector4>.Read(packer, ref readA);
        Packer<Vector4>.Read(packer, ref readB);

        Assert.That(readA, Is.EqualTo(a));
        Assert.That(readB, Is.EqualTo(b));
    }

    // ───────────────────────────────────────────────
    //  Quaternion
    // ───────────────────────────────────────────────

    [Test]
    public void Quaternion_Identity()
    {
        var value = Quaternion.identity;
        packer.ResetPositionAndMode(false);
        Packer<Quaternion>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Quaternion read = default;
        Packer<Quaternion>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Quaternion_Typical()
    {
        var value = Quaternion.Euler(45f, 90f, 180f);
        packer.ResetPositionAndMode(false);
        Packer<Quaternion>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Quaternion read = default;
        Packer<Quaternion>.Read(packer, ref read);
        Assert.That(read.x, Is.EqualTo(value.x));
        Assert.That(read.y, Is.EqualTo(value.y));
        Assert.That(read.z, Is.EqualTo(value.z));
        Assert.That(read.w, Is.EqualTo(value.w));
    }

    [Test]
    public void Quaternion_Raw()
    {
        var value = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        packer.ResetPositionAndMode(false);
        Packer<Quaternion>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Quaternion read = default;
        Packer<Quaternion>.Read(packer, ref read);
        Assert.That(read.x, Is.EqualTo(value.x));
        Assert.That(read.y, Is.EqualTo(value.y));
        Assert.That(read.z, Is.EqualTo(value.z));
        Assert.That(read.w, Is.EqualTo(value.w));
    }

    [Test]
    public void Quaternion_Sequential()
    {
        var a = Quaternion.Euler(10f, 20f, 30f);
        var b = Quaternion.Euler(270f, 0f, 45f);

        packer.ResetPositionAndMode(false);
        Packer<Quaternion>.Write(packer, a);
        Packer<Quaternion>.Write(packer, b);

        packer.ResetPositionAndMode(true);
        Quaternion readA = default, readB = default;
        Packer<Quaternion>.Read(packer, ref readA);
        Packer<Quaternion>.Read(packer, ref readB);

        Assert.That(readA.x, Is.EqualTo(a.x));
        Assert.That(readA.y, Is.EqualTo(a.y));
        Assert.That(readA.z, Is.EqualTo(a.z));
        Assert.That(readA.w, Is.EqualTo(a.w));

        Assert.That(readB.x, Is.EqualTo(b.x));
        Assert.That(readB.y, Is.EqualTo(b.y));
        Assert.That(readB.z, Is.EqualTo(b.z));
        Assert.That(readB.w, Is.EqualTo(b.w));
    }

    // ───────────────────────────────────────────────
    //  Color32
    // ───────────────────────────────────────────────

    [Test]
    public void Color32_Black()
    {
        var value = new Color32(0, 0, 0, 255);
        packer.ResetPositionAndMode(false);
        Packer<Color32>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Color32 read = default;
        Packer<Color32>.Read(packer, ref read);
        Assert.That(read.r, Is.EqualTo(value.r));
        Assert.That(read.g, Is.EqualTo(value.g));
        Assert.That(read.b, Is.EqualTo(value.b));
        Assert.That(read.a, Is.EqualTo(value.a));
    }

    [Test]
    public void Color32_White()
    {
        var value = new Color32(255, 255, 255, 255);
        packer.ResetPositionAndMode(false);
        Packer<Color32>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Color32 read = default;
        Packer<Color32>.Read(packer, ref read);
        Assert.That(read.r, Is.EqualTo(value.r));
        Assert.That(read.g, Is.EqualTo(value.g));
        Assert.That(read.b, Is.EqualTo(value.b));
        Assert.That(read.a, Is.EqualTo(value.a));
    }

    [Test]
    public void Color32_Typical()
    {
        var value = new Color32(128, 64, 200, 100);
        packer.ResetPositionAndMode(false);
        Packer<Color32>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Color32 read = default;
        Packer<Color32>.Read(packer, ref read);
        Assert.That(read.r, Is.EqualTo(value.r));
        Assert.That(read.g, Is.EqualTo(value.g));
        Assert.That(read.b, Is.EqualTo(value.b));
        Assert.That(read.a, Is.EqualTo(value.a));
    }

    [Test]
    public void Color32_Sequential()
    {
        var a = new Color32(10, 20, 30, 40);
        var b = new Color32(250, 200, 150, 100);

        packer.ResetPositionAndMode(false);
        Packer<Color32>.Write(packer, a);
        Packer<Color32>.Write(packer, b);

        packer.ResetPositionAndMode(true);
        Color32 readA = default, readB = default;
        Packer<Color32>.Read(packer, ref readA);
        Packer<Color32>.Read(packer, ref readB);

        Assert.That(readA.r, Is.EqualTo(a.r));
        Assert.That(readA.g, Is.EqualTo(a.g));
        Assert.That(readA.b, Is.EqualTo(a.b));
        Assert.That(readA.a, Is.EqualTo(a.a));

        Assert.That(readB.r, Is.EqualTo(b.r));
        Assert.That(readB.g, Is.EqualTo(b.g));
        Assert.That(readB.b, Is.EqualTo(b.b));
        Assert.That(readB.a, Is.EqualTo(b.a));
    }

    // ───────────────────────────────────────────────
    //  Color (delegates to Color32)
    // ───────────────────────────────────────────────

    [Test]
    public void Color_Typical()
    {
        var value = new Color(0.5f, 0.25f, 0.75f, 1f);
        packer.ResetPositionAndMode(false);
        Packer<Color>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Color read = default;
        Packer<Color>.Read(packer, ref read);

        // Color -> Color32 -> Color loses precision, so compare via Color32
        Color32 expected32 = value;
        Color32 read32 = read;
        Assert.That(read32.r, Is.EqualTo(expected32.r));
        Assert.That(read32.g, Is.EqualTo(expected32.g));
        Assert.That(read32.b, Is.EqualTo(expected32.b));
        Assert.That(read32.a, Is.EqualTo(expected32.a));
    }

    // ───────────────────────────────────────────────
    //  Rect
    // ───────────────────────────────────────────────

    [Test]
    public void Rect_Zero()
    {
        var value = Rect.zero;
        packer.ResetPositionAndMode(false);
        Packer<Rect>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Rect read = default;
        Packer<Rect>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Rect_Typical()
    {
        var value = new Rect(10.5f, -20.3f, 100f, 50.5f);
        packer.ResetPositionAndMode(false);
        Packer<Rect>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Rect read = default;
        Packer<Rect>.Read(packer, ref read);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void Rect_Sequential()
    {
        var a = new Rect(0, 0, 1, 1);
        var b = new Rect(-100f, 200f, 50.5f, 75.3f);

        packer.ResetPositionAndMode(false);
        Packer<Rect>.Write(packer, a);
        Packer<Rect>.Write(packer, b);

        packer.ResetPositionAndMode(true);
        Rect readA = default, readB = default;
        Packer<Rect>.Read(packer, ref readA);
        Packer<Rect>.Read(packer, ref readB);

        Assert.That(readA, Is.EqualTo(a));
        Assert.That(readB, Is.EqualTo(b));
    }

    // ───────────────────────────────────────────────
    //  Bounds (uses optimized Vector3 internally)
    // ───────────────────────────────────────────────

    [Test]
    public void Bounds_Typical()
    {
        var value = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(10f, 20f, 30f));
        packer.ResetPositionAndMode(false);
        Packer<Bounds>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Bounds read = default;
        Packer<Bounds>.Read(packer, ref read);
        Assert.That(read.center, Is.EqualTo(value.center));
        Assert.That(read.size, Is.EqualTo(value.size));
    }

    // ───────────────────────────────────────────────
    //  Ray / Ray2D (use optimized Vector2/Vector3)
    // ───────────────────────────────────────────────

    [Test]
    public void Ray_Typical()
    {
        var value = new Ray(new Vector3(1f, 2f, 3f), new Vector3(0f, 1f, 0f));
        packer.ResetPositionAndMode(false);
        Packer<Ray>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Ray read = default;
        Packer<Ray>.Read(packer, ref read);
        Assert.That(read.origin, Is.EqualTo(value.origin));
        Assert.That(read.direction, Is.EqualTo(value.direction));
    }

    [Test]
    public void Ray2D_Typical()
    {
        var value = new Ray2D(new Vector2(1f, 2f), new Vector2(0f, 1f));
        packer.ResetPositionAndMode(false);
        Packer<Ray2D>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        Ray2D read = default;
        Packer<Ray2D>.Read(packer, ref read);
        Assert.That(read.origin, Is.EqualTo(value.origin));
        Assert.That(read.direction, Is.EqualTo(value.direction));
    }

    // ───────────────────────────────────────────────
    //  Mixed sequential write/read
    //  Ensures bit alignment stays correct across types
    // ───────────────────────────────────────────────

    [Test]
    public void MixedTypes_Sequential()
    {
        var v2 = new Vector2(1.1f, 2.2f);
        var v3 = new Vector3(3.3f, 4.4f, 5.5f);
        var v4 = new Vector4(6.6f, 7.7f, 8.8f, 9.9f);
        var q = Quaternion.Euler(15f, 30f, 45f);
        var c = new Color32(100, 150, 200, 250);
        var r = new Rect(1f, 2f, 3f, 4f);
        int intVal = 42;
        bool boolVal = true;

        packer.ResetPositionAndMode(false);
        Packer<Vector2>.Write(packer, v2);
        Packer<int>.Write(packer, intVal);
        Packer<Vector3>.Write(packer, v3);
        Packer<bool>.Write(packer, boolVal);
        Packer<Vector4>.Write(packer, v4);
        Packer<Quaternion>.Write(packer, q);
        Packer<Color32>.Write(packer, c);
        Packer<Rect>.Write(packer, r);

        packer.ResetPositionAndMode(true);

        Vector2 readV2 = default;
        int readInt = default;
        Vector3 readV3 = default;
        bool readBool = default;
        Vector4 readV4 = default;
        Quaternion readQ = default;
        Color32 readC = default;
        Rect readR = default;

        Packer<Vector2>.Read(packer, ref readV2);
        Packer<int>.Read(packer, ref readInt);
        Packer<Vector3>.Read(packer, ref readV3);
        Packer<bool>.Read(packer, ref readBool);
        Packer<Vector4>.Read(packer, ref readV4);
        Packer<Quaternion>.Read(packer, ref readQ);
        Packer<Color32>.Read(packer, ref readC);
        Packer<Rect>.Read(packer, ref readR);

        Assert.That(readV2, Is.EqualTo(v2));
        Assert.That(readInt, Is.EqualTo(intVal));
        Assert.That(readV3, Is.EqualTo(v3));
        Assert.That(readBool, Is.EqualTo(boolVal));
        Assert.That(readV4, Is.EqualTo(v4));
        Assert.That(readQ.x, Is.EqualTo(q.x));
        Assert.That(readQ.y, Is.EqualTo(q.y));
        Assert.That(readQ.z, Is.EqualTo(q.z));
        Assert.That(readQ.w, Is.EqualTo(q.w));
        Assert.That(readC.r, Is.EqualTo(c.r));
        Assert.That(readC.g, Is.EqualTo(c.g));
        Assert.That(readC.b, Is.EqualTo(c.b));
        Assert.That(readC.a, Is.EqualTo(c.a));
        Assert.That(readR, Is.EqualTo(r));
    }
}
