using PurrNet;
using UnityEngine;

public class NetworkAudioSourceTest : NetworkIdentity
{
    [SerializeField] private NetworkAudioSource _netAudio;
    [SerializeField] private AudioClip _oneShotClip;
    [SerializeField] private float _targetVolume = 0.5f;

    private void Reset()
    {
        _netAudio = GetComponent<NetworkAudioSource>();
    }

    [PurrButton("Play")]
    private void ButtonPlay() => _netAudio.Play();

    [PurrButton("Stop")]
    private void ButtonStop() => _netAudio.Stop();

    [PurrButton("Pause")]
    private void ButtonPause() => _netAudio.Pause();

    [PurrButton("UnPause")]
    private void ButtonUnPause() => _netAudio.UnPause();

    [PurrButton("Play One Shot")]
    private void ButtonPlayOneShot()
    {
        if (_oneShotClip)
            _netAudio.PlayOneShot(_oneShotClip);
        else
            Debug.LogWarning("No one-shot clip assigned.");
    }

    [PurrButton("Volume Up (+0.1)")]
    private void ButtonVolumeUp() => _netAudio.volume = Mathf.Clamp01(_netAudio.volume + 0.1f);

    [PurrButton("Volume Down (-0.1)")]
    private void ButtonVolumeDown() => _netAudio.volume = Mathf.Clamp01(_netAudio.volume - 0.1f);

    [PurrButton("Set Target Volume")]
    private void ButtonSetVolume() => _netAudio.volume = _targetVolume;

    [PurrButton("Pitch Up (+0.1)")]
    private void ButtonPitchUp() => _netAudio.pitch = Mathf.Clamp(_netAudio.pitch + 0.1f, -3f, 3f);

    [PurrButton("Pitch Down (-0.1)")]
    private void ButtonPitchDown() => _netAudio.pitch = Mathf.Clamp(_netAudio.pitch - 0.1f, -3f, 3f);

    [PurrButton("Toggle Loop")]
    private void ButtonToggleLoop() => _netAudio.loop = !_netAudio.loop;

    [PurrButton("Toggle Mute")]
    private void ButtonToggleMute() => _netAudio.mute = !_netAudio.mute;
}
