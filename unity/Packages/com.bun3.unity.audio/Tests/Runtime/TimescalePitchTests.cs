using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class TimescalePitchTests
    {
        private static SoundDef LoopDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("ts", 4410, 1, 44100, false) };
            def.Loop = true;
            def.Pitch = new FloatRange(1f, 1f);
            return def;
        }

        [UnityTest]
        public IEnumerator TimescaleChange_ScalesSfxPitch_AndRestores()
        {
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 2,
                PitchWithTimescale = true,
            });
            var h = sys.Play(LoopDef());
            try
            {
                Time.timeScale = 0.5f;
                sys.Tick(0.02f);
                Assert.That(sys.SourcePitchForTest(0), Is.EqualTo(0.5f).Within(0.001f));
                Time.timeScale = 1f;
                sys.Tick(0.02f);
                Assert.That(sys.SourcePitchForTest(0), Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Time.timeScale = 1f;
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator FlagOff_PitchUntouched()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.Play(LoopDef());
            try
            {
                Time.timeScale = 0.5f;
                sys.Tick(0.02f);
                Assert.That(sys.SourcePitchForTest(0), Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Time.timeScale = 1f;
            }
            yield break;
        }
    }
}
