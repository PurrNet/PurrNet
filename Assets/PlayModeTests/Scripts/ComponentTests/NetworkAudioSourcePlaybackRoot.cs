using PurrNet;

public class NetworkAudioSourcePlaybackRoot : NetworkIdentity
{
    public static NetworkAudioSourcePlaybackRoot LocalInstance;

    public NetworkAudioSource networkAudio;

    public static void ResetAll()
    {
        LocalInstance = null;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned() => LocalInstance = this;

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;
    }
}
