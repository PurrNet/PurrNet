/// <summary>A remote owner writes textures; the server must relay each replacement to the other clients.</summary>
public class SyncTextureAssetOwnerAuthScenario : SyncTextureAssetScenarioBase
{
    protected override bool ownerAuthority => true;
    protected override int barrierBase => 19320;
}
