using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SourceCreatedHookTests
    {
        private static void SetHook(SoundSystemConfig config, Action<AudioSource> hook)
        {
            var field = typeof(SoundSystemConfig).GetField("OnSourceCreated");
            Assert.That(field, Is.Not.Null, "The pool needs a construction-time source preparation hook.");
            field.SetValue(config, hook);
        }

        [UnityTest]
        public IEnumerator PreparesEverySfxSourceOnceBeforePlayback()
        {
            var count = 0;
            var config = new SoundSystemConfig { SfxVoices = 2, OcclusionChecksPerFrame = 0 };
            SetHook(config, source =>
            {
                Assert.That(source.playOnAwake, Is.False);
                Assert.That(source.clip, Is.Null);
                source.gameObject.AddComponent<AudioLowPassFilter>();
                count++;
            });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var clip = AudioClip.Create("Prepared pool", 44100, 1, 44100, false);
            def.Clips = new[] { clip };
            try
            {
                using var system = new SoundSystem(config);
                Assert.That(count, Is.EqualTo(2));
                Assert.That(system.SourceForTest(0).GetComponent<AudioLowPassFilter>(), Is.Not.Null);
                Assert.That(system.SourceForTest(1).GetComponent<AudioLowPassFilter>(), Is.Not.Null);
                system.Play(def);
                system.Play(def);
                system.Play(def);
                Assert.That(count, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(clip);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrowingPreparationDisposesPartiallyCreatedPool()
        {
            var before = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length;
            var config = new SoundSystemConfig { SfxVoices = 2 };
            SetHook(config, _ => throw new InvalidOperationException("Preparation failed."));
            Assert.Throws<InvalidOperationException>(() => new SoundSystem(config));
            yield return null;
            Assert.That(Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length, Is.EqualTo(before));
        }
    }
}
