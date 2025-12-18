using PurrNet.Packing;

namespace PurrNet
{
    public interface IMigrateStrategy
    {
        BitPacker GetStrategy();

        void Migrate(BitPacker packer);
    }
}
