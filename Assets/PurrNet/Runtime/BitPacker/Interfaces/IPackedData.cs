using System;
using System.Threading.Tasks;
using PurrNet.Modules;
using PurrNet.Utils;

namespace PurrNet.Packing
{
    /// <summary>
    /// Implement on types that need async preparation around sync serialization.
    /// PrepareForPackAsync runs before packing (sender); PrepareAfterUnpackAsync runs after unpacking (receiver).
    /// The sync packer only sees the wire-ready representation.
    /// </summary>
    public interface IAsyncPackable
    {
        /// <summary>Prepare this instance for sync serialization. Called before Packer runs on sender.</summary>
        Task PrepareForPackAsync();

        /// <summary>Hydrate this instance after sync deserialization. Called after Packer runs on receiver.</summary>
        Task PrepareAfterUnpackAsync();
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
