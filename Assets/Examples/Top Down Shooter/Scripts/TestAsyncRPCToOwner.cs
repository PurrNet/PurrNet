using System;
using System.Threading.Tasks;
using PurrNet;
using UnityEngine;
using Random = UnityEngine.Random;

public class TestAsyncRPCToOwner : NetworkIdentity
{
    [PurrButton]
    public void CallRpc()
    {
        DoRpc();
    }

    private async void DoRpc()
    {
        try
        {
            var res = await GetNumber(owner.GetValueOrDefault());
            Debug.Log($"Got number: {res}");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    [TargetRpc]
    private async Task<int> GetNumber(PlayerID playerID)
    {
        var v = Random.Range(0, 100);
        Debug.Log($"Going to send: {v}");
        await Task.Delay(1000);
        return v;
    }
}
