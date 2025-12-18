using Cysharp.Threading.Tasks;
using PurrNet;

public static class InventoryUtils
{
    [ServerRpc]
    public static async UniTask<bool> RPC_MoveItem<T>(
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
        await UniTask.Yield();
        return true;
    }
}