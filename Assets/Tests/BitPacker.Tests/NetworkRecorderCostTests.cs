#if PURRSERVICES_RECORDER
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Recording;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// What recording costs the main thread in a busy match: see <see cref="RecorderCostBenchmark"/>, which a built
/// player runs too. Explicit: run it by name on a quiet machine; it writes the recorder's own cost table to
/// test-local/recorder-cost. <c>-costLabel name</c> names the report.
/// </summary>
public class NetworkRecorderCostTests
{
    [UnityTest, Explicit]
    public IEnumerator BusyMatch()
    {
        yield return RecorderCostBenchmark.Run(Path.GetFullPath(Path.Combine(Application.dataPath, "../test-local/recorder-cost")));
        Assert.That(RecorderCostBenchmark.complete, Is.True, RecorderCostBenchmark.report);
    }
}

/// <summary>
/// What it costs to ask the engine, once per object, for the things a recorder could want beyond what it records
/// now: animation state, particle systems, audio sources. Next to the pose and visibility it already reads, so the
/// numbers can be compared. Explicit; writes test-local/recorder-cost/polling.txt.
/// </summary>
public class RecorderPollingCostTests
{
    const int Objects = 1000, Rounds = 200;

    [UnityTest, Explicit]
    public IEnumerator EngineCallsPerObject()
    {
        var created = new List<Object>();
#if UNITY_EDITOR
        const string controllerPath = "Assets/__RecorderPollingCost.controller";
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var clip = new AnimationClip { name = "Idle" };
        clip.SetCurve("", typeof(Transform), "localScale.x", AnimationCurve.Linear(0, 1, 1, 1));
        UnityEditor.AssetDatabase.AddObjectToAsset(clip, controller);
        controller.AddMotion(clip);
#endif
        try
        {
            var transforms = new List<Transform>();
            var renderers = new List<Renderer>();
            var animators = new List<Animator>();
            var particles = new List<ParticleSystem>();
            var audio = new List<AudioSource>();
            for (int i = 0; i < Objects; i++)
            {
                var go = new GameObject("PollingCost" + i);
                created.Add(go);
                go.transform.position = new Vector3(i, 0, 0);
                transforms.Add(go.transform);
                go.AddComponent<MeshFilter>();
                renderers.Add(go.AddComponent<MeshRenderer>());
                var animator = go.AddComponent<Animator>();
#if UNITY_EDITOR
                animator.runtimeAnimatorController = controller;
#endif
                animators.Add(animator);
                var system = go.AddComponent<ParticleSystem>();
                particles.Add(system);
                audio.Add(go.AddComponent<AudioSource>());
            }
            for (int i = 0; i < 5; i++)
                yield return null;

            var lines = new List<string>();
            var clips = new List<AnimatorClipInfo>(4);
            long sink = 0;
            void Measure(string name, System.Action<int> call)
            {
                for (int i = 0; i < Objects; i++)
                    call(i);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int r = 0; r < Rounds; r++)
                    for (int i = 0; i < Objects; i++)
                        call(i);
                clock.Stop();
                lines.Add($"{name,-46} {clock.Elapsed.TotalMilliseconds * 1e6 / (Rounds * Objects),7:0} ns");
            }

            Measure("(empty loop)", i => sink += i);
            Measure("transform.GetPositionAndRotation", i => { transforms[i].GetPositionAndRotation(out var p, out var q); sink += (long)p.x; });
            Measure("transform.lossyScale", i => sink += (long)transforms[i].lossyScale.x);
            Measure("renderer alive + enabled + activeInHierarchy", i => { var r = renderers[i]; if (r && r.enabled && r.gameObject.activeInHierarchy) sink++; });
            Measure("animator.GetCurrentAnimatorStateInfo(0)", i => sink += animators[i].GetCurrentAnimatorStateInfo(0).fullPathHash);
            Measure("animator.IsInTransition(0)", i => { if (animators[i].IsInTransition(0)) sink++; });
            Measure("animator.GetCurrentAnimatorClipInfo(0, list)", i => { animators[i].GetCurrentAnimatorClipInfo(0, clips); sink += clips.Count; });
            Measure("particleSystem.isPlaying", i => { if (particles[i].isPlaying) sink++; });
            Measure("particleSystem.isEmitting", i => { if (particles[i].isEmitting) sink++; });
            Measure("particleSystem.particleCount", i => sink += particles[i].particleCount);
            Measure("audioSource.isPlaying", i => { if (audio[i].isPlaying) sink++; });
            Measure("audioSource.timeSamples", i => sink += audio[i].timeSamples);
            Measure("animator alive", i => { if (animators[i]) sink++; });
            Measure("animator.isInitialized", i => { if (animators[i].isInitialized) sink++; });
            Measure("animator.isActiveAndEnabled", i => { if (animators[i].isActiveAndEnabled) sink++; });
            Measure("animator.layerCount", i => sink += animators[i].layerCount);
            Measure("animator.speed", i => sink += (long)animators[i].speed);
            Measure("animator.GetNextAnimatorStateInfo(0)", i => sink += animators[i].GetNextAnimatorStateInfo(0).fullPathHash);
            Measure("particleSystem.time", i => sink += (long)particles[i].time);
            Measure("audioSource.volume", i => sink += (long)audio[i].volume);
            Measure("audioSource.pitch", i => sink += (long)audio[i].pitch);
            Measure("audioSource.loop", i => { if (audio[i].loop) sink++; });
            Measure("audioSource.clip", i => { if (audio[i].clip) sink++; });
            Measure("one object: animator + particles + audio", i =>
            {
                var animator = animators[i];
                if (animator && animator.isActiveAndEnabled && animator.isInitialized)
                {
                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    if (animator.IsInTransition(0)) sink++;
                    sink += info.fullPathHash;
                }
                var system = particles[i];
                if (system && system.isPlaying) sink++;
                var source = audio[i];
                if (source && source.isPlaying) sink++;
            });
            Measure("one kind at a time: animators", i =>
            {
                var animator = animators[i];
                if (animator && animator.isActiveAndEnabled && animator.isInitialized)
                {
                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    if (animator.IsInTransition(0)) sink++;
                    sink += info.fullPathHash;
                }
            });
            Measure("one kind at a time: particles", i => { var system = particles[i]; if (system && system.isPlaying) sink++; });
            Measure("one kind at a time: audio", i => { var source = audio[i]; if (source && source.isPlaying) sink++; });
            var foundAnimators = new List<Animator>();
            var foundParticles = new List<ParticleSystem>();
            var foundAudio = new List<AudioSource>();
            Measure("GetComponentsInChildren x3 (one object)", i =>
            {
                var go = transforms[i].gameObject;
                go.GetComponentsInChildren(true, foundAnimators);
                go.GetComponentsInChildren(true, foundParticles);
                go.GetComponentsInChildren(true, foundAudio);
                sink += foundAnimators.Count + foundParticles.Count + foundAudio.Count;
            });
            var foundBehaviours = new List<Component>();
            Measure("GetComponentsInChildren<Component> (one object)", i =>
            {
                transforms[i].gameObject.GetComponentsInChildren(true, foundBehaviours);
                sink += foundBehaviours.Count;
            });

            string report = $"per call, {Objects} objects, {Rounds} rounds, {(Application.isEditor ? "editor (Mono)" : "player")}, checksum {sink}\n" + string.Join("\n", lines);
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../test-local/recorder-cost"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "polling.txt"), report);
            Debug.Log(report);
        }
        finally
        {
            foreach (var o in created)
                if (o)
                    Object.DestroyImmediate(o);
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.DeleteAsset(controllerPath);
#endif
        }
    }
}
#endif
