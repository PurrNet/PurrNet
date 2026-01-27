using JetBrains.Annotations;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Packing
{
    [UsedImplicitly]
    public static class BitPackerUnityExtensions
    {
        [RegisterPackers]
        static void RegisterEqualityOverrides()
        {
            PurrEquality<Quaternion>.OverrideDefault(new QuaternionEqualityComparer());
        }

        [UsedImplicitly]
        static ushort PackHalf(float value)
        {
            value = value switch
            {
                // clamp to -1 to 1
                < -1f => -1f,
                > 1f => 1f,
                _ => value
            };

            // map -1 to 1 to 0 to 1 and then to 0 to 65535
            return (ushort)((value * 0.5f + 0.5f) * 65535);
        }

        [UsedImplicitly]
        static float UnpackHalf(ushort value)
        {
            return value / 65535f * 2f - 1f;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, ForceMode value)
        {
            packer.Write((int)value);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref ForceMode value)
        {
            int val = default;
            packer.Read(ref val);
            value = (ForceMode)val;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, LayerMask value)
        {
            packer.Write((int)value);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref LayerMask value)
        {
            int val = default;
            packer.Read(ref val);
            value = val;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Vector2 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Vector2 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Ray value)
        {
            Packer<Vector3>.Write(packer, value.origin);
            Packer<Vector3>.Write(packer, value.direction);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Ray value)
        {
            Vector3 origin = default;
            Vector3 direction = default;

            Packer<Vector3>.Read(packer, ref origin);
            Packer<Vector3>.Read(packer, ref direction);

            value = new Ray(origin, direction);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Ray2D value)
        {
            Packer<Vector2>.Write(packer, value.origin);
            Packer<Vector2>.Write(packer, value.direction);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Ray2D value)
        {
            Vector2 origin = default;
            Vector2 direction = default;

            Packer<Vector2>.Read(packer, ref origin);
            Packer<Vector2>.Read(packer, ref direction);

            value = new Ray2D(origin, direction);
        }

        [UsedByIL]
        public static unsafe void Write(this BitPacker packer, Vector3 value)
        {
            var x = value.x;
            var y = value.y;
            var z = value.z;

            uint xbits = *(uint*)&x;
            uint ybits = *(uint*)&y;
            uint zbits = *(uint*)&z;

            ulong xyBits = ((ulong)xbits << 32) | ybits;

            packer.EnsureBitsExist(64 + 32);
            packer.WriteBitsWithoutChecks(xyBits, 64);
            packer.WriteBitsWithoutChecks(zbits, 32);
        }

        [UsedByIL]
        public static unsafe void Read(this BitPacker packer, ref Vector3 value)
        {
            ulong xyBits = packer.ReadBits(64);
            ulong zbits = packer.ReadBits(32);

            uint xbits = (uint)(xyBits >> 32);
            uint ybits = (uint)(xyBits & 0xFFFFFFFF);

            value.x = *(float*)&xbits;
            value.y = *(float*)&ybits;
            value.z = *(float*)&zbits;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Vector4 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
            packer.Write(value.w);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Vector4 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
            packer.Read(ref value.w);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Vector2Int value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Vector2Int value)
        {
            int x = default;
            int y = default;
            packer.Read(ref x);
            packer.Read(ref y);
            value = new Vector2Int(x, y);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Vector3Int value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Vector3Int value)
        {
            int x = default;
            int y = default;
            int z = default;
            packer.Read(ref x);
            packer.Read(ref y);
            packer.Read(ref z);
            value = new Vector3Int(x, y, z);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Quaternion value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
            packer.Write(value.w);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Quaternion value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
            packer.Read(ref value.w);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Color32 value)
        {
            packer.Write(value.r);
            packer.Write(value.g);
            packer.Write(value.b);
            packer.Write(value.a);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Color32 value)
        {
            byte r = default;
            byte g = default;
            byte b = default;
            byte a = default;

            packer.Read(ref r);
            packer.Read(ref g);
            packer.Read(ref b);
            packer.Read(ref a);

            value = new Color32(r, g, b, a);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Color value)
        {
            Color32 color32 = value;
            packer.Write(color32);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Color value)
        {
            Color32 color32 = default;
            packer.Read(ref color32);
            value = color32;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Rect value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.width);
            packer.Write(value.height);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Rect value)
        {
            float x = default;
            float y = default;
            float width = default;
            float height = default;

            packer.Read(ref x);
            packer.Read(ref y);
            packer.Read(ref width);
            packer.Read(ref height);

            value = new Rect(x, y, width, height);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, Bounds value)
        {
            packer.Write(value.center);
            packer.Write(value.size);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref Bounds value)
        {
            Vector3 center = default;
            Vector3 size = default;

            packer.Read(ref center);
            packer.Read(ref size);

            value = new Bounds(center, size);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, BoundsInt value)
        {
            packer.Write(value.center);
            packer.Write(value.size);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref BoundsInt value)
        {
            Vector3Int center = default;
            Vector3Int size = default;

            packer.Read(ref center);
            packer.Read(ref size);

            value = new BoundsInt(center, size);
        }


        [UsedByIL]
        public static void Write(this BitPacker packer, UnloadSceneOptions value)
        {
            packer.WriteInteger((int)value, 1);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref UnloadSceneOptions value)
        {
            long intValue = default;
            packer.ReadInteger(ref intValue, 1);
            value = (UnloadSceneOptions)intValue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, LoadSceneMode value)
        {
            packer.WriteInteger((int)value, 1);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref LoadSceneMode value)
        {
            long intValue = default;
            packer.ReadInteger(ref intValue, 1);
            value = (LoadSceneMode)intValue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, LocalPhysicsMode value)
        {
            packer.WriteInteger((int)value, 2);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref LocalPhysicsMode value)
        {
            long intValue = default;
            packer.ReadInteger(ref intValue, 2);
            value = (LocalPhysicsMode)intValue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, LoadSceneParameters value)
        {
            Packer<LoadSceneMode>.Write(packer, value.loadSceneMode);
            Packer<LocalPhysicsMode>.Write(packer, value.localPhysicsMode);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref LoadSceneParameters value)
        {
            LoadSceneMode loadSceneMode = default;
            LocalPhysicsMode localPhysicsMode = default;

            Packer<LoadSceneMode>.Read(packer, ref loadSceneMode);
            Packer<LocalPhysicsMode>.Read(packer, ref localPhysicsMode);

            value = new LoadSceneParameters
            {
                loadSceneMode = loadSceneMode,
                localPhysicsMode = localPhysicsMode
            };
        }
    }
}
