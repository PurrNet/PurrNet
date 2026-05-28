using PurrNet;

// Child identity that makes a DestroyDuringSpawnScenario churn prefab a multi-identity hierarchy,
// so destroying it mid-spawn exercises cleanup of a spawn entry with more than one identity (root
// at list[0] + child). The root's activation brings this child active with it.
public class DestroyDuringSpawnChildIdentity : NetworkIdentity
{
}
