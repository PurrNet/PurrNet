using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Modules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet.Recording
{
    /// <summary>
    /// Where the game's own calls that start a sound, a particle system or an animation go through, so a recording
    /// knows about them the moment they happen, without having to look for them. PurrNet's code generation points
    /// the game's calls of the engine methods here (<c>source.PlayOneShot(clip)</c> becomes
    /// <c>RecordedEffects.PlayOneShot(source, clip)</c>); each method makes the engine call it stands for, and tells
    /// the recording when one is running on this machine. Code that is not woven (the engine itself, animation
    /// events, UnityEvents wired in the inspector, assemblies that do not reference PurrNet) is not seen here.
    /// </summary>
    public static class RecordedEffects
    {
        /// <summary>True while a recording on this machine takes effects.</summary>
        public static bool enabled;

        static readonly List<RecordingSession> _sessions = new List<RecordingSession>(1);

        internal static void Add(RecordingSession session)
        {
            if (!_sessions.Contains(session))
                _sessions.Add(session);
            enabled = true;
        }

        internal static void Remove(RecordingSession session)
        {
            _sessions.Remove(session);
            enabled = _sessions.Count > 0;
        }

        /// <summary>True while a recording on this machine has entities, and wants to hear what the game does to them.</summary>
        public static bool watching;

        static readonly List<RecordingSession> _watchers = new List<RecordingSession>(1);

        internal static void Watch(RecordingSession session)
        {
            if (!_watchers.Contains(session))
                _watchers.Add(session);
            watching = true;
        }

        internal static void Unwatch(RecordingSession session)
        {
            _watchers.Remove(session);
            watching = _watchers.Count > 0;
        }

        /// <summary>
        /// A recording that cannot hear the game goes back to looking at every entity on every tick.
        /// </summary>
        static void WatchFailed(RecordingSession session, Exception e)
        {
            Unwatch(session);
            session.WatchFailed(e);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetActive(GameObject gameObject, bool value)
        {
            gameObject.SetActive(value);
            if (watching)
                ActiveChanged(gameObject);
        }

        static void ActiveChanged(GameObject gameObject)
        {
            for (int i = _watchers.Count - 1; i >= 0; i--)
            {
                var session = _watchers[i];
                try { session.ActiveChanged(gameObject); }
                catch (Exception e) { WatchFailed(session, e); }
            }
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void set_enabled(Renderer renderer, bool value)
        {
            renderer.enabled = value;
            if (watching)
                RendererChanged(renderer);
        }

        static void RendererChanged(Renderer renderer)
        {
            for (int i = _watchers.Count - 1; i >= 0; i--)
            {
                var session = _watchers[i];
                try { session.RendererChanged(renderer); }
                catch (Exception e) { WatchFailed(session, e); }
            }
        }

#if UNITY_PHYSICS_3D
        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void set_enabled(Collider collider, bool value)
        {
            collider.enabled = value;
            if (watching)
                ShapeChanged(collider);
        }

        static void ShapeChanged(Component component)
        {
            for (int i = _watchers.Count - 1; i >= 0; i--)
            {
                var session = _watchers[i];
                try { session.ShapeChanged(component); }
                catch (Exception e) { WatchFailed(session, e); }
            }
        }
#endif

        /// <summary>
        /// The game's call has been made by the time a recording hears of it, and nothing a recording does may
        /// reach the game: a recording that fails here stops taking effects.
        /// </summary>
        static void Failed(RecordingSession session, Exception e)
        {
            Remove(session);
            session.EffectsFailed(e);
        }

        /// <summary>An object the game instantiated: when it carries effects of its own they are followed.</summary>
        internal static T Created<T>(T instance) where T : Object
        {
            if (enabled)
                NoteCreated(instance);
            return instance;
        }

        static void NoteCreated(Object instance)
        {
            GameObject go = instance is GameObject created ? created : instance is Component component ? component.gameObject : null;
            if (go is null)
                return;
            for (int i = _sessions.Count - 1; i >= 0; i--)
                _sessions[i].ObjectCreated(go);
        }

        /// <summary>The game destroys an object: one that was followed for its effects has ended.</summary>
        internal static void Destroyed(Object instance)
        {
            if (watching)
            {
                for (int i = _watchers.Count - 1; i >= 0; i--)
                {
                    var session = _watchers[i];
                    try { session.PartDestroyed(instance); }
                    catch (Exception e) { WatchFailed(session, e); }
                }
            }
            if (!enabled || !(instance is GameObject go))
                return;
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.ObjectDestroyed(go); }
                catch (Exception e) { Failed(session, e); }
            }
        }

#if PURRNET_AUDIO
        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(AudioSource source)
        {
            source.Play();
            if (enabled)
                Sound(source, null, 1f, 0);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayOneShot(AudioSource source, AudioClip clip)
        {
            source.PlayOneShot(clip);
            if (enabled)
                Sound(source, clip, 1f, RecordedAudio.OneShot);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayOneShot(AudioSource source, AudioClip clip, float volumeScale)
        {
            source.PlayOneShot(clip, volumeScale);
            if (enabled)
                Sound(source, clip, volumeScale, RecordedAudio.OneShot);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayClipAtPoint(AudioClip clip, Vector3 position)
        {
            AudioSource.PlayClipAtPoint(clip, position);
            if (enabled)
                SoundAtPoint(clip, position, 1f);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayClipAtPoint(AudioClip clip, Vector3 position, float volume)
        {
            AudioSource.PlayClipAtPoint(clip, position, volume);
            if (enabled)
                SoundAtPoint(clip, position, volume);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Stop(AudioSource source)
        {
            source.Stop();
            if (!enabled)
                return;
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.SoundStopped(source); }
                catch (Exception e) { Failed(session, e); }
            }
        }

        static void Sound(AudioSource source, AudioClip clip, float volumeScale, byte flags)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.SoundPlayed(source, clip, volumeScale, flags); }
                catch (Exception e) { Failed(session, e); }
            }
        }

        static void SoundAtPoint(AudioClip clip, Vector3 position, float volume)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.SoundAtPoint(clip, position, volume); }
                catch (Exception e) { Failed(session, e); }
            }
        }
