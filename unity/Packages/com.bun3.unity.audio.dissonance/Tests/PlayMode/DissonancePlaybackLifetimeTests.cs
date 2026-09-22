using System;
using System.Collections;
using global::Dissonance.Audio.Playback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Dissonance.Tests.PlayMode
{
    public class DissonancePlaybackLifetimeTests
    {
        [UnityTest]
        public IEnumerator SdkPlaybackCanBeDisabledReusedAndDestroyedWithoutLeakingOwnership()
        {
            var go = new GameObject("Playback lifetime test");
            go.SetActive(false);
            var registry = new ExternalAudioRegistry(1);
            try
            {
                var source = go.AddComponent<AudioSource>();
                go.AddComponent<SamplePlaybackComponent>();
                go.AddComponent<VoicePlayback>();
                var bridge = go.AddComponent<DissonanceOutputBridge>();
                source.volume = 0.8f;
                bridge.Bind(registry, new ExternalAudioSettings(gain: 0.4f));
                go.SetActive(true);
                yield return null;
                Assert.That(bridge.TryGetReadiness(out _), Is.True);
                Assert.That(source.clip, Is.Not.Null);
                Assert.That(source.volume, Is.EqualTo(0.4f));
                bridge.SetGain(0.2f);
                ((IAudioOutputSubscriber)bridge).OnAudioPlayback(new ArraySegment<float>(new float[4]), false);
                Assert.That(bridge.ReceivedSamples, Is.EqualTo(4));
                go.SetActive(false);
                Assert.That(source.volume, Is.EqualTo(0.8f));
                Assert.That(bridge.IsRegistered, Is.False);
                yield return null;
                go.SetActive(true);
                yield return null;
                Assert.That(bridge.IsRegistered, Is.True);
                Assert.That(bridge.ReceivedSamples, Is.Zero);
                Assert.That(source.volume, Is.EqualTo(0.4f));
                UnityEngine.Object.Destroy(go);
                yield return null;
                var replacement = new GameObject("Replacement output");
                try
                {
                    Assert.That(registry.Register(replacement.AddComponent<AudioSource>(), default).IsValid, Is.True);
                }
                finally { UnityEngine.Object.Destroy(replacement); }
            }
            finally
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                registry.Dispose();
            }
        }
    }
}
