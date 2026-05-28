using PurrNet;

// The canary for DestroyDuringSpawnScenario: spawned after a burst of destroyed-mid-spawn churn
// objects. It must replicate to every peer; if the churn wedged the pipeline it never arrives.
public class DestroyDuringSpawnMarkerIdentity : NetworkIdentity
{
    public static DestroyDuringSpawnMarkerIdentity LocalInstance;
    public static int ServerSpawnCount;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerSpawnCount = 0;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        if (asServer)
            ServerSpawnCount++;
    }
}
