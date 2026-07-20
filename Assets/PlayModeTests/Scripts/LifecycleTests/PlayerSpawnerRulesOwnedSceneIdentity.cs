using PurrNet;

public class PlayerSpawnerRulesOwnedSceneIdentity : NetworkIdentity
{
    public static bool Seeded;

    public static void ResetAll()
    {
        Seeded = false;
    }
}
