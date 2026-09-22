using System;
using System.Linq;
using System.Reflection;
using global::Dissonance.Audio.Playback;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance.Tests
{
    public class DissonanceOutputBridgeTests
    {
        [Test]
        public void PlaybackPrefabHasAnOutputOwnershipBridge()
        {
            var bridge = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Bun3.Unity.Audio.Dissonance.DissonanceOutputBridge"))
                .FirstOrDefault(type => type != null);
            Assert.That(bridge, Is.Not.Null,
                "Dissonance playback requires an adapter that binds external ownership to each enabled playback lifetime.");
        }

        [Test]
        public void DisabledLifetimeRestoresGainAndReusedLifetimeResetsDiagnostics()
        {
            var type = FindBridge();
            var go = new GameObject("Playback");
            using var registry = new ExternalAudioRegistry(1);
            try
            {
                go.SetActive(false);
                var source = go.AddComponent<AudioSource>();
                go.AddComponent<SamplePlaybackComponent>();
                go.AddComponent<VoicePlayback>();
                var bridge = (MonoBehaviour)go.AddComponent(type);
                source.volume = 0.8f;
                type.GetMethod("Bind").Invoke(bridge, new object[] { registry, new ExternalAudioSettings(gain: 0.4f) });
                go.SetActive(true);
                type.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
                Assert.That(source.volume, Is.EqualTo(0.4f));
                ((IAudioOutputSubscriber)bridge).OnAudioPlayback(new ArraySegment<float>(new float[3]), false);
                Assert.That(type.GetProperty("ReceivedSamples").GetValue(bridge), Is.EqualTo(3L));
                type.GetMethod("SetGain").Invoke(bridge, new object[] { 0.2f });
                type.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
                Assert.That(source.volume, Is.EqualTo(0.8f));
                ((IAudioOutputSubscriber)bridge).OnAudioPlayback(new ArraySegment<float>(new float[7]), true);
                type.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
                Assert.That(source.volume, Is.EqualTo(0.4f));
                Assert.That(type.GetProperty("ReceivedSamples").GetValue(bridge), Is.EqualTo(0L));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void BindAndUnbindPreserveSdkPlaybackFieldsAndSubscriberDoesNotChangePcm()
        {
            var type = FindBridge();
            var go = new GameObject("Playback");
            var clip = AudioClip.Create("Fixture", 64, 1, 48000, false);
            using var registry = new ExternalAudioRegistry(1);
            try
            {
                go.SetActive(false);
                var source = go.AddComponent<AudioSource>();
                var samples = go.AddComponent<SamplePlaybackComponent>();
                go.AddComponent<VoicePlayback>();
                var bridge = (MonoBehaviour)go.AddComponent(type);
                go.SetActive(true);
                source.clip = clip;
                source.pitch = 1.2f;
                source.loop = false;
                source.mute = true;
                source.spatialBlend = 0.6f;
                source.transform.position = new Vector3(2, 3, 4);
                type.GetMethod("Bind").Invoke(bridge, new object[] { registry, new ExternalAudioSettings(gain: 0.3f) });
                samples.Start();
                var cached = (IAudioOutputSubscriber[])typeof(SamplePlaybackComponent)
                    .GetField("_subscribers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(samples);
                Assert.That(cached, Does.Contain((IAudioOutputSubscriber)bridge));
                var pcm = new[] { 0.25f, -0.5f, 0.75f };
                foreach (var subscriber in cached)
                    subscriber.OnAudioPlayback(new ArraySegment<float>(pcm, 1, 2), true);
                Assert.That(pcm, Is.EqualTo(new[] { 0.25f, -0.5f, 0.75f }));
                Assert.That(type.GetProperty("ReceivedSamples").GetValue(bridge), Is.EqualTo(2L));
                type.GetMethod("Unbind").Invoke(bridge, null);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.pitch, Is.EqualTo(1.2f));
                Assert.That(source.loop, Is.False);
                Assert.That(source.mute, Is.True);
                Assert.That(source.spatialBlend, Is.EqualTo(0.6f));
                Assert.That(source.transform.position, Is.EqualTo(new Vector3(2, 3, 4)));
            }
            finally
            {
                go.GetComponent<AudioSource>().clip = null;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        private static Type FindBridge()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Bun3.Unity.Audio.Dissonance.DissonanceOutputBridge"))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, "The output ownership bridge is missing.");
            return type;
        }
    }
}
