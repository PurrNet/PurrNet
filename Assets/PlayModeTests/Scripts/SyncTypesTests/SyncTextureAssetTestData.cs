using System;
using UnityEngine;

/// <summary>CPU-readable fixtures and an independent JPEG decode for the texture asset scenarios.</summary>
public sealed class SyncTextureAssetTestData
{
    public const int PhaseCount = 3;
    public const int TransferPartSize = 768;

    public readonly Texture2D source;
    public readonly byte[] jpeg;
    public readonly Color32[] decodedPixels;
    public readonly int width;
    public readonly int height;

    public SyncTextureAssetTestData(int phase)
    {
        width = phase == 1 ? 80 : phase == 0 ? 8 : 13;
        height = phase == 1 ? 56 : phase == 0 ? 8 : 9;
        source = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        uint state = 0x9E3779B9;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (phase == 1)
            {
                byte r = NextByte(ref state);
                byte g = NextByte(ref state);
                byte b = NextByte(ref state);
                pixels[i] = new Color32(r, g, b, 255);
            }
            else
            {
                pixels[i] = phase == 0
                    ? new Color32(35, 117, 201, 255)
                    : new Color32(213, 63, 29, 255);
            }
        }

        // SetPixels32 and JPEG encoding use CPU memory. No rendering or GPU readback is needed,
        // including in -nographics and dedicated-server players.
        source.SetPixels32(pixels);
        jpeg = source.EncodeToJPG();
        if (jpeg == null || jpeg.Length == 0)
            throw new InvalidOperationException("fixture JPEG encoding returned no bytes");

        var decoded = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        try
        {
            if (!decoded.LoadImage(jpeg))
                throw new InvalidOperationException("fixture JPEG decoding failed");
            if (decoded.width != width || decoded.height != height)
                throw new InvalidOperationException("fixture JPEG decoded to the wrong dimensions");
            decodedPixels = decoded.GetPixels32();
            bool hasColor = false;
            bool hasVariation = false;
            Color32 solidColor = pixels[0];
            for (int i = 0; i < decodedPixels.Length; i++)
            {
                var pixel = decodedPixels[i];
                hasColor |= pixel.r != 0 || pixel.g != 0 || pixel.b != 0;
                hasVariation |= !pixel.Equals(decodedPixels[0]);
                if (phase != 1 && (Math.Abs(pixel.r - solidColor.r) > 16 ||
                                   Math.Abs(pixel.g - solidColor.g) > 16 ||
                                   Math.Abs(pixel.b - solidColor.b) > 16))
                    throw new InvalidOperationException("fixture JPEG decode lost the expected solid source color");
            }
            if (!hasColor || (phase == 1 && !hasVariation))
                throw new InvalidOperationException("fixture JPEG decode lost its colored / nonuniform CPU pixels");
        }
        finally
        {
            UnityEngine.Object.Destroy(decoded);
        }
    }

    private static byte NextByte(ref uint state)
    {
        state = unchecked(state * 1664525 + 1013904223);
        return (byte)(state >> 24);
    }

    public string CompareBytes(ArraySegment<byte> actual)
    {
        if (actual.Count != jpeg.Length)
            return $"JPEG length={actual.Count}, expected {jpeg.Length}";
        for (int i = 0; i < jpeg.Length; i++)
        {
            if (actual.Array[actual.Offset + i] != jpeg[i])
                return $"JPEG byte {i}={actual.Array[actual.Offset + i]}, expected {jpeg[i]}";
        }
        return null;
    }

    public string CompareTexture(Texture2D actual)
    {
        if (!actual)
            return "callback texture is null";
        if (actual.width != width || actual.height != height)
            return $"texture dimensions={actual.width}x{actual.height}, expected {width}x{height}";

        var actualPixels = actual.GetPixels32();
        if (actualPixels.Length != decodedPixels.Length)
            return $"pixel count={actualPixels.Length}, expected {decodedPixels.Length}";
        for (int i = 0; i < decodedPixels.Length; i++)
        {
            var a = actualPixels[i];
            var e = decodedPixels[i];
            if (a.r != e.r || a.g != e.g || a.b != e.b || a.a != e.a)
                return $"decoded pixel {i}=({a.r},{a.g},{a.b},{a.a}), expected ({e.r},{e.g},{e.b},{e.a})";
        }
        return null;
    }
}
