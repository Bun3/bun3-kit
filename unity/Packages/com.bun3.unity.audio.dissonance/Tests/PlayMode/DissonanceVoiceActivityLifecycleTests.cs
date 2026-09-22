using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using global::Dissonance;
using global::Dissonance.VAD;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Dissonance.Tests.PlayMode
{
    public class DissonanceVoiceActivityLifecycleTests
    {
        [UnityTest]
        public IEnumerator ActualSdkSubscriptionIsRemovedAndPlaybackMuteDoesNotSuppressActivity()
        {
            var type = Type.GetType("Bun3.Unity.Audio.Dissonance.DissonanceVoiceActivityScope, Bun3.Unity.Audio.Dissonance");
            Assert.That(type, Is.Not.Null, "The VAD scope must subscribe to the installed SDK.");
            var go = new GameObject("VAD subscription test");
            go.SetActive(false);
            var originalVolume = AudioListener.volume;
            IDisposable scope = null;
            try
            {
                var comms = go.AddComponent<DissonanceComms>();
                var capture = typeof(DissonanceComms).GetField("_capture", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(comms);
                var listeners = (List<IVoiceActivationListener>)capture.GetType()
                    .GetField("_activationListeners", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(capture);
                var originalCount = listeners.Count;
                scope = (IDisposable)Activator.CreateInstance(type, new object[] { comms });
                Assert.That(listeners.Count, Is.EqualTo(originalCount + 1));
                var listener = listeners[listeners.Count - 1];
                AudioListener.volume = 0;
                listener.VoiceActivationStart();
                var snapshot = type.GetMethod("Poll").Invoke(scope, null);
                Assert.That(snapshot.GetType().GetProperty("IsSpeechActive").GetValue(snapshot), Is.True);
                yield return null;
                scope.Dispose();
                Assert.That(listeners.Count, Is.EqualTo(originalCount));
                listener.VoiceActivationStart();
                snapshot = type.GetMethod("Poll").Invoke(scope, null);
                Assert.That(snapshot.GetType().GetProperty("IsSpeechActive").GetValue(snapshot), Is.False);
            }
            finally
            {
                scope?.Dispose();
                AudioListener.volume = originalVolume;
                UnityEngine.Object.Destroy(go);
            }
        }
    }
}
