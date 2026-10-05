/// <summary>Server writes small, multipart, and replacement JPEG textures; every peer must receive callbacks.</summary>
public class SyncTextureAssetServerAuthScenario : SyncTextureAssetScenarioBase
{
    protected override bool ownerAuthority => false;
    protected override int barrierBase => 19300;
}
