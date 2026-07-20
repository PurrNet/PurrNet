using System.Threading.Tasks;
using JetBrains.Annotations;
using PurrNet;

public static class InventoryUtils
{
    [ServerRpc]
    [CanBeNull]
    public static async Task<bool> RPC_MoveItem<T>(
        PlayerID sourceInventoryOwner,
        InventoryType sourceInventoryType,
        CompactGuidPurr uniqueItemId,
        int itemIndex,
        PlayerID targetInventoryOwner,
        InventoryType targetInventoryType,
        int targetSlotIndex,
        RPCInfo info = default)
        where T : InventoryData<T>
    {
        await Task.Yield();
        return true;
    }
}
