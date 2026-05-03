using System;

namespace PurrNet.Modules
{
    internal readonly struct StaticGenericKey : IEquatable<StaticGenericKey>
    {
        readonly IntPtr _type;
        readonly string _methodName;
        readonly Type[] _types;
        readonly int _typesHash;

        public StaticGenericKey(IntPtr type, string methodName, Type[] types)
        {
            _type = type;
            _methodName = methodName;
            _types = types;

            var hash = new HashCode();
            for (int i = 0; i < types.Length; i++)
                hash.Add(types[i]);
            _typesHash = hash.ToHashCode();
        }

        // Used when storing this key in a long-lived dictionary, since the source
        // `types` array comes from a pool and gets recycled after the call.
        public StaticGenericKey CloneForStorage()
        {
            return new StaticGenericKey(_type, _methodName, (Type[])_types.Clone());
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_type, _methodName, _typesHash);
        }

        public bool Equals(StaticGenericKey other)
        {
            if (!_type.Equals(other._type) || _methodName != other._methodName ||
                _typesHash != other._typesHash)
                return false;

            if (_types.Length != other._types.Length)
                return false;

            for (int i = 0; i < _types.Length; i++)
            {
                if (_types[i] != other._types[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is StaticGenericKey other && Equals(other);
        }
    }
}
