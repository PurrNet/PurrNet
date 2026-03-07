using System;
using System.Threading.Tasks;
using PurrNet.Modules;
using PurrNet.Utils;

namespace PurrNet.Packing
{
    /// <summary>
    /// Implement on types that need async preparation around sync serialization.
    /// PrepareForPackAsync runs before packing (sender); PrepareAfterUnpackAsync runs after unpacking (receiver).
    /// Both return the prepared instance (for structs, return this after mutating).
    /// </summary>
    public interface IAsyncPackable
    {
        /// <summary>Prepare for sync serialization. Returns the prepared instance (for structs, return this after mutating).</summary>
        ValueTask<IAsyncPackable> PrepareForPackAsync();

        /// <summary>Hydrate after sync deserialization. Returns the prepared instance (for structs, return this after mutating).</summary>
        ValueTask<IAsyncPackable> PrepareAfterUnpackAsync();
    }
    public class NetworkRegister
    {
        [UsedByIL]
        public static void Hash(RuntimeTypeHandle handle)
        {
            var type = Type.GetTypeFromHandle(handle);
            Hasher.PrepareType(type);
        }
    }

    public interface IPackedAuto
    {
    }

    /// <summary>
    /// Marks a type as self-serializable, meaning its serializer
    /// should not cascade into base or derived class serializers.
    /// Only this type's serializer will be used.
    /// </summary>
    public interface IStandaloneSerializable {}

    public interface IPacked
    {
        void Write(BitPacker packer);

        void Read(BitPacker packer);
    }

    public interface IPackedSimple
    {
        void Serialize(BitPacker packer);
    }
}
