using System;
using JetBrains.Annotations;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Audio Source")]
    [PurrDocs("plug-n-play-components/networkaudiosource")]
    public sealed class NetworkAudioSource : NetworkIdentity, ITick
    {
        [Tooltip("The audio source to sync")]
        [SerializeField, PurrLock] private AudioSource _audioSource;

        [Tooltip(
            "If true the owner has authority over this audio source, if no owner is set it is controlled by the server")]
        [SerializeField, PurrLock]
        private bool _ownerAuth = true;

        /// <summary>
        /// If true the owner has authority over this audio source, if no owner is set it is controlled by the server
        /// </summary>
        public bool ownerAuth => _ownerAuth;

        /// <summary>
        /// The audio source being synced
        /// </summary>
        public AudioSource audioSource => _audioSource;

        AudioDirtyFlags _dirtyFlags;

        private void Reset()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        AudioSourceState CaptureState()
        {
            return new AudioSourceState
            {
                clip = _audioSource.clip,
                volume = _audioSource.volume,
                pitch = _audioSource.pitch,
                loop = _audioSource.loop,
                mute = _audioSource.mute,
                spatialBlend = _audioSource.spatialBlend,
                minDistance = _audioSource.minDistance,
                maxDistance = _audioSource.maxDistance,
                playState = GetPlayState(),
                time = _audioSource.time
            };
        }

        AudioPlayState GetPlayState()
        {
            if (_audioSource.isPlaying)
                return AudioPlayState.Playing;

            if (_audioSource.time > 0f && !_audioSource.isPlaying)
                return AudioPlayState.Paused;

            return AudioPlayState.Stopped;
        }

        void ApplyDelta(AudioSourceDelta delta)
        {
            if (!_audioSource) return;

            var flags = delta.flags;

            if ((flags & AudioDirtyFlags.Clip) != 0)
                _audioSource.clip = delta.state.clip;
            if ((flags & AudioDirtyFlags.Volume) != 0)
                _audioSource.volume = delta.state.volume;
            if ((flags & AudioDirtyFlags.Pitch) != 0)
                _audioSource.pitch = delta.state.pitch;
            if ((flags & AudioDirtyFlags.Loop) != 0)
                _audioSource.loop = delta.state.loop;
            if ((flags & AudioDirtyFlags.Mute) != 0)
                _audioSource.mute = delta.state.mute;
            if ((flags & AudioDirtyFlags.SpatialBlend) != 0)
                _audioSource.spatialBlend = delta.state.spatialBlend;
            if ((flags & AudioDirtyFlags.MinDistance) != 0)
                _audioSource.minDistance = delta.state.minDistance;
            if ((flags & AudioDirtyFlags.MaxDistance) != 0)
                _audioSource.maxDistance = delta.state.maxDistance;

            if ((flags & AudioDirtyFlags.PlayState) != 0)
            {
                switch (delta.state.playState)
                {
                    case AudioPlayState.Playing:
                        if ((flags & AudioDirtyFlags.Time) != 0)
                            _audioSource.time = delta.state.time;
                        _audioSource.Play();
                        break;
                    case AudioPlayState.Paused:
                        _audioSource.Pause();
                        break;
                    case AudioPlayState.Stopped:
                        _audioSource.Stop();
                        break;
                }
            }
            else if ((flags & AudioDirtyFlags.Time) != 0 && _audioSource.isPlaying)
            {
                float drift = Mathf.Abs(_audioSource.time - delta.state.time);
                if (drift > 0.1f)
                    _audioSource.time = delta.state.time;
            }

        }

        void ApplyFullState(AudioSourceState state)
        {
            if (!_audioSource) return;

            _audioSource.clip = state.clip;
            _audioSource.volume = state.volume;
            _audioSource.pitch = state.pitch;
            _audioSource.loop = state.loop;
            _audioSource.mute = state.mute;
            _audioSource.spatialBlend = state.spatialBlend;
            _audioSource.minDistance = state.minDistance;
            _audioSource.maxDistance = state.maxDistance;

            switch (state.playState)
            {
                case AudioPlayState.Playing:
                    _audioSource.time = state.time;
                    _audioSource.Play();
                    break;
                case AudioPlayState.Paused:
                    _audioSource.Pause();
                    break;
                case AudioPlayState.Stopped:
                    _audioSource.Stop();
                    break;
            }

        }

        protected override void OnObserverAdded(PlayerID player)
        {
            if (!IsController(_ownerAuth))
                return;

            var state = CaptureState();

            if (isServer)
            {
                ReconcileState(player, state);
            }
            else
            {
                ForwardReconcileToServer(player, state);
            }
        }

        public void OnTick(float delta)
        {
            if (_dirtyFlags == AudioDirtyFlags.None) return;

            if (!IsController(_ownerAuth))
            {
                _dirtyFlags = AudioDirtyFlags.None;
                return;
            }

            var state = CaptureState();
            var packet = new AudioSourceDelta { flags = _dirtyFlags, state = state };
            bool needsReliable = (_dirtyFlags & AudioDirtyFlags.ReliableMask) != 0;

            if (isServer)
            {
                if (needsReliable)
                    ApplyDeltaOnObserversReliable(packet);
                else
                    ApplyDeltaOnObservers(packet);
            }
            else
            {
                if (needsReliable)
                    ForwardDeltaToServerReliable(packet);
                else
                    ForwardDeltaToServer(packet);
            }

            _dirtyFlags = AudioDirtyFlags.None;
        }

        /// <summary>
        /// Whether the audio source is currently playing
        /// </summary>
        public bool isPlaying => _audioSource && _audioSource.isPlaying;

        /// <summary>
        /// The audio clip to play
        /// </summary>
        public AudioClip clip
        {
            get => _audioSource ? _audioSource.clip : null;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.clip = value;
                _dirtyFlags |= AudioDirtyFlags.Clip;
            }
        }

        /// <summary>
        /// The volume of the audio source (0.0 to 1.0)
        /// </summary>
        public float volume
        {
            get => _audioSource ? _audioSource.volume : 0f;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.volume = value;
                _dirtyFlags |= AudioDirtyFlags.Volume;
            }
        }

        /// <summary>
        /// The pitch of the audio source
        /// </summary>
        public float pitch
        {
            get => _audioSource ? _audioSource.pitch : 1f;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.pitch = value;
                _dirtyFlags |= AudioDirtyFlags.Pitch;
            }
        }

        /// <summary>
        /// Whether the audio source should loop
        /// </summary>
        public bool loop
        {
            get => _audioSource && _audioSource.loop;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.loop = value;
                _dirtyFlags |= AudioDirtyFlags.Loop;
            }
        }

        /// <summary>
        /// Whether the audio source is muted
        /// </summary>
        public bool mute
        {
            get => _audioSource && _audioSource.mute;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.mute = value;
                _dirtyFlags |= AudioDirtyFlags.Mute;
            }
        }

        /// <summary>
        /// The spatial blend of the audio source (0.0 = 2D, 1.0 = 3D)
        /// </summary>
        public float spatialBlend
        {
            get => _audioSource ? _audioSource.spatialBlend : 0f;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.spatialBlend = value;
                _dirtyFlags |= AudioDirtyFlags.SpatialBlend;
            }
        }

        /// <summary>
        /// Within the min distance the audio source will cease to grow louder in volume
        /// </summary>
        public float minDistance
        {
            get => _audioSource ? _audioSource.minDistance : 1f;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.minDistance = value;
                _dirtyFlags |= AudioDirtyFlags.MinDistance;
            }
        }

        /// <summary>
        /// The distance a sound stops attenuating at
        /// </summary>
        public float maxDistance
        {
            get => _audioSource ? _audioSource.maxDistance : 500f;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.maxDistance = value;
                _dirtyFlags |= AudioDirtyFlags.MaxDistance;
            }
        }

        /// <summary>
        /// Playback position in seconds
        /// </summary>
        public float time
        {
            get => _audioSource ? _audioSource.time : 0f;
            set
            {
                if (!IsController(_ownerAuth)) return;
                _audioSource.time = value;
                _dirtyFlags |= AudioDirtyFlags.Time;
            }
        }

        /// <summary>
        /// Plays the audio source
        /// </summary>
        public void Play()
        {
            if (!IsController(_ownerAuth)) return;
            _audioSource.Play();
            _dirtyFlags |= AudioDirtyFlags.PlayState | AudioDirtyFlags.Time;
        }

        /// <summary>
        /// Sets the clip and plays the audio source
        /// </summary>
        public void Play(AudioClip audioClip)
        {
            if (!IsController(_ownerAuth)) return;
            _audioSource.clip = audioClip;
            _audioSource.Play();
            _dirtyFlags |= AudioDirtyFlags.Clip | AudioDirtyFlags.PlayState | AudioDirtyFlags.Time;
        }

        /// <summary>
        /// Stops the audio source
        /// </summary>
        public void Stop()
        {
            if (!IsController(_ownerAuth)) return;
            _audioSource.Stop();
            _dirtyFlags |= AudioDirtyFlags.PlayState;
        }

        /// <summary>
        /// Pauses the audio source
        /// </summary>
        public void Pause()
        {
            if (!IsController(_ownerAuth)) return;
            _audioSource.Pause();
            _dirtyFlags |= AudioDirtyFlags.PlayState;
        }

        /// <summary>
        /// Resumes the audio source from a paused state
        /// </summary>
        public void UnPause()
        {
            if (!IsController(_ownerAuth)) return;
            _audioSource.UnPause();
            _dirtyFlags |= AudioDirtyFlags.PlayState | AudioDirtyFlags.Time;
        }

        /// <summary>
        /// Plays an AudioClip as a one-shot sound effect. Does not affect the current clip or play state.
        /// The clip must be registered in NetworkAssets.
        /// </summary>
        /// <param name="audioClip">The clip to play</param>
        /// <param name="volumeScale">Volume scale for this one-shot (default 1.0)</param>
        public void PlayOneShot(AudioClip audioClip, float volumeScale = 1f)
        {
            if (!IsController(_ownerAuth)) return;

            _audioSource.PlayOneShot(audioClip, volumeScale);

            if (isServer)
            {
                OneShotOnObservers(audioClip, volumeScale);
            }
            else
            {
                ForwardOneShotToServer(audioClip, volumeScale);
            }
        }

        [ObserversRpc(excludeSender: true, channel: Channel.Unreliable)]
        private void ApplyDeltaOnObservers(AudioSourceDelta delta)
        {
            if (IsController(_ownerAuth)) return;
            ApplyDelta(delta);
        }

        [ServerRpc(channel: Channel.Unreliable)]
        private void ForwardDeltaToServer(AudioSourceDelta delta)
        {
            if (!_ownerAuth) return;
            ApplyDelta(delta);
            ApplyDeltaOnObservers(delta);
        }

        [ObserversRpc(excludeSender: true)]
        private void ApplyDeltaOnObserversReliable(AudioSourceDelta delta)
        {
            if (IsController(_ownerAuth)) return;
            ApplyDelta(delta);
        }

        [ServerRpc]
        private void ForwardDeltaToServerReliable(AudioSourceDelta delta)
        {
            if (!_ownerAuth) return;
            ApplyDelta(delta);
            ApplyDeltaOnObserversReliable(delta);
        }

        [TargetRpc]
        private void ReconcileState([UsedImplicitly] PlayerID player, AudioSourceState state)
        {
            if (IsController(_ownerAuth)) return;
            ApplyFullState(state);
        }

        [ServerRpc]
        private void ForwardReconcileToServer(PlayerID target, AudioSourceState state)
        {
            if (!_ownerAuth) return;
            ReconcileState(target, state);
        }

        [ObserversRpc(excludeSender: true)]
        private void OneShotOnObservers(AudioClip audioClip, float volumeScale)
        {
            if (IsController(_ownerAuth)) return;
            if (_audioSource && audioClip)
                _audioSource.PlayOneShot(audioClip, volumeScale);
        }

        [ServerRpc]
        private void ForwardOneShotToServer(AudioClip audioClip, float volumeScale)
        {
            if (!_ownerAuth) return;
            if (_audioSource && audioClip)
                _audioSource.PlayOneShot(audioClip, volumeScale);
            OneShotOnObservers(audioClip, volumeScale);
        }
    }

    public enum AudioPlayState : byte
    {
        Stopped,
        Playing,
        Paused
    }

    [Flags]
    enum AudioDirtyFlags : ushort
    {
        None        = 0,
        Clip        = 1 << 0,
        Volume      = 1 << 1,
        Pitch       = 1 << 2,
        Loop        = 1 << 3,
        Mute        = 1 << 4,
        SpatialBlend = 1 << 5,
        MinDistance  = 1 << 6,
        MaxDistance  = 1 << 7,
        PlayState   = 1 << 8,
        Time        = 1 << 9,

        /// <summary>
        /// Flags that represent discrete state changes which must arrive reliably.
        /// </summary>
        ReliableMask = Clip | PlayState
    }

    struct AudioSourceState
    {
        public AudioClip clip;
        public float volume;
        public float pitch;
        public bool loop;
        public bool mute;
        public float spatialBlend;
        public float minDistance;
        public float maxDistance;
        public AudioPlayState playState;
        public float time;
    }

    struct AudioSourceDelta : IPacked
    {
        public AudioDirtyFlags flags;
        public AudioSourceState state;

        public void Write(BitPacker packer)
        {
            Packer<ushort>.Write(packer, (ushort)flags);

            if ((flags & AudioDirtyFlags.Clip) != 0)
                Packer<AudioClip>.Write(packer, state.clip);
            if ((flags & AudioDirtyFlags.Volume) != 0)
                Packer<float>.Write(packer, state.volume);
            if ((flags & AudioDirtyFlags.Pitch) != 0)
                Packer<float>.Write(packer, state.pitch);
            if ((flags & AudioDirtyFlags.Loop) != 0)
                packer.WriteBit(state.loop);
            if ((flags & AudioDirtyFlags.Mute) != 0)
                packer.WriteBit(state.mute);
            if ((flags & AudioDirtyFlags.SpatialBlend) != 0)
                Packer<float>.Write(packer, state.spatialBlend);
            if ((flags & AudioDirtyFlags.MinDistance) != 0)
                Packer<float>.Write(packer, state.minDistance);
            if ((flags & AudioDirtyFlags.MaxDistance) != 0)
                Packer<float>.Write(packer, state.maxDistance);
            if ((flags & AudioDirtyFlags.PlayState) != 0)
                Packer<AudioPlayState>.Write(packer, state.playState);
            if ((flags & AudioDirtyFlags.Time) != 0)
                Packer<float>.Write(packer, state.time);
        }

        public void Read(BitPacker packer)
        {
            ushort raw = default;
            Packer<ushort>.Read(packer, ref raw);
            flags = (AudioDirtyFlags)raw;

            if ((flags & AudioDirtyFlags.Clip) != 0)
                Packer<AudioClip>.Read(packer, ref state.clip);
            if ((flags & AudioDirtyFlags.Volume) != 0)
                Packer<float>.Read(packer, ref state.volume);
            if ((flags & AudioDirtyFlags.Pitch) != 0)
                Packer<float>.Read(packer, ref state.pitch);
            if ((flags & AudioDirtyFlags.Loop) != 0)
                state.loop = packer.ReadBit();
            if ((flags & AudioDirtyFlags.Mute) != 0)
                state.mute = packer.ReadBit();
            if ((flags & AudioDirtyFlags.SpatialBlend) != 0)
                Packer<float>.Read(packer, ref state.spatialBlend);
            if ((flags & AudioDirtyFlags.MinDistance) != 0)
                Packer<float>.Read(packer, ref state.minDistance);
            if ((flags & AudioDirtyFlags.MaxDistance) != 0)
                Packer<float>.Read(packer, ref state.maxDistance);
            if ((flags & AudioDirtyFlags.PlayState) != 0)
                Packer<AudioPlayState>.Read(packer, ref state.playState);
            if ((flags & AudioDirtyFlags.Time) != 0)
                Packer<float>.Read(packer, ref state.time);
        }
    }
}
