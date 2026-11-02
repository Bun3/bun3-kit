using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    /// <summary>Pins the Unity behaviour that device recovery relies on.</summary>
    public sealed class AudioResetBehaviourTests
    {
        [UnityTest]
        public IEnumerator Reset_StopsPlayingSourceAndReportsScriptReset()
        {
            var go = new GameObject("reset-probe");
            var clip = AudioClip.Create("reset-probe", 44100, 1, 44100, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            var calls = 0;
            var deviceWasChanged = true;
            AudioSettings.AudioConfigurationChangeHandler handler = changed => { calls++; deviceWasChanged = changed; };
            try
            {
                source.Play();
                yield return null;
                Assume.That(source.isPlaying, Is.True);
                AudioSettings.OnAudioConfigurationChanged += handler;
                AudioSettings.Reset(AudioSettings.GetConfiguration());
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(deviceWasChanged, Is.False);
                Assert.That(source.isPlaying, Is.False);
            }
            finally
            {
                AudioSettings.OnAudioConfigurationChanged -= handler;
                Object.Destroy(go);
                Object.Destroy(clip);
            }
        }
    }
}
