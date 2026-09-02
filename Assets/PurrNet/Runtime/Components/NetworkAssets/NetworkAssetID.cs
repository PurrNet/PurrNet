using System;
using PurrNet.Packing;

namespace PurrNet
{
    public readonly struct NetworkAssetID : IEquatable<NetworkAssetID>, IPackedAuto
    {
        public readonly PackedInt value;

        public static NetworkAssetID invalid => new NetworkAssetID(-1);

        public bool isValid => value >= 0;

        public NetworkAssetID(int value)
        {
            this.value = value;
        }

        public static implicit operator NetworkAssetID(int value) => new NetworkAssetID(value);

        public static explicit operator int(NetworkAssetID id) => id.value;

        public bool Equals(NetworkAssetID other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkAssetID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value;
        }

        public static bool operator ==(NetworkAssetID a, NetworkAssetID b) => a.value == b.value;

        public static bool operator !=(NetworkAssetID a, NetworkAssetID b) => a.value != b.value;

        public override string ToString()
        {
            return value.ToString();
        }
    }
}
