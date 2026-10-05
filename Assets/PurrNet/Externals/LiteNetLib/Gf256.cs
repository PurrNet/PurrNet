namespace LiteNetLib
{
    // Arithmetic in GF(2^8) (polynomial 0x11D) for reliable-channel repair symbols.
    internal static class Gf256
    {
        private static readonly byte[] Exp = new byte[512];
        private static readonly byte[] Log = new byte[256];
        // Product of a and b at [a << 8 | b]; one lookup per symbol byte.
        private static readonly byte[] Product = new byte[256 * 256];

        static Gf256()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = (byte)x;
                Log[x] = (byte)i;
                x <<= 1;
                if ((x & 0x100) != 0)
                    x ^= 0x11D;
            }
            for (int i = 255; i < Exp.Length; i++)
                Exp[i] = Exp[i - 255];
            for (int a = 1; a < 256; a++)
                for (int b = 1; b < 256; b++)
                    Product[a << 8 | b] = Exp[Log[a] + Log[b]];
        }

        public static byte Inverse(byte value) => Exp[255 - Log[value]];

        // destination += coefficient * source over length bytes.
        public static void MulAdd(byte[] destination, int destinationOffset, byte[] source, int sourceOffset, int length, byte coefficient)
        {
            if (coefficient == 0)
                return;
            int row = coefficient << 8;
            for (int i = 0; i < length; i++)
                destination[destinationOffset + i] ^= Product[row | source[sourceOffset + i]];
        }

        public static void Scale(byte[] buffer, int offset, int length, byte coefficient)
        {
            int row = coefficient << 8;
            for (int i = 0; i < length; i++)
                buffer[offset + i] = Product[row | buffer[offset + i]];
        }

        // A nonzero coefficient both ends derive from a repair's seed and one sequence it covers.
        public static byte Coefficient(ushort seed, int sequence)
        {
            uint x = seed * 0x9E3779B1u ^ (uint)sequence * 0x85EBCA77u;
            x ^= x >> 15;
            x *= 0x2C1B3C6Du;
            x ^= x >> 12;
            return (byte)(1 + x % 255);
        }
    }
}