#endif

#if PURRNET_PARTICLES
        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(ParticleSystem system)
        {
            system.Play();
            if (enabled)
                Played(system);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(ParticleSystem system, bool withChildren)
        {
            system.Play(withChildren);
            if (enabled)
                Played(system);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Stop(ParticleSystem system)
        {
            system.Stop();
            if (enabled)
                Stopped(system);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Stop(ParticleSystem system, bool withChildren)
        {
            system.Stop(withChildren);
            if (enabled)
                Stopped(system);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Stop(ParticleSystem system, bool withChildren, ParticleSystemStopBehavior stopBehavior)
        {
            system.Stop(withChildren, stopBehavior);
            if (enabled)
                Stopped(system);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit(ParticleSystem system, int count)
        {
            system.Emit(count);
            if (!enabled)
                return;
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.ParticlesEmitted(system, count); }
                catch (Exception e) { Failed(session, e); }
            }
        }

        static void Played(ParticleSystem system)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.ParticlesPlayed(system); }
                catch (Exception e) { Failed(session, e); }
            }
        }

        static void Stopped(ParticleSystem system)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.ParticlesStopped(system); }
                catch (Exception e) { Failed(session, e); }
            }
        }
#endif

#if UNITY_ANIMATION
        static void Called(Animator animator, byte kind, string name)
        {
            int hash = Animator.StringToHash(name);
            Called(animator, kind, hash, name);
        }

        static void Called(Animator animator, byte kind, int hash, string name = null)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.AnimatorCalled(animator, kind, hash, name); }
                catch (Exception e) { Failed(session, e); }
            }
        }

        /// <summary>
        /// A parameter the game sets writes no row (many games set one every frame); the animator is asked what
        /// came of it on the same tick.
        /// </summary>
        static void Touched(Animator animator)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                try { session.AnimatorTouched(animator); }
                catch (Exception e) { Failed(session, e); }
            }
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBool(Animator animator, string name, bool value)
        {
            animator.SetBool(name, value);
            if (enabled)
                Touched(animator);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBool(Animator animator, int id, bool value)
        {
            animator.SetBool(id, value);
            if (enabled)
                Touched(animator);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetInteger(Animator animator, string name, int value)
        {
            animator.SetInteger(name, value);
            if (enabled)
                Touched(animator);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetInteger(Animator animator, int id, int value)
        {
            animator.SetInteger(id, value);
            if (enabled)
                Touched(animator);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFloat(Animator animator, string name, float value)
        {
            animator.SetFloat(name, value);
            if (enabled)
                Touched(animator);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFloat(Animator animator, int id, float value)
        {
            animator.SetFloat(id, value);
            if (enabled)
                Touched(animator);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFloat(Animator animator, string name, float value, float dampTime, float deltaTime)
        {
            animator.SetFloat(name, value, dampTime, deltaTime);
            if (enabled)
                Touched(animator);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFloat(Animator animator, int id, float value, float dampTime, float deltaTime)
        {
            animator.SetFloat(id, value, dampTime, deltaTime);
            if (enabled)
                Touched(animator);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetTrigger(Animator animator, string name)
        {
            animator.SetTrigger(name);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Trigger, name);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetTrigger(Animator animator, int id)
        {
            animator.SetTrigger(id);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Trigger, id);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(Animator animator, string stateName)
        {
            animator.Play(stateName);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(Animator animator, string stateName, int layer)
        {
            animator.Play(stateName, layer);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(Animator animator, string stateName, int layer, float normalizedTime)
        {
            animator.Play(stateName, layer, normalizedTime);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(Animator animator, int stateNameHash)
        {
            animator.Play(stateNameHash);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateNameHash);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(Animator animator, int stateNameHash, int layer)
        {
            animator.Play(stateNameHash, layer);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateNameHash);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Play(Animator animator, int stateNameHash, int layer, float normalizedTime)
        {
            animator.Play(stateNameHash, layer, normalizedTime);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateNameHash);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayInFixedTime(Animator animator, string stateName)
        {
            animator.PlayInFixedTime(stateName);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayInFixedTime(Animator animator, string stateName, int layer)
        {
            animator.PlayInFixedTime(stateName, layer);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayInFixedTime(Animator animator, string stateName, int layer, float fixedTime)
        {
            animator.PlayInFixedTime(stateName, layer, fixedTime);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayInFixedTime(Animator animator, int stateNameHash)
        {
            animator.PlayInFixedTime(stateNameHash);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateNameHash);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayInFixedTime(Animator animator, int stateNameHash, int layer)
        {
            animator.PlayInFixedTime(stateNameHash, layer);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateNameHash);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PlayInFixedTime(Animator animator, int stateNameHash, int layer, float fixedTime)
        {
            animator.PlayInFixedTime(stateNameHash, layer, fixedTime);
            if (enabled)
                Called(animator, RecordedAnimatorCall.Play, stateNameHash);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFade(Animator animator, string stateName, float normalizedTransitionDuration)
        {
            animator.CrossFade(stateName, normalizedTransitionDuration);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFade(Animator animator, string stateName, float normalizedTransitionDuration, int layer)
        {
            animator.CrossFade(stateName, normalizedTransitionDuration, layer);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFade(Animator animator, string stateName, float normalizedTransitionDuration, int layer,
            float normalizedTimeOffset)
        {
            animator.CrossFade(stateName, normalizedTransitionDuration, layer, normalizedTimeOffset);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFade(Animator animator, string stateName, float normalizedTransitionDuration, int layer,
            float normalizedTimeOffset, float normalizedTransitionTime)
        {
            animator.CrossFade(stateName, normalizedTransitionDuration, layer, normalizedTimeOffset, normalizedTransitionTime);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFade(Animator animator, int stateHashName, float normalizedTransitionDuration)
        {
            animator.CrossFade(stateHashName, normalizedTransitionDuration);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateHashName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFade(Animator animator, int stateHashName, float normalizedTransitionDuration, int layer)
        {
            animator.CrossFade(stateHashName, normalizedTransitionDuration, layer);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateHashName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFade(Animator animator, int stateHashName, float normalizedTransitionDuration, int layer,
            float normalizedTimeOffset)
        {
            animator.CrossFade(stateHashName, normalizedTransitionDuration, layer, normalizedTimeOffset);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateHashName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFade(Animator animator, int stateHashName, float normalizedTransitionDuration, int layer,
            float normalizedTimeOffset, float normalizedTransitionTime)
        {
            animator.CrossFade(stateHashName, normalizedTransitionDuration, layer, normalizedTimeOffset, normalizedTransitionTime);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateHashName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFadeInFixedTime(Animator animator, string stateName, float fixedTransitionDuration)
        {
            animator.CrossFadeInFixedTime(stateName, fixedTransitionDuration);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFadeInFixedTime(Animator animator, string stateName, float fixedTransitionDuration, int layer)
        {
            animator.CrossFadeInFixedTime(stateName, fixedTransitionDuration, layer);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFadeInFixedTime(Animator animator, string stateName, float fixedTransitionDuration, int layer,
            float fixedTimeOffset)
        {
            animator.CrossFadeInFixedTime(stateName, fixedTransitionDuration, layer, fixedTimeOffset);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFadeInFixedTime(Animator animator, string stateName, float fixedTransitionDuration, int layer,
            float fixedTimeOffset, float normalizedTransitionTime)
        {
            animator.CrossFadeInFixedTime(stateName, fixedTransitionDuration, layer, fixedTimeOffset, normalizedTransitionTime);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFadeInFixedTime(Animator animator, int stateHashName, float fixedTransitionDuration)
        {
            animator.CrossFadeInFixedTime(stateHashName, fixedTransitionDuration);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateHashName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFadeInFixedTime(Animator animator, int stateHashName, float fixedTransitionDuration, int layer)
        {
            animator.CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, layer);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateHashName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFadeInFixedTime(Animator animator, int stateHashName, float fixedTransitionDuration, int layer,
            float fixedTimeOffset)
        {
            animator.CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, layer, fixedTimeOffset);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateHashName);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CrossFadeInFixedTime(Animator animator, int stateHashName, float fixedTransitionDuration, int layer,
            float fixedTimeOffset, float normalizedTransitionTime)
        {
            animator.CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, layer, fixedTimeOffset, normalizedTransitionTime);
            if (enabled)
                Called(animator, RecordedAnimatorCall.CrossFade, stateHashName);
        }
#endif
    }
}
